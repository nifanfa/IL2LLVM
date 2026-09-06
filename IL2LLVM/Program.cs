using LLVMSharp.Interop;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System.Diagnostics;

string fileName = "../../../../ConsoleApp1/bin/Debug/net10.0/ConsoleApp1.dll";

LLVM.InitializeAllTargetInfos();
LLVM.InitializeAllTargets();
LLVM.InitializeAllTargetMCs();
LLVM.InitializeAllAsmParsers();
LLVM.InitializeAllAsmPrinters();

var context = LLVMContextRef.Global;
var module = context.CreateModuleWithName(Path.GetFileNameWithoutExtension(fileName));

module.Target = "i386-pc-windows-msvc";

var target = LLVMTargetRef.GetTargetFromTriple(module.Target);
var machine = target.CreateTargetMachine(module.Target, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
                                         LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);

int pointerSize = (int)machine.CreateTargetDataLayout().ABISizeOfType(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
LLVMTypeRef sizeType = LLVMTypeRef.CreateIntPtr(machine.CreateTargetDataLayout());

Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>> moduleMethods = new();
Dictionary<RuntimeMethod, Tuple<LLVMValueRef, LLVMTypeRef>> runtimeMethods = new();
Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef>> staticFields = new();
Dictionary<string, LLVMValueRef> stringConstants = new(StringComparer.Ordinal);
Dictionary<string, TypeDefinition> localTypes = new(StringComparer.Ordinal);
Dictionary<string, ulong> runtimeTypeIds = new(StringComparer.Ordinal);
ulong nextRuntimeTypeId = 1;
int nextVirtualDispatchId = 0;
var exceptionPointerType = LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
var exceptionPushType = LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, [exceptionPointerType, exceptionPointerType]);
var exceptionPushFunction = module.AddFunction("RuntimeExceptionPush", exceptionPushType);
var exceptionPopType = LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, [exceptionPointerType]);
var exceptionPopFunction = module.AddFunction("RuntimeExceptionPop", exceptionPopType);
var exceptionBufferType = LLVMTypeRef.CreateFunction(exceptionPointerType, [exceptionPointerType]);
var exceptionBufferFunction = module.AddFunction("RuntimeExceptionBuffer", exceptionBufferType);
var exceptionCurrentType = LLVMTypeRef.CreateFunction(exceptionPointerType, []);
var exceptionCurrentFunction = module.AddFunction("RuntimeExceptionCurrent", exceptionCurrentType);
var exceptionThrowType = LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, [exceptionPointerType]);
var exceptionThrowFunction = module.AddFunction("RuntimeExceptionThrow", exceptionThrowType);
var setjmpType = LLVMTypeRef.CreateFunction(LLVMTypeRef.Int32, [exceptionPointerType, LLVMTypeRef.Int32], true);
var setjmpFunction = module.AddFunction("_setjmp3", setjmpType);

{
    var funcType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), [sizeType]);
    var funcValue = module.AddFunction(RuntimeMethod.Newobj.ToString(), funcType);
    runtimeMethods.Add(RuntimeMethod.Newobj, new(funcValue, funcType));
}

{
    var funcType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), [sizeType, sizeType, sizeType]);
    var funcValue = module.AddFunction(RuntimeMethod.Newarr.ToString(), funcType);
    runtimeMethods.Add(RuntimeMethod.Newarr, new(funcValue, funcType));
}

{
    var assembly = AssemblyDefinition.ReadAssembly(fileName);
    Dictionary<string, MethodDefinition> localMethods = GetAllTypes(assembly.MainModule.Types)
        .SelectMany(t => t.Methods)
        .ToDictionary(m => m.FullName, StringComparer.Ordinal);
    Dictionary<string, MethodDefinition> localMethodsByFriendlyName = localMethods.Values
        .ToDictionary(m => GetFriendlyMethodName(m), StringComparer.Ordinal);
    localTypes = GetAllTypes(assembly.MainModule.Types)
        .ToDictionary(t => t.FullName, StringComparer.Ordinal);

    HashSet<string> reachableMethods = new(StringComparer.Ordinal);
    Queue<MethodDefinition> pendingMethods = new();
    HashSet<string> rootMethods = new(StringComparer.Ordinal);
    if (assembly.EntryPoint is not null)
        rootMethods.Add(assembly.EntryPoint.FullName);
    foreach (var method in localMethods.Values.Where(method =>
        method.IsPublic && method.IsStatic && method.Parameters.Count == 0 &&
        method.ReturnType.MetadataType == MetadataType.Void))
        rootMethods.Add(method.FullName);

    foreach (var rootMethod in localMethods.Values.Where(method => rootMethods.Contains(method.FullName)))
    {
        reachableMethods.Add(rootMethod.FullName);
        pendingMethods.Enqueue(rootMethod);
    }
    foreach (var cctor in localMethods.Values.Where(method => method.Name == ".cctor" && method.HasBody))
    {
        if (reachableMethods.Add(cctor.FullName))
            pendingMethods.Enqueue(cctor);
    }

    while (pendingMethods.Count != 0)
    {
        var current = pendingMethods.Dequeue();
        if (!current.HasBody || current.Body is null)
            continue;
        foreach (var instruction in current.Body.Instructions ?? [])
        {
            if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn))
                continue;
            var reference = (MethodReference)instruction.Operand;
            var localTargetMethod = ResolveCallTarget(reference);
            var localTarget = FindLocalMethod(localTargetMethod, localMethods);
            if (localTarget is not null && reachableMethods.Add(localTarget.FullName))
                pendingMethods.Enqueue(localTarget);
        }
    }

    foreach (TypeDefinition type in GetAllTypes(assembly.MainModule.Types))
    {
        var fields = type.Fields;
        if (fields.Any())
        {
            foreach (var field in fields)
            {
                if (field.IsStatic)
                {
                    string fieldName = GetFriendlyFieldName(field);
                    var fieldType = GetLLVMTypeRef(field.FieldType);
                    var fieldValue = module.AddGlobal(fieldType, fieldName);
                    fieldValue.Initializer = LLVMValueRef.CreateConstNull(fieldType);

                    staticFields.Add(fieldName, new(fieldValue, fieldType));
                }
            }
        }

        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            if (!reachableMethods.Contains(method.FullName))
                continue;
            RegisterMethodFunction(module, method, method.Body.Instructions);

            foreach (var instr in method.Body.Instructions)
            {
                switch (instr.OpCode.Code)
                {
                    case Code.Call:
                    case Code.Calli:
                    case Code.Callvirt:
                    case Code.Newobj:
                    case Code.Ldftn:
                    case Code.Ldvirtftn:
                        if (instr.OpCode.Code == Code.Calli)
                            break;
                        MethodReference targetMethod = (MethodReference)instr.Operand;
                        MethodReference callTarget = ResolveCallTarget(targetMethod);
                        var localTarget = FindLocalMethod(targetMethod, localMethods);
                        var instructions = localTarget?.HasBody == true ? localTarget.Body.Instructions : new();
                        RegisterMethodFunction(module, targetMethod, instructions);
                        if (!ReferenceEquals(callTarget, targetMethod))
                        {
                            var targetInstructions = localMethods.TryGetValue(callTarget.FullName, out var implementation) && implementation.HasBody
                                ? implementation.Body.Instructions
                                : new();
                            RegisterMethodFunction(module, callTarget, targetInstructions);
                        }
                        if (instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis)
                        {
                            foreach (var candidateType in localTypes.Values.Where(candidate => !candidate.IsInterface))
                            {
                                var implementation = FindMethodImplementation(candidateType, targetMethod);
                                if (implementation is not null)
                                    RegisterMethodFunction(module, implementation, implementation.Body?.Instructions);
                            }
                        }
                        break;
                }
            }
        }
    }

    Queue<MethodReference> pendingReferences = new(moduleMethods.Values
        .SelectMany(method => method.Item4 ?? [])
        .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
        .Select(instruction => (MethodReference)instruction.Operand));
    HashSet<string> processedReferences = new(StringComparer.Ordinal);
    while (pendingReferences.Count != 0)
    {
        var reference = pendingReferences.Dequeue();
        if (!processedReferences.Add(reference.FullName))
            continue;
        var localTarget = FindLocalMethod(reference, localMethods);
        if (localTarget is null)
            continue;
        var instructions = localTarget.HasBody ? localTarget.Body.Instructions : new();
        var before = moduleMethods.Count;
        RegisterMethodFunction(module, reference, instructions);
        var callTarget = ResolveCallTarget(reference);
        if (!ReferenceEquals(callTarget, reference))
            RegisterMethodFunction(module, callTarget, localMethods.TryGetValue(callTarget.FullName, out var implementation) && implementation.HasBody
                ? implementation.Body.Instructions
                : new());
        if (moduleMethods.Count != before || localTarget.HasBody)
        {
            foreach (var instruction in instructions.Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn))
                pendingReferences.Enqueue((MethodReference)instruction.Operand);
        }
        if (reference.Resolve()?.IsVirtual == true || reference.Resolve()?.IsAbstract == true)
        {
            foreach (var candidateType in localTypes.Values.Where(candidate => !candidate.IsInterface))
            {
                var implementation = FindMethodImplementation(candidateType, reference);
                if (implementation is null)
                    continue;
                RegisterMethodFunction(module, implementation, implementation.Body?.Instructions);
                foreach (var instruction in implementation.Body?.Instructions ?? [])
                    if (instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
                        pendingReferences.Enqueue((MethodReference)instruction.Operand);
            }
        }
    }

    foreach (var arrayEnumeratorType in localTypes.Values.Where(type => type.Name.StartsWith("ArrayEnumerator`", StringComparison.Ordinal)))
        foreach (var method in arrayEnumeratorType.Methods.Where(method => method.HasBody))
            RegisterMethodFunction(module, method, method.Body.Instructions);

    LLVMBuilderRef entryBuilder = default;
    foreach (var method in moduleMethods)
    {
        if (method.Value.Item4.Any())
        {
            Console.WriteLine($"Method: {method.Value.Item3}, FriendlyMethodName: {GetFriendlyMethodName(method.Value.Item3)}");
            var allocaBlock = method.Value.Item1.AppendBasicBlock("alloca");
            var entry = method.Value.Item1.AppendBasicBlock(GetLabelName(method.Value.Item4.First()));
            var builder = context.CreateBuilder();
            entryBuilder = context.CreateBuilder();
            entryBuilder.PositionAtEnd(allocaBlock);
            builder.PositionAtEnd(entry);
            {
                Stack<LLVMValueRef> stack = new();
                Dictionary<LLVMBasicBlockRef, List<(LLVMBasicBlockRef Source, List<LLVMValueRef> Values)>> incomingStacks = new();
                Dictionary<LLVMBasicBlockRef, Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>>> spillSlots = new();
                Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>> local = new();
                Dictionary<LLVMValueRef, TypeReference> trackedTypes = new();
                Dictionary<int, TypeReference> localRuntimeTypes = new();
                HashSet<LLVMBasicBlockRef> terminatedBlocks = new();
                Dictionary<LLVMBasicBlockRef, LLVMBasicBlockRef> finallyContinuations = new();
                List<ExceptionRegion> exceptionRegions = new();
                Dictionary<ExceptionHandler, (LLVMValueRef Slot, Dictionary<int, LLVMBasicBlockRef> Targets)> finallyStates = new();
                Dictionary<int, (LLVMBasicBlockRef Handler, LLVMBasicBlockRef Next)> filterStates = new();
                Dictionary<ExceptionHandler, LLVMValueRef> caughtExceptions = new();
                SortedDictionary<int, LLVMBasicBlockRef> label = new();
                int nextFinallyContinuation = 1;
                TypeReference? constrainedType = null;

                void TrackType(LLVMValueRef value, TypeReference type)
                {
                    if (value != default)
                        trackedTypes[value] = type;
                }

                LLVMValueRef BuildVirtualDispatch(MethodReference targetMethod, LLVMValueRef[] targetArgs,
                    LLVMTypeRef targetFunctionType, LLVMValueRef targetFunction, List<(TypeDefinition RuntimeType, MethodReference Implementation)> implementations,
                    bool allowArraySpecial = true)
                {
                    if (allowArraySpecial && targetMethod.Name == "GetEnumerator" && targetMethod.DeclaringType is GenericInstanceType enumerableType &&
                        enumerableType.ElementType.FullName == "System.Collections.Generic.IEnumerable`1")
                    {
                        var arrayEnumeratorDefinition = localTypes.Values.First(type => type.Name.StartsWith("ArrayEnumerator`", StringComparison.Ordinal));
                        var constructor = arrayEnumeratorDefinition.Methods.First(methodDefinition => methodDefinition.Name == ".ctor" && methodDefinition.Parameters.Count == 1);
                        var constructorMethod = moduleMethods[GetFriendlyMethodName(constructor)];
                        var arrayReceiverValue = targetArgs[0];
                        var arrayMethodTable = builder.BuildLoad2(sizeType,
                            GetFieldAddress(builder, arrayReceiverValue, GetObjectMethodTableField()));
                        var arrayType = new ArrayType(SubstituteGenericParameter(enumerableType.GenericArguments[0], method.Value.Item3));
                        var arrayId = LLVMValueRef.CreateConstInt(sizeType, GetRuntimeTypeId(arrayType), false);
                        var arrayBlock = method.Value.Item1.AppendBasicBlock($"array.enum.{nextVirtualDispatchId++}");
                        var fallbackBlock = method.Value.Item1.AppendBasicBlock($"array.enum.next.{nextVirtualDispatchId++}");
                        var arrayContinuation = method.Value.Item1.AppendBasicBlock($"array.enum.cont.{nextVirtualDispatchId++}");
                        var arraySourceBlock = builder.InsertBlock;
                        builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, arrayMethodTable, arrayId), arrayBlock, fallbackBlock);
                        terminatedBlocks.Add(arraySourceBlock);
                        builder.PositionAtEnd(arrayBlock);
                        var enumerator = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2,
                            runtimeMethods[RuntimeMethod.Newobj].Item1,
                            [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(arrayEnumeratorDefinition), false)]);
                        var enumeratorType = new GenericInstanceType(arrayEnumeratorDefinition);
                        enumeratorType.GenericArguments.Add(enumerableType.GenericArguments[0]);
                        InitializeRuntimeType(builder, enumerator, arrayEnumeratorDefinition);
                        builder.BuildCall2(constructorMethod.Item2, constructorMethod.Item1, [enumerator, arrayReceiverValue]);
                        builder.BuildBr(arrayContinuation);
                        builder.PositionAtEnd(fallbackBlock);
                        var arrayFallback = BuildVirtualDispatch(targetMethod, targetArgs, targetFunctionType, targetFunction, implementations, false);
                        var arrayFallbackSource = builder.InsertBlock;
                        builder.BuildBr(arrayContinuation);
                        builder.PositionAtEnd(arrayContinuation);
                        var result = builder.BuildPhi(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), "array.enum.result");
                        result.AddIncoming([enumerator, arrayFallback], [arrayBlock, arrayFallbackSource], 2);
                        return result;
                    }

                    var distinctImplementations = implementations
                        .Select(candidate => GetFriendlyMethodName(candidate.Implementation))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (implementations.Count == 0 || distinctImplementations.Count <= 1)
                        return builder.BuildCall2(targetFunctionType, targetFunction, targetArgs);

                    var receiver = targetArgs[0];
                    var methodTable = builder.BuildLoad2(sizeType,
                        GetFieldAddress(builder, receiver, GetObjectMethodTableField()));
                    var continuation = method.Value.Item1.AppendBasicBlock($"virt.cont.{nextVirtualDispatchId++}");
                    var incomingValues = new List<LLVMValueRef>();
                    var incomingBlocks = new List<LLVMBasicBlockRef>();
                    var sourceBlock = builder.InsertBlock;
                    var sourceTerminated = false;

                    for (int i = 0; i < implementations.Count; i++)
                    {
                        var candidate = implementations[i];
                        var implementationName = GetFriendlyMethodName(candidate.Implementation);
                        if (!moduleMethods.TryGetValue(implementationName, out var implementationMethod))
                            continue;
                        var callBlock = method.Value.Item1.AppendBasicBlock($"virt.call.{nextVirtualDispatchId++}");
                        var nextBlock = method.Value.Item1.AppendBasicBlock($"virt.next.{nextVirtualDispatchId++}");
                        var candidateId = LLVMValueRef.CreateConstInt(sizeType, GetRuntimeTypeId(candidate.RuntimeType), false);
                        var matches = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, methodTable, candidateId);
                        builder.BuildCondBr(matches, callBlock, nextBlock);
                        if (!sourceTerminated)
                        {
                            terminatedBlocks.Add(sourceBlock);
                            sourceTerminated = true;
                        }

                        builder.PositionAtEnd(callBlock);
                        var result = builder.BuildCall2(implementationMethod.Item2, implementationMethod.Item1, targetArgs);
                        if (targetMethod.ReturnType.MetadataType != MetadataType.Void)
                        {
                            incomingValues.Add(ConvertValue(builder, result, GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod))));
                            incomingBlocks.Add(callBlock);
                        }
                        builder.BuildBr(continuation);
                        builder.PositionAtEnd(nextBlock);
                    }

                    var fallback = builder.BuildCall2(targetFunctionType, targetFunction, targetArgs);
                    if (targetMethod.ReturnType.MetadataType != MetadataType.Void)
                    {
                        incomingValues.Add(ConvertValue(builder, fallback,
                            GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod))));
                        incomingBlocks.Add(builder.InsertBlock);
                    }
                    builder.BuildBr(continuation);
                    builder.PositionAtEnd(continuation);
                    if (targetMethod.ReturnType.MetadataType == MetadataType.Void)
                        return default;
                    var phi = builder.BuildPhi(GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)), "virt.result");
                    phi.AddIncoming(incomingValues.ToArray(), incomingBlocks.ToArray(), (uint)incomingValues.Count);
                    return phi;
                }

                LLVMValueRef BuildEntryAlloca(LLVMTypeRef type)
                {
                    entryBuilder.PositionAtEnd(allocaBlock);
                    return entryBuilder.BuildAlloca(type);
                }

                void SaveStack(LLVMBasicBlockRef block)
                {
                    if (!incomingStacks.TryGetValue(block, out var values))
                    {
                        values = new();
                        incomingStacks[block] = values;
                    }
                    var stackValues = stack.Reverse().ToList();
                    values.Add((builder.InsertBlock, stackValues));
                    if (!spillSlots.TryGetValue(block, out var slots))
                    {
                        slots = new();
                        spillSlots[block] = slots;
                    }
                    for (int i = 0; i < stackValues.Count; i++)
                    {
                        var value = stackValues[i];
                        if (!slots.TryGetValue(i, out var slot))
                        {
                            var storage = BuildEntryAlloca(value.TypeOf);
                            slot = new(storage, value.TypeOf);
                            slots[i] = slot;
                        }
                        builder.BuildStore(ConvertValue(builder, value, slot.Item2), slot.Item1);
                    }
                }

                void RestoreStack(LLVMBasicBlockRef block)
                {
                    stack.Clear();
                    if (!incomingStacks.TryGetValue(block, out var incoming) || incoming.Count == 0)
                        return;
                    var depth = incoming.Max(item => item.Values.Count);
                    if (!spillSlots.TryGetValue(block, out var slots))
                        return;
                    for (int i = 0; i < depth; i++)
                    {
                        if (slots.TryGetValue(i, out var slot))
                            stack.Push(builder.BuildLoad2(slot.Item2, slot.Item1));
                    }
                }

                void SaveException(LLVMBasicBlockRef block, LLVMValueRef exception)
                {
                    if (!spillSlots.TryGetValue(block, out var slots))
                    {
                        slots = new();
                        spillSlots[block] = slots;
                    }
                    if (!slots.TryGetValue(0, out var slot))
                    {
                        var storage = BuildEntryAlloca(exceptionPointerType);
                        slot = new(storage, exceptionPointerType);
                        slots[0] = slot;
                    }
                    builder.BuildStore(exception, slot.Item1);
                    if (!incomingStacks.TryGetValue(block, out var incoming))
                    {
                        incoming = new();
                        incomingStacks[block] = incoming;
                    }
                    incoming.Add((builder.InsertBlock, [exception]));
                }

                LLVMValueRef BuildExceptionMatch(LLVMValueRef exception, TypeReference? targetType)
                {
                    if (targetType is null)
                        return LLVMValueRef.CreateConstInt(LLVMTypeRef.Int1, 1, false);
                    var matches = new List<LLVMValueRef>();
                    foreach (var candidate in localTypes.Values.Where(type => !type.IsInterface && !type.IsValueType))
                    {
                        if (!IsRuntimeTypeCompatible(candidate, targetType))
                            continue;
                        matches.Add(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ,
                            builder.BuildLoad2(sizeType, GetFieldAddress(builder, exception, GetObjectMethodTableField())),
                            LLVMValueRef.CreateConstInt(sizeType, GetRuntimeTypeId(candidate), false)));
                    }
                    if (matches.Count == 0)
                        return LLVMValueRef.CreateConstInt(LLVMTypeRef.Int1, 0, false);
                    var result = matches[0];
                    for (int i = 1; i < matches.Count; i++)
                        result = builder.BuildOr(result, matches[i]);
                    return result;
                }

                void RegisterFinallyContinuation(ExceptionHandler handler, LLVMBasicBlockRef target)
                {
                    var state = finallyStates[handler];
                    int id = nextFinallyContinuation++;
                    state.Targets[id] = target;
                    builder.BuildStore(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, (uint)id, false), state.Slot);
                    finallyStates[handler] = state;
                }

                void EmitExceptionSetup(ExceptionRegion region)
                {
                    var source = builder.InsertBlock;
                    var normal = method.Value.Item1.AppendBasicBlock($"eh.normal.{nextVirtualDispatchId++}");
                    var dispatch = method.Value.Item1.AppendBasicBlock($"eh.dispatch.{nextVirtualDispatchId++}");
                    builder.BuildCall2(exceptionPushType, exceptionPushFunction, [region.Frame, region.Buffer]);
                    var jumpResult = builder.BuildCall2(setjmpType, setjmpFunction,
                        [builder.BuildCall2(exceptionBufferType, exceptionBufferFunction, [region.Frame]),
                         LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0, false)]);
                    builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, jumpResult,
                        LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0, false)), normal, dispatch);
                    terminatedBlocks.Add(source);

                    builder.PositionAtEnd(dispatch);
                    builder.BuildCall2(exceptionPopType, exceptionPopFunction, [region.Frame]);
                    var exception = builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, []);
                    var chain = dispatch;
                    for (int index = 0; index < region.Handlers.Count; index++)
                    {
                        var handler = region.Handlers[index];
                        builder.PositionAtEnd(chain);
                        var next = method.Value.Item1.AppendBasicBlock($"eh.next.{nextVirtualDispatchId++}");
                        if (handler.HandlerType == ExceptionHandlerType.Filter)
                        {
                            var filterBlock = label[handler.FilterStart!.Offset];
                            builder.BuildStore(exception, caughtExceptions[handler]);
                            SaveException(filterBlock, exception);
                            builder.BuildBr(filterBlock);
                            terminatedBlocks.Add(chain);
                            var endfilter = region.Handlers[index].FilterStart is null
                                ? null
                                : method.Value.Item3 is MethodDefinition filterMethod
                                    ? filterMethod.Body.Instructions.FirstOrDefault(instruction =>
                                        instruction.OpCode.Code == Code.Endfilter &&
                                        instruction.Offset >= handler.FilterStart.Offset && instruction.Offset < handler.HandlerStart.Offset)
                                    : null;
                            if (endfilter is not null)
                                filterStates[endfilter.Offset] = (label[handler.HandlerStart.Offset], next);
                        }
                        else if (handler.HandlerType == ExceptionHandlerType.Catch)
                        {
                            var match = method.Value.Item1.AppendBasicBlock($"eh.match.{nextVirtualDispatchId++}");
                            builder.BuildCondBr(BuildExceptionMatch(exception, handler.CatchType), match, next);
                            terminatedBlocks.Add(chain);
                            builder.PositionAtEnd(match);
                            builder.BuildStore(exception, caughtExceptions[handler]);
                            SaveException(label[handler.HandlerStart.Offset], exception);
                            builder.BuildBr(label[handler.HandlerStart.Offset]);
                            terminatedBlocks.Add(match);
                        }
                        else if (handler.HandlerType is ExceptionHandlerType.Finally or ExceptionHandlerType.Fault)
                        {
                            var finallyBlock = label[handler.HandlerStart.Offset];
                            var rethrow = method.Value.Item1.AppendBasicBlock($"eh.rethrow.{nextVirtualDispatchId++}");
                            RegisterFinallyContinuation(handler, rethrow);
                            builder.BuildBr(finallyBlock);
                            terminatedBlocks.Add(chain);
                            builder.PositionAtEnd(rethrow);
                            builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                                [builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, [])]);
                            builder.BuildUnreachable();
                            terminatedBlocks.Add(rethrow);
                            chain = next;
                            break;
                        }
                        chain = next;
                    }
                    builder.PositionAtEnd(chain);
                    builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                        [builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, [])]);
                    builder.BuildUnreachable();
                    terminatedBlocks.Add(chain);
                    builder.PositionAtEnd(normal);
                }

                bool CanFallThrough(Instruction instruction) => instruction.OpCode.Code is not
                    (Code.Br or Code.Br_S or Code.Leave or Code.Leave_S or Code.Ret or Code.Throw or Code.Rethrow or Code.Endfinally or Code.Endfilter or Code.Switch);

                var methodDefinition = method.Value.Item3.Resolve();
                if (methodDefinition?.HasBody == true)
                {
                    for (int i = 0; i < methodDefinition.Body.Variables.Count; i++)
                        local[i] = CreateLocalStorage(entryBuilder,
                            SubstituteGenericParameter(methodDefinition.Body.Variables[i].VariableType, method.Value.Item3));
                }
                Dictionary<int, TypeReference> discoveredVariables = new();
                foreach (var instruction in method.Value.Item4)
                {
                    if (instruction.Operand is VariableDefinition variable && instruction.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Ldloc or Code.Ldloc_S or Code.Ldloca or Code.Ldloca_S)
                        discoveredVariables[variable.Index] = variable.VariableType;
                }
                foreach (var variable in discoveredVariables)
                    if (!local.ContainsKey(variable.Key))
                        local[variable.Key] = CreateLocalStorage(entryBuilder,
                            SubstituteGenericParameter(variable.Value, method.Value.Item3));

                for (int i = 0; i < GetMethodParameterCount(method.Value.Item3); i++)
                {
                    var argument = method.Value.Item1.GetParam((uint)i);
                    local[-1 - i] = new(BuildEntryAlloca(argument.TypeOf), argument.TypeOf);
                    entryBuilder.PositionAtEnd(allocaBlock);
                    entryBuilder.BuildStore(argument, local[-1 - i].Item1);
                }
                label.Add(method.Value.Item4.First().Offset, entry);

                if (rootMethods.Contains(method.Value.Item3.FullName))
                {
                    var cctor = moduleMethods.Values.FirstOrDefault(candidate =>
                        candidate.Item3.Name == ".cctor" &&
                        candidate.Item3.DeclaringType.FullName == method.Value.Item3.DeclaringType.FullName);
                    if (cctor is not null && cctor.Item1 != default)
                        builder.BuildCall2(cctor.Item2, cctor.Item1, Array.Empty<LLVMValueRef>());
                }

                // Scan for branches
                Instruction? previousInstruction = null;
                foreach (var instr in method.Value.Item4)
                {
                    switch (instr.OpCode.Code)
                    {
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                        case Code.Leave:
                        case Code.Leave_S:
                            {
                                var branchStart = (Instruction)instr.Operand;
                                var next = instr.Next;
                                if (!label.ContainsKey(branchStart.Offset))
                                {
                                    label.TryAdd(branchStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(branchStart)));
                                }
                                if (!label.ContainsKey(next.Offset))
                                {
                                    label.TryAdd(next.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(next))); // fallthrough
                                }
                                break;
                            }
                    case Code.Switch:
                        {
                            foreach (var branchStart in (Instruction[])instr.Operand)
                                if (!label.ContainsKey(branchStart.Offset))
                                    label.TryAdd(branchStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(branchStart)));
                            if (instr.Next is not null && !label.ContainsKey(instr.Next.Offset))
                                label.TryAdd(instr.Next.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(instr.Next)));
                            break;
                        }
                    }
                }

                if (method.Value.Item3 is MethodDefinition bodyWithHandlers)
                {
                    foreach (var handler in bodyWithHandlers.Body.ExceptionHandlers)
                    {
                        if (!label.ContainsKey(handler.HandlerStart.Offset))
                            label.TryAdd(handler.HandlerStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(handler.HandlerStart)));
                        if (handler.FilterStart is not null && !label.ContainsKey(handler.FilterStart.Offset))
                            label.TryAdd(handler.FilterStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(handler.FilterStart)));
                    }

                    foreach (var regionGroup in bodyWithHandlers.Body.ExceptionHandlers
                                 .GroupBy(handler => (handler.TryStart.Offset, handler.TryEnd.Offset)))
                    {
                        var frame = BuildEntryAlloca(LLVMTypeRef.CreateArray(LLVMTypeRef.Int8, (uint)(pointerSize * 2)));
                        var buffer = BuildEntryAlloca(LLVMTypeRef.CreateArray(LLVMTypeRef.Int8, 256));
                        var region = new ExceptionRegion
                        {
                            Start = regionGroup.Key.Item1,
                            End = regionGroup.Key.Item2,
                            Handlers = regionGroup.ToList(),
                            Frame = frame,
                            Buffer = buffer
                        };
                        exceptionRegions.Add(region);
                        foreach (var handler in region.Handlers.Where(handler =>
                                     handler.HandlerType is ExceptionHandlerType.Finally or ExceptionHandlerType.Fault))
                            finallyStates[handler] = (BuildEntryAlloca(LLVMTypeRef.Int32), new());
                        foreach (var handler in region.Handlers.Where(handler =>
                                     handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter))
                            caughtExceptions[handler] = BuildEntryAlloca(exceptionPointerType);
                    }
                    exceptionRegions.Sort((left, right) =>
                    {
                        var start = left.Start.CompareTo(right.Start);
                        return start != 0 ? start : right.End.CompareTo(left.End);
                    });
                }

                var emittedExceptionSetups = new HashSet<ExceptionRegion>();
                foreach (var instr in method.Value.Item4)
                {
                        Console.WriteLine($"{GetLabelName(instr)}\t\t{instr.OpCode}\t{instr.Operand}");
                        if (label.ContainsKey(instr.Offset))
                        {
                            var curr = label[instr.Offset];
                            if (curr != builder.InsertBlock)
                            {
                                if (!terminatedBlocks.Contains(builder.InsertBlock))
                                {
                                    if (previousInstruction is not null && CanFallThrough(previousInstruction))
                                    {
                                        SaveStack(curr);
                                    }
                                    builder.BuildBr(curr);
                                    terminatedBlocks.Add(builder.InsertBlock);
                                }
                                builder.PositionAtEnd(curr);
                                RestoreStack(curr);
                            }
                        }
                        if (!terminatedBlocks.Contains(builder.InsertBlock))
                        {
                            foreach (var region in exceptionRegions.Where(region => region.Start == instr.Offset && emittedExceptionSetups.Add(region)))
                                EmitExceptionSetup(region);
                        }
                        if (terminatedBlocks.Contains(builder.InsertBlock))
                        {
                            previousInstruction = instr;
                            continue;
                        }
                    switch (instr.OpCode.Code)
                    {
                        case Code.Nop:
                        case Code.Volatile:
                        case Code.Readonly:
                        case Code.Tail:
                            break;
                        case Code.Constrained:
                            constrainedType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                            break;
                        case Code.Pop:
                            if (stack.Count != 0)
                                stack.Pop();
                            break;
                        case Code.Newarr:
                            {
                                TypeReference type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var size = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(type));
                                var count = ConvertValue(builder, stack.Pop(), sizeType, false);
                                var function = runtimeMethods[RuntimeMethod.Newarr];
                                var arrayBaseSize = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false);
                                var ptr = builder.BuildCall2(function.Item2, function.Item1, [count, size, arrayBaseSize]);
                                var lengthField = GetArrayLengthField();
                                StoreField(builder, ptr, lengthField, count);
                                InitializeRuntimeType(builder, ptr, new ArrayType(type));
                                stack.Push(ptr);
                                TrackType(ptr, new ArrayType(type));
                            }
                            break;
                        case Code.Call:
                        case Code.Callvirt:
                        case Code.Newobj:
                            {
                                MethodReference targetMethod = (MethodReference)instr.Operand;
                                if (instr.OpCode.Code == Code.Callvirt && targetMethod.Name == "GetEnumerator" &&
                                    stack.Count != 0 && trackedTypes.TryGetValue(stack.Peek(), out var enumerableReceiver) &&
                                    enumerableReceiver is ArrayType arrayReceiver)
                                {
                                    var arrayEnumeratorDefinition = localTypes.Values.First(type => type.Name.StartsWith("ArrayEnumerator`", StringComparison.Ordinal));
                                    var constructor = arrayEnumeratorDefinition.Methods.First(methodDefinition => methodDefinition.Name == ".ctor" && methodDefinition.Parameters.Count == 1);
                                    var constructorName = GetFriendlyMethodName(constructor);
                                    var constructorMethod = moduleMethods[constructorName];
                                    var array = stack.Pop();
                                    var enumerator = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2,
                                        runtimeMethods[RuntimeMethod.Newobj].Item1,
                                        [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(arrayEnumeratorDefinition), false)]);
                                    var enumeratorType = new GenericInstanceType(arrayEnumeratorDefinition);
                                    enumeratorType.GenericArguments.Add(arrayReceiver.ElementType);
                                    InitializeRuntimeType(builder, enumerator, arrayEnumeratorDefinition);
                                    builder.BuildCall2(constructorMethod.Item2, constructorMethod.Item1, [enumerator, array]);
                                    stack.Push(enumerator);
                                    TrackType(enumerator, enumeratorType);
                                    break;
                                }
                                MethodReference callTarget = ResolveCallTarget(targetMethod);
                                bool useRuntimeDispatch = false;
                                var callConstrainedType = constrainedType;
                                constrainedType = null;
                                if (instr.OpCode.Code == Code.Callvirt && callConstrainedType is not null)
                                {
                                    callTarget = ResolveVirtualTarget(targetMethod, callConstrainedType);
                                    useRuntimeDispatch = false;
                                }
                                else if (instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis)
                                {
                                    var stackValues = stack.ToArray();
                                    if (stackValues.Length > targetMethod.Parameters.Count &&
                                        trackedTypes.TryGetValue(stackValues[targetMethod.Parameters.Count], out var receiverType))
                                    {
                                        callTarget = ResolveVirtualTarget(targetMethod, receiverType);
                                        useRuntimeDispatch = IsKnownRuntimeType(receiverType) && receiverType.Resolve()?.IsSealed != true;
                                    }
                                    else
                                        useRuntimeDispatch = true;
                                }

                                if (instr.OpCode.Code == Code.Callvirt && targetMethod.Name == "Invoke" && IsDelegateType(targetMethod.DeclaringType))
                                {
                                    var invokeArguments = new List<LLVMValueRef>();
                                    for (int i = 0; i < targetMethod.Parameters.Count && stack.Count != 0; i++)
                                        invokeArguments.Add(stack.Pop());
                                    invokeArguments.Reverse();
                                    var delegateObject = stack.Count == 0
                                        ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                        : stack.Pop();
                                    var functionField = GetDelegateField(targetMethod.DeclaringType, "_function");
                                    var targetField = GetDelegateField(targetMethod.DeclaringType, "_target");
                                    var functionValue = builder.BuildLoad2(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), GetFieldAddress(builder, delegateObject, functionField));
                                    var invokeReturnType = SubstituteGenericParameter(targetMethod.ReturnType, targetMethod);
                                    var functionType = LLVMTypeRef.CreateFunction(GetLLVMTypeRef(invokeReturnType),
                                        [LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), .. targetMethod.Parameters.Select(parameter => GetLLVMTypeRef(SubstituteGenericParameter(parameter.ParameterType, targetMethod)))]);
                                    var functionPointer = functionValue;
                                    var callArguments = new List<LLVMValueRef>
                                    {
                                        builder.BuildLoad2(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), GetFieldAddress(builder, delegateObject, targetField))
                                    };
                                    callArguments.AddRange(invokeArguments.Select((value, index) => ConvertValue(builder, value,
                                        GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[index].ParameterType, targetMethod)))));
                                    var invokeResult = builder.BuildCall2(functionType, functionPointer, callArguments.ToArray());
                                    if (invokeReturnType.MetadataType != MetadataType.Void)
                                    {
                                        stack.Push(invokeResult);
                                        TrackType(invokeResult, invokeReturnType);
                                    }
                                    break;
                                }
                                if (targetMethod.DeclaringType.FullName == "System.Type" && targetMethod.Name == "GetTypeFromHandle")
                                {
                                    stack.Push(stack.Count == 0
                                        ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                        : stack.Pop());
                                    break;
                                }
                                if (targetMethod.DeclaringType.FullName == "System.Array" && targetMethod.HasThis &&
                                    targetMethod.Name is "get_Rank" or "GetLength" or "GetLowerBound" or "GetUpperBound")
                                {
                                    var dimension = targetMethod.Parameters.Count == 0
                                        ? default
                                        : stack.Pop();
                                    var array = stack.Pop();
                                    LLVMValueRef arrayResult;
                                    if (targetMethod.Name == "get_Rank")
                                        arrayResult = builder.BuildLoad2(GetLLVMTypeRef(targetMethod.ReturnType),
                                            GetFieldAddress(builder, array, localTypes["System.Array"].Fields.First(field => field.Name == "_rank")));
                                    else if (targetMethod.Name == "GetLowerBound")
                                        arrayResult = LLVMValueRef.CreateConstInt(GetLLVMTypeRef(targetMethod.ReturnType), 0, false);
                                    else
                                    {
                                        var lengths = Enumerable.Range(0, 3)
                                            .Select(index => builder.BuildLoad2(GetLLVMTypeRef(targetMethod.ReturnType),
                                                GetFieldAddress(builder, array, localTypes["System.Array"].Fields.First(field => field.Name == $"_length{index}"))))
                                            .ToArray();
                                        var nativeDimension = ConvertValue(builder, dimension, GetLLVMTypeRef(targetMethod.Parameters[0].ParameterType), false);
                                        arrayResult = lengths[2];
                                        arrayResult = builder.BuildSelect(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, nativeDimension,
                                            LLVMValueRef.CreateConstInt(nativeDimension.TypeOf, 0, false)), lengths[0], arrayResult);
                                        arrayResult = builder.BuildSelect(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, nativeDimension,
                                            LLVMValueRef.CreateConstInt(nativeDimension.TypeOf, 1, false)), lengths[1], arrayResult);
                                        if (targetMethod.Name == "GetUpperBound")
                                            arrayResult = builder.BuildSub(arrayResult, LLVMValueRef.CreateConstInt(arrayResult.TypeOf, 1, false));
                                    }
                                    stack.Push(arrayResult);
                                    TrackType(arrayResult, targetMethod.ReturnType);
                                    break;
                                }
                                if (targetMethod.DeclaringType is ArrayType multidimensionalArray && multidimensionalArray.Rank > 1)
                                {
                                    var elementType = GetLLVMTypeRef(multidimensionalArray.ElementType);
                                    if (instr.OpCode.Code == Code.Newobj)
                                    {
                                        var dimensions = Enumerable.Range(0, multidimensionalArray.Rank)
                                            .Select(_ => stack.Pop()).Reverse().ToArray();
                                        var total = LLVMValueRef.CreateConstInt(sizeType, 1, false);
                                        foreach (var dimension in dimensions)
                                            total = builder.BuildMul(total, ConvertValue(builder, dimension, sizeType, false));
                                        var array = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newarr].Item2,
                                            runtimeMethods[RuntimeMethod.Newarr].Item1,
                                            [total,
                                             LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(multidimensionalArray.ElementType), false),
                                             LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false)]);
                                        StoreField(builder, array, GetArrayLengthField(), total);
                                        StoreField(builder, array, localTypes["System.Array"].Fields.First(field => field.Name == "_rank"),
                                            LLVMValueRef.CreateConstInt(sizeType, (ulong)multidimensionalArray.Rank, false));
                                        for (int i = 0; i < dimensions.Length; i++)
                                            StoreField(builder, array, localTypes["System.Array"].Fields.First(field => field.Name == $"_length{i}"),
                                                ConvertValue(builder, dimensions[i], sizeType, false));
                                        InitializeRuntimeType(builder, array, multidimensionalArray);
                                        stack.Push(array);
                                        TrackType(array, multidimensionalArray);
                                        break;
                                    }
                                    if (targetMethod.Name is "Get" or "Set")
                                    {
                                        LLVMValueRef value = default;
                                        if (targetMethod.Name == "Set")
                                            value = stack.Pop();
                                        var indices = Enumerable.Range(0, multidimensionalArray.Rank)
                                            .Select(_ => stack.Pop()).Reverse().ToArray();
                                        var array = stack.Pop();
                                        var address = GetMultiArrayElementAddress(builder, array, indices, elementType);
                                        if (targetMethod.Name == "Set")
                                            builder.BuildStore(ConvertValue(builder, value, elementType), address);
                                        else
                                            stack.Push(builder.BuildLoad2(elementType, address));
                                        break;
                                    }
                                }
                                if (callTarget.Name == "GetValue" && targetMethod.DeclaringType is GenericInstanceType getValueInterface &&
                                    getValueInterface.GenericArguments.Count != 0)
                                {
                                    var receiver = stack.Pop();
                                    var valueType = getValueInterface.GenericArguments[0];
                                    var genericDefinition = localTypes.Values.First(type => type.Name.StartsWith("GenericFeature`1", StringComparison.Ordinal));
                                    var valueField = genericDefinition.Fields.First(field => field.Name == "_value");
                                    var valueAddress = GetFieldAddress(builder, receiver, valueField);
                                    var value = IsValueType(valueType)
                                        ? valueAddress
                                        : builder.BuildLoad2(GetLLVMTypeRef(valueType), valueAddress);
                                    stack.Push(value);
                                    TrackType(value, valueType);
                                    break;
                                }
                                LLVMValueRef ptr = default;

                                if (instr.OpCode.Code == Code.Newobj)
                                {
                                    if (IsDelegateType(targetMethod.DeclaringType))
                                    {
                                        var delegateFunction = stack.Count == 0
                                            ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                            : ConvertValue(builder, stack.Pop(), LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
                                        var delegateTarget = stack.Count == 0
                                            ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                            : stack.Pop();
                                        var delegateType = targetMethod.DeclaringType.Resolve() ?? throw new NotSupportedException($"Delegate type is not defined: {targetMethod.DeclaringType.FullName}");
                                        ptr = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2, runtimeMethods[RuntimeMethod.Newobj].Item1,
                                            [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(delegateType), false)]);
                                        InitializeRuntimeType(builder, ptr, targetMethod.DeclaringType);
                                        builder.BuildStore(delegateFunction, GetFieldAddress(builder, ptr, GetDelegateField(targetMethod.DeclaringType, "_function")));
                                        StoreField(builder, ptr, GetDelegateField(targetMethod.DeclaringType, "_target"), delegateTarget);
                                        stack.Push(ptr);
                                        TrackType(ptr, targetMethod.DeclaringType);
                                        break;
                                    }
                                    var function = runtimeMethods[RuntimeMethod.Newobj];
                                    int size = targetMethod.DeclaringType is GenericInstanceType
                                        ? GetTypeSize(targetMethod.DeclaringType)
                                        : localTypes.TryGetValue(targetMethod.DeclaringType.FullName, out var targetType)
                                            ? GetTypeDefinitionSize(targetType)
                                            : pointerSize;
                                    ptr = builder.BuildCall2(function.Item2, function.Item1, [LLVMValueRef.CreateConstInt(sizeType, (ulong)size)]);
                                    InitializeRuntimeType(builder, ptr, targetMethod.DeclaringType);
                                }

                                var targetName = GetFriendlyMethodName(callTarget);
                                if (targetMethod.DeclaringType is GenericInstanceType requestedGeneric)
                                {
                                    var requestedElement = requestedGeneric.ElementType.FullName;
                                    var requestedArguments = requestedGeneric.GenericArguments.Select(GetRuntimeTypeKey).ToArray();
                                    var specialized = moduleMethods.Values.FirstOrDefault(candidate =>
                                    {
                                        if (candidate.Item3.DeclaringType is not GenericInstanceType candidateGeneric ||
                                            candidateGeneric.ElementType.FullName != requestedElement ||
                                            candidateGeneric.GenericArguments.Count != requestedArguments.Length ||
                                            !candidateGeneric.GenericArguments.Select(GetRuntimeTypeKey).SequenceEqual(requestedArguments))
                                            return false;
                                        return candidate.Item3.Name == callTarget.Name &&
                                            candidate.Item3.Parameters.Count == callTarget.Parameters.Count;
                                    });
                                    if (specialized is not null)
                                        targetName = GetFriendlyMethodName(specialized.Item3);
                                }
                                if (targetMethod.DeclaringType is GenericInstanceType genericDeclaringType &&
                                    genericDeclaringType.GenericArguments.Any(argument => IsValueType(argument)))
                                {
                                    var genericArguments = string.Join("_", genericDeclaringType.GenericArguments.Select(GetFriendlyTypeName));
                                    var specializedName = moduleMethods.Keys.FirstOrDefault(name =>
                                        name.Contains("_" + genericArguments + "_", StringComparison.Ordinal) &&
                                        name.Contains("_" + callTarget.Name.Replace('.', '_') + "_", StringComparison.Ordinal));
                                    if (specializedName is not null)
                                        targetName = specializedName;
                                }
                                if (!moduleMethods.TryGetValue(targetName, out var m))
                                {
                                    var fallback = moduleMethods.Keys.FirstOrDefault(name =>
                                        name.EndsWith("_" + callTarget.Name, StringComparison.Ordinal) ||
                                        name.Contains("_" + callTarget.Name + "_", StringComparison.Ordinal));
                                    if (fallback is null)
                                        throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}");
                                    m = moduleMethods[fallback];
                                }

                                var targetFuncCreated = m.Item2;
                                var targetFunc = m.Item1;
                                var targetArgsList = new List<LLVMValueRef>();
                                int parameterCount = instr.OpCode.Code == Code.Newobj
                                    ? targetMethod.Parameters.Count
                                    : GetMethodParameterCount(targetMethod);
                                for (int i = 0; i < parameterCount; i++)
                                {
                                    if (stack.Count != 0)
                                        targetArgsList.Add(stack.Pop());
                                    else
                                        targetArgsList.Add(LLVMValueRef.CreateConstNull(instr.OpCode.Code != Code.Newobj && i == 0 && targetMethod.HasThis
                                            ? LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)
                                            : GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[Math.Max(0, i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0))].ParameterType, targetMethod))));
                                }
                                targetArgsList.Reverse();
                                LLVMValueRef[] targetArgs = targetArgsList.ToArray();
                                if (callConstrainedType is not null && targetArgs.Length != 0 &&
                                    IsManagedReferenceType(callConstrainedType) &&
                                    trackedTypes.TryGetValue(targetArgs[0], out var constrainedReceiverType) &&
                                    constrainedReceiverType is ByReferenceType)
                                {
                                    targetArgs[0] = builder.BuildLoad2(GetLLVMTypeRef(callConstrainedType), targetArgs[0]);
                                    TrackType(targetArgs[0], callConstrainedType);
                                }
                                for (int i = 0; i < targetArgs.Length; i++)
                                {
                                    var expectedType = instr.OpCode.Code != Code.Newobj && i == 0 && targetMethod.HasThis
                                        ? LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)
                                        : GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0)].ParameterType, targetMethod));
                                    targetArgs[i] = ConvertValue(builder, targetArgs[i], expectedType);
                                }
                                if (ptr != default)
                                {
                                    targetArgs = targetArgs.Length == 0
                                        ? [ptr]
                                        : [.. (LLVMValueRef[])[ptr], .. targetArgs];
                                }

                                var result = instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis && useRuntimeDispatch
                                    ? BuildVirtualDispatch(targetMethod, targetArgs, targetFuncCreated, targetFunc, GetVirtualImplementations(targetMethod))
                                    : builder.BuildCall2(targetFuncCreated, targetFunc, targetArgs);
                                if (result != default && targetMethod.ReturnType is GenericParameter returnParameter &&
                                    targetMethod.DeclaringType is GenericInstanceType returnDeclaringType &&
                                    returnParameter.Position < returnDeclaringType.GenericArguments.Count)
                                {
                                    var concreteReturnType = returnDeclaringType.GenericArguments[returnParameter.Position];
                                    var concreteLLVMType = GetLLVMTypeRef(concreteReturnType);
                                    if (result.TypeOf.Equals(sizeType) && concreteLLVMType.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                                        result = builder.BuildIntToPtr(result, concreteLLVMType);
                                    else if (result.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind && concreteLLVMType.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
                                        result = builder.BuildPtrToInt(result, concreteLLVMType);
                                }
                                if (targetMethod.ReturnType.MetadataType != MetadataType.Void)
                                {
                                    stack.Push(result);
                                    TrackType(result, SubstituteGenericParameter(targetMethod.ReturnType, targetMethod));
                                }
                                if (ptr != default)
                                {
                                    stack.Push(ptr);
                                    TrackType(ptr, targetMethod.DeclaringType);
                                }
                                if (IsNoReturnMethod(callTarget, localMethods))
                                {
                                    builder.BuildUnreachable();
                                    terminatedBlocks.Add(builder.InsertBlock);
                                }
                            }
                            break;
                        case Code.Calli:
                            {
                                var callSite = (CallSite)instr.Operand;
                                var functionPointer = stack.Pop();
                                var arguments = Enumerable.Range(0, callSite.Parameters.Count).Select(_ => stack.Pop()).Reverse().ToList();
                                var functionType = LLVMTypeRef.CreateFunction(GetLLVMTypeRef(callSite.ReturnType),
                                    callSite.Parameters.Select(parameter => GetLLVMTypeRef(parameter.ParameterType)).ToArray());
                                var result = builder.BuildCall2(functionType, functionPointer, arguments.ToArray());
                                if (callSite.ReturnType.MetadataType != MetadataType.Void)
                                    stack.Push(result);
                            }
                            break;
                        case Code.Ldftn:
                        case Code.Ldvirtftn:
                            {
                                MethodReference targetMethod = (MethodReference)instr.Operand;
                                MethodReference callTarget = ResolveCallTarget(targetMethod);
                                if (instr.OpCode.Code == Code.Ldvirtftn && stack.Count != 0)
                                {
                                    var receiver = stack.Pop();
                                    if (trackedTypes.TryGetValue(receiver, out var receiverType))
                                        callTarget = ResolveVirtualTarget(targetMethod, receiverType);
                                }
                                stack.Push(moduleMethods[GetFriendlyMethodName(callTarget)].Item1);
                            }
                            break;
                        case Code.Castclass:
                        case Code.Isinst:
                            {
                                var targetType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var value = stack.Count == 0
                                    ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                    : stack.Pop();
                            stack.Push(value);
                            if (!trackedTypes.ContainsKey(value))
                                TrackType(value, targetType);
                            }
                            break;
                        case Code.Ret:
                            var returnType = SubstituteGenericParameter(method.Value.Item3.ReturnType, method.Value.Item3);
                            if (returnType.MetadataType != MetadataType.Void)
                            {
                                builder.BuildRet(ConvertValue(builder, stack.Count == 0
                                    ? LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType))
                                    : stack.Pop(), GetLLVMTypeRef(returnType)));
                            }
                            else
                            {
                                builder.BuildRetVoid();
                            }
                            terminatedBlocks.Add(builder.InsertBlock);
                            break;
                        case Code.Stfld:
                            {
                                var fieldReference = (FieldReference)instr.Operand;
                                FieldDefinition field = GetLocalField(fieldReference);
                                var fieldType = SubstituteFieldType(fieldReference, method.Value.Item3);
                                var val = stack.Pop();
                                var obj = stack.Pop();
                                if (IsValueType(fieldType))
                                    CopyValue(builder, GetFieldAddress(builder, obj, field), val, GetTypeSize(fieldType));
                                else
                                    builder.BuildStore(ConvertValue(builder, val, GetLLVMTypeRef(fieldType)), GetFieldAddress(builder, obj, field));
                            }
                            break;
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc:
                        case Code.Stloc_S:
                            {
                                int offset = instr.OpCode.Code switch
                                {
                                    Code.Stloc_0 => 0,
                                    Code.Stloc_1 => 1,
                                    Code.Stloc_2 => 2,
                                    Code.Stloc_3 => 3,
                                    Code.Stloc => ((VariableDefinition)instr.Operand).Index,
                                    Code.Stloc_S => ((VariableDefinition)instr.Operand).Index,
                                };

                                var variableType = GetMethodVariableType(method.Value.Item3, offset);
                                var val = stack.Count != 0
                                    ? stack.Pop()
                                    : LLVMValueRef.CreateConstNull(variableType is not null && GetLLVMTypeRef(variableType).Kind == LLVMTypeKind.LLVMPointerTypeKind
                                        ? GetLLVMTypeRef(variableType)
                                        : sizeType);
                                if (variableType is not null && IsValueType(variableType))
                                {
                                    var alloc = local.ContainsKey(offset) ? local[offset] : CreateLocalStorage(entryBuilder, variableType);
                                    local.TryAdd(offset, alloc);
                                    CopyValue(builder, builder.BuildLoad2(alloc.Item2, alloc.Item1), val, GetTypeSize(variableType));
                                }
                                else
                                {
                                    var storageType = variableType is null ? val.TypeOf : GetLLVMTypeRef(variableType);
                                    var alloc = local.ContainsKey(offset) ? local[offset] : new(entryBuilder.BuildAlloca(storageType), storageType);
                                    builder.BuildStore(ConvertValue(builder, val, alloc.Item2), alloc.Item1);
                                    local.TryAdd(offset, alloc);
                                }
                                if (trackedTypes.TryGetValue(val, out var valType))
                                    localRuntimeTypes[offset] = valType;
                            }
                            break;
                        case Code.Stelem_I:
                        case Code.Stelem_I1:
                        case Code.Stelem_I2:
                        case Code.Stelem_I4:
                        case Code.Stelem_I8:
                        case Code.Stelem_R4:
                        case Code.Stelem_R8:
                        case Code.Stelem_Ref:
                        case Code.Stelem_Any:
                            {
                                var value = stack.Pop();
                                var index = stack.Pop();
                                var array = stack.Pop();
                                LLVMTypeRef type = instr.OpCode.Code switch
                                {
                                    Code.Stelem_I => sizeType,
                                    Code.Stelem_I1 => LLVMTypeRef.Int8,
                                    Code.Stelem_I2 => LLVMTypeRef.Int16,
                                    Code.Stelem_I4 => LLVMTypeRef.Int32,
                                    Code.Stelem_I8 => LLVMTypeRef.Int64,
                                    Code.Stelem_R4 => LLVMTypeRef.Float,
                                    Code.Stelem_R8 => LLVMTypeRef.Double,
                                    Code.Stelem_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                     Code.Stelem_Any => GetLLVMTypeRef(SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3))
                                };
                                builder.BuildStore(ConvertValue(builder, value, type), GetArrayElementAddress(builder, array, index, type));
                            }
                            break;
                        case Code.Stsfld:
                        case Code.Ldsflda:
                            {
                                FieldReference field = (FieldReference)instr.Operand;

                                var ptr = staticFields[GetFriendlyFieldName(field)];
                                if (instr.OpCode.Code == Code.Ldsflda)
                                    stack.Push(ptr.Item1);
                                else
                                {
                                    var value = stack.Count == 0 ? LLVMValueRef.CreateConstNull(ptr.Item2) : stack.Pop();
                                    builder.BuildStore(ConvertValue(builder, value, ptr.Item2), ptr.Item1);
                                }
                            }
                            break;
                        case Code.Ldsfld:
                            {
                                FieldReference field = (FieldReference)instr.Operand;
                                var ptr = staticFields[GetFriendlyFieldName(field)];
                                var value = builder.BuildLoad2(ptr.Item2, ptr.Item1);
                                stack.Push(value);
                                TrackType(value, field.FieldType);
                            }
                            break;
                        case Code.Ldstr:
                            {
                                var value = (string)instr.Operand;
                                var stringValue = BuildStringValue(builder, value);
                                stack.Push(stringValue);
                                TrackType(stringValue, localTypes["System.String"]);
                            }
                            break;
                        case Code.Ldfld:
                        case Code.Ldflda:
                            {
                                var fieldReference = (FieldReference)instr.Operand;
                                FieldDefinition field = GetLocalField(fieldReference);
                                var fieldType = SubstituteFieldType(fieldReference, method.Value.Item3);
                                var obj = stack.Pop();
                                var gep = GetFieldAddress(builder, obj, field);
                                if (instr.OpCode.Code == Code.Ldflda)
                                {
                                    stack.Push(gep);
                                    TrackType(gep, new ByReferenceType(fieldType));
                                }
                                else if (IsValueType(fieldType))
                                {
                                    stack.Push(gep);
                                    TrackType(gep, fieldType);
                                }
                                else
                                {
                                    var value = builder.BuildLoad2(GetLLVMTypeRef(fieldType), gep);
                                    stack.Push(value);
                                    TrackType(value, fieldType);
                                }
                            }
                            break;
                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                            {
                                var param = method.Value.Item1.GetParam(instr.OpCode.Code switch
                                {
                                    Code.Ldarg_0 => 0,
                                    Code.Ldarg_1 => 1,
                                    Code.Ldarg_2 => 2,
                                    Code.Ldarg_3 => 3
                                });
                                stack.Push(param);
                                var argumentIndex = instr.OpCode.Code switch
                                {
                                    Code.Ldarg_0 => 0,
                                    Code.Ldarg_1 => 1,
                                    Code.Ldarg_2 => 2,
                                    Code.Ldarg_3 => 3
                                };
                                if (method.Value.Item3.HasThis && argumentIndex == 0)
                                    TrackType(param, method.Value.Item3.DeclaringType);
                                else
                                {
                                    var parameterIndex = argumentIndex - (method.Value.Item3.HasThis ? 1 : 0);
                                    if (parameterIndex >= 0 && parameterIndex < method.Value.Item3.Parameters.Count)
                                        TrackType(param, method.Value.Item3.Parameters[parameterIndex].ParameterType);
                                }
                            }
                            break;
                        case Code.Ldarg:
                        case Code.Ldarg_S:
                            {
                                int index = instr.Operand switch
                                {
                                    ParameterDefinition parameter => parameter.Index + (method.Value.Item3.HasThis ? 1 : 0),
                                    _ => Convert.ToInt32(instr.Operand)
                                };
                                var argument = method.Value.Item1.GetParam((uint)index);
                                stack.Push(argument);
                                if (method.Value.Item3.HasThis && index == 0)
                                    TrackType(argument, method.Value.Item3.DeclaringType);
                                else
                                {
                                    var parameterIndex = index - (method.Value.Item3.HasThis ? 1 : 0);
                                    if (parameterIndex >= 0 && parameterIndex < method.Value.Item3.Parameters.Count)
                                        TrackType(argument, method.Value.Item3.Parameters[parameterIndex].ParameterType);
                                }
                            }
                            break;
                        case Code.Starg:
                        case Code.Starg_S:
                            {
                                int index = instr.Operand switch
                                {
                                    ParameterDefinition parameter => parameter.Index + (method.Value.Item3.HasThis ? 1 : 0),
                                    _ => Convert.ToInt32(instr.Operand)
                                };
                                var value = stack.Pop();
                                local[-1 - index] = local.TryGetValue(-1 - index, out var arg)
                                    ? arg
                                    : new(entryBuilder.BuildAlloca(value.TypeOf), value.TypeOf);
                                builder.BuildStore(value, local[-1 - index].Item1);
                            }
                            break;
                        case Code.Ldelem_I1:
                        case Code.Ldelem_U1:
                        case Code.Ldelem_I2:
                        case Code.Ldelem_U2:
                        case Code.Ldelem_I4:
                        case Code.Ldelem_U4:
                        case Code.Ldelem_I8:
                        case Code.Ldelem_R4:
                        case Code.Ldelem_R8:
                        case Code.Ldelem_Ref:
                        case Code.Ldelem_Any:
                            {
                                var index = stack.Pop();
                                var array = stack.Pop();
                                var type = instr.OpCode.Code switch
                                {
                                    Code.Ldelem_I1 => LLVMTypeRef.Int8,
                                    Code.Ldelem_U1 => LLVMTypeRef.Int8,
                                    Code.Ldelem_I2 => LLVMTypeRef.Int16,
                                    Code.Ldelem_U2 => LLVMTypeRef.Int16,
                                    Code.Ldelem_I4 => LLVMTypeRef.Int32,
                                    Code.Ldelem_U4 => LLVMTypeRef.Int32,
                                    Code.Ldelem_I8 => LLVMTypeRef.Int64,
                                    Code.Ldelem_R4 => LLVMTypeRef.Float,
                                    Code.Ldelem_R8 => LLVMTypeRef.Double,
                                    Code.Ldelem_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                    Code.Ldelem_Any => GetLLVMTypeRef(SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3))
                                };
                                var gep = GetArrayElementAddress(builder, array, index, type);
                                var element = builder.BuildLoad2(type, gep);
                                if (instr.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_U1 or Code.Ldelem_I2 or Code.Ldelem_U2)
                                    element = ConvertValue(builder, element, sizeType, instr.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_I2);
                                stack.Push(element);
                                if (instr.OpCode.Code == Code.Ldelem_Any)
                                    TrackType(element, SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3));
                            }
                            break;
                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                        case Code.Ldloc:
                        case Code.Ldloc_S:
                            {
                                int offset = instr.OpCode.Code switch
                                {
                                    Code.Ldloc_0 => 0,
                                    Code.Ldloc_1 => 1,
                                    Code.Ldloc_2 => 2,
                                    Code.Ldloc_3 => 3,
                                    Code.Ldloc => ((VariableDefinition)instr.Operand).Index,
                                    Code.Ldloc_S => ((VariableDefinition)instr.Operand).Index,
                                };
                                var load = builder.BuildLoad2(local[offset].Item2, local[offset].Item1);
                                stack.Push(load);
                                var localType = GetMethodVariableType(method.Value.Item3, offset);
                                if (localType is not null)
                                    TrackType(load, localRuntimeTypes.TryGetValue(offset, out var runtimeType)
                                        ? runtimeType
                                        : localType);
                            }
                            break;
                        case Code.Ldc_I4_M1:
                            stack.Push(LLVMValueRef.CreateConstInt(sizeType, unchecked((ulong)-1), true));
                            break;
                        case Code.Ldc_I4_0:
                        case Code.Ldc_I4_1:
                        case Code.Ldc_I4_2:
                        case Code.Ldc_I4_3:
                        case Code.Ldc_I4_4:
                        case Code.Ldc_I4_5:
                        case Code.Ldc_I4_6:
                        case Code.Ldc_I4_7:
                        case Code.Ldc_I4_8:
                        case Code.Ldc_I4:
                        case Code.Ldc_I4_S:
                            {
                                var value = LLVMValueRef.CreateConstInt(sizeType, (ulong)(instr.OpCode.Code switch
                                {
                                    Code.Ldc_I4_0 => 0,
                                    Code.Ldc_I4_1 => 1,
                                    Code.Ldc_I4_2 => 2,
                                    Code.Ldc_I4_3 => 3,
                                    Code.Ldc_I4_4 => 4,
                                    Code.Ldc_I4_5 => 5,
                                    Code.Ldc_I4_6 => 6,
                                    Code.Ldc_I4_7 => 7,
                                    Code.Ldc_I4_8 => 8,
                                    Code.Ldc_I4 => (int)instr.Operand,
                                    Code.Ldc_I4_S => (sbyte)instr.Operand
                                }));
                                stack.Push(value);
                            }
                            break;
                        case Code.Ldelema:
                            {
                                var index = stack.Pop();
                                var array = stack.Pop();
                                stack.Push(GetArrayElementAddress(builder, array, index,
                                    GetLLVMTypeRef(SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3))));
                            }
                            break;
                        case Code.Ldlen:
                            {
                                var array = stack.Pop();
                                var lengthField = GetArrayLengthField();
                                var length = GetFieldAddress(builder, array, lengthField);
                                stack.Push(builder.BuildLoad2(GetLLVMTypeRefFromMetadataType(lengthField.FieldType.MetadataType), length));
                            }
                            break;
                        case Code.Ldloca:
                        case Code.Ldloca_S:
                            {
                                var variable = (VariableDefinition)instr.Operand;
                                var variableType = SubstituteGenericParameter(variable.VariableType, method.Value.Item3);
                                int index = variable.Index;
                                var alloc = local.ContainsKey(index)
                                    ? local[index]
                                    : CreateLocalStorage(entryBuilder, variableType);
                                local.TryAdd(index, alloc);
                                var address = IsValueType(variableType)
                                    ? builder.BuildLoad2(alloc.Item2, alloc.Item1)
                                    : alloc.Item1;
                                stack.Push(address);
                                TrackType(address, variableType);
                            }
                            break;
                        case Code.Ldarga:
                        case Code.Ldarga_S:
                            {
                                int index = instr.Operand switch
                                {
                                    ParameterDefinition parameter => parameter.Index + (method.Value.Item3.HasThis ? 1 : 0),
                                    _ => Convert.ToInt32(instr.Operand)
                                };
                                if (!local.TryGetValue(-1 - index, out var argumentStorage))
                                {
                                    var argument = method.Value.Item1.GetParam((uint)index);
                                    argumentStorage = new(entryBuilder.BuildAlloca(argument.TypeOf), argument.TypeOf);
                                    builder.BuildStore(argument, argumentStorage.Item1);
                                    local[-1 - index] = argumentStorage;
                                }
                                var argumentValue = method.Value.Item1.GetParam((uint)index);
                                if (argumentValue.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                                    stack.Push(argumentValue);
                                else
                                    stack.Push(argumentStorage.Item1);
                            }
                            break;
                        case Code.Ldind_I1:
                        case Code.Ldind_U1:
                        case Code.Ldind_I2:
                        case Code.Ldind_U2:
                        case Code.Ldind_I4:
                        case Code.Ldind_U4:
                        case Code.Ldind_I8:
                        case Code.Ldind_R4:
                        case Code.Ldind_R8:
                        case Code.Ldind_I:
                        case Code.Ldind_Ref:
                            {
                                var address = stack.Pop();
                                var type = instr.OpCode.Code switch
                                {
                                    Code.Ldind_I1 or Code.Ldind_U1 => LLVMTypeRef.Int8,
                                    Code.Ldind_I2 or Code.Ldind_U2 => LLVMTypeRef.Int16,
                                    Code.Ldind_I4 or Code.Ldind_U4 => LLVMTypeRef.Int32,
                                    Code.Ldind_I8 => LLVMTypeRef.Int64,
                                    Code.Ldind_R4 => LLVMTypeRef.Float,
                                    Code.Ldind_R8 => LLVMTypeRef.Double,
                                    Code.Ldind_I => sizeType,
                                    Code.Ldind_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                };
                                stack.Push(builder.BuildLoad2(type, address));
                            }
                            break;
                        case Code.Stind_I1:
                        case Code.Stind_I2:
                        case Code.Stind_I4:
                        case Code.Stind_I8:
                        case Code.Stind_R4:
                        case Code.Stind_R8:
                        case Code.Stind_I:
                        case Code.Stind_Ref:
                            {
                                var value = stack.Pop();
                                var address = stack.Pop();
                                var type = instr.OpCode.Code switch
                                {
                                    Code.Stind_I1 => LLVMTypeRef.Int8,
                                    Code.Stind_I2 => LLVMTypeRef.Int16,
                                    Code.Stind_I4 => LLVMTypeRef.Int32,
                                    Code.Stind_I8 => LLVMTypeRef.Int64,
                                    Code.Stind_R4 => LLVMTypeRef.Float,
                                    Code.Stind_R8 => LLVMTypeRef.Double,
                                    Code.Stind_I => sizeType,
                                    Code.Stind_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                };
                                builder.BuildStore(ConvertValue(builder, value, type), address);
                            }
                            break;
                        case Code.Initobj:
                            {
                                var type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var address = stack.Pop();
                                unsafe
                                {
                                    LLVM.BuildMemSet(builder, address, LLVMValueRef.CreateConstInt(LLVMTypeRef.Int8, 0, false),
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(type)), false), 1);
                                }
                            }
                            break;
                        case Code.Ldobj:
                            {
                                var type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var address = stack.Pop();
                                if (IsValueType(type))
                                {
                                    var storage = CreateLocalStorage(entryBuilder, type);
                                    var destination = builder.BuildLoad2(storage.Item2, storage.Item1);
                                    CopyValue(builder, destination, address, GetTypeSize(type));
                                    stack.Push(destination);
                                }
                                else
                                    stack.Push(builder.BuildLoad2(GetLLVMTypeRef(type), address));
                            }
                            break;
                        case Code.Stobj:
                            {
                                var type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var value = stack.Pop();
                                var address = stack.Pop();
                                if (IsValueType(type))
                                    CopyValue(builder, address, value, GetTypeSize(type));
                                else
                                    builder.BuildStore(ConvertValue(builder, value, GetLLVMTypeRef(type)), address);
                            }
                            break;
                        case Code.Cpobj:
                            {
                                var type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var source = stack.Pop();
                                var destination = stack.Pop();
                                CopyValue(builder, destination, source, GetTypeSize(type));
                            }
                            break;
                        case Code.Ldc_I8:
                            stack.Push(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int64, unchecked((ulong)(long)instr.Operand), true));
                            break;
                        case Code.Ldc_R4:
                            stack.Push(LLVMValueRef.CreateConstReal(LLVMTypeRef.Float, (float)instr.Operand));
                            break;
                        case Code.Ldc_R8:
                            stack.Push(LLVMValueRef.CreateConstReal(LLVMTypeRef.Double, (double)instr.Operand));
                            break;
                        case Code.Ldnull:
                            {
                                var nullptr = LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
                                stack.Push(nullptr);
                            }
                            break;
                        case Code.Throw:
                            builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                                [stack.Count == 0
                                    ? LLVMValueRef.CreateConstNull(exceptionPointerType)
                                    : ConvertValue(builder, stack.Pop(), exceptionPointerType)]);
                            builder.BuildUnreachable();
                            terminatedBlocks.Add(builder.InsertBlock);
                            break;
                        case Code.Rethrow:
                            var activeCatch = method.Value.Item3 is MethodDefinition rethrowMethod
                                ? rethrowMethod.Body.ExceptionHandlers
                                    .Where(handler => handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter &&
                                        handler.HandlerStart.Offset <= instr.Offset &&
                                        (handler.HandlerEnd is null || instr.Offset < handler.HandlerEnd.Offset))
                                    .OrderByDescending(handler => handler.HandlerStart.Offset)
                                    .FirstOrDefault()
                                : null;
                            builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                                [activeCatch is not null && caughtExceptions.TryGetValue(activeCatch, out var caughtException)
                                    ? builder.BuildLoad2(exceptionPointerType, caughtException)
                                    : builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, [])]);
                            builder.BuildUnreachable();
                            terminatedBlocks.Add(builder.InsertBlock);
                            break;
                        case Code.Endfinally:
                            var finallyHandler = method.Value.Item3 is MethodDefinition finallyMethod
                                ? finallyMethod.Body.ExceptionHandlers.FirstOrDefault(handler =>
                                    handler.HandlerType is ExceptionHandlerType.Finally or ExceptionHandlerType.Fault &&
                                    handler.HandlerStart.Offset <= instr.Offset &&
                                    (handler.HandlerEnd is null || instr.Offset < handler.HandlerEnd.Offset))
                                : null;
                            if (finallyHandler is not null && finallyStates.TryGetValue(finallyHandler, out var finallyState))
                            {
                                var defaultBlock = method.Value.Item1.AppendBasicBlock($"finally.invalid.{nextVirtualDispatchId++}");
                                var switchValue = builder.BuildSwitch(builder.BuildLoad2(LLVMTypeRef.Int32, finallyState.Slot),
                                    defaultBlock, (uint)finallyState.Targets.Count);
                                foreach (var continuation in finallyState.Targets)
                                    switchValue.AddCase(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, (uint)continuation.Key, false), continuation.Value);
                                terminatedBlocks.Add(builder.InsertBlock);
                                builder.PositionAtEnd(defaultBlock);
                                builder.BuildUnreachable();
                                terminatedBlocks.Add(defaultBlock);
                            }
                            else
                            {
                                builder.BuildUnreachable();
                                terminatedBlocks.Add(builder.InsertBlock);
                            }
                            break;
                        case Code.Endfilter:
                            if (filterStates.TryGetValue(instr.Offset, out var filterState))
                            {
                                var filterResult = stack.Count == 0
                                    ? LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0, false)
                                    : ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int32);
                                builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, filterResult,
                                    LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, 0, false)), filterState.Handler, filterState.Next);
                            }
                            else
                                builder.BuildUnreachable();
                            terminatedBlocks.Add(builder.InsertBlock);
                            break;
                        case Code.Leave:
                        case Code.Leave_S:
                            {
                                var branch = label[((Instruction)instr.Operand).Offset];
                                var exitedRegions = exceptionRegions
                                    .Where(region => region.Start <= instr.Offset && instr.Offset < region.End &&
                                        !((Instruction)instr.Operand).Offset.Equals(region.Start) &&
                                        !(((Instruction)instr.Operand).Offset >= region.Start && ((Instruction)instr.Operand).Offset < region.End))
                                    .OrderBy(region => region.End - region.Start)
                                    .ToList();
                                foreach (var exitedRegion in exitedRegions)
                                    builder.BuildCall2(exceptionPopType, exceptionPopFunction, [exitedRegion.Frame]);
                                var leaveHandler = method.Value.Item3 is MethodDefinition leaveMethod
                                    ? leaveMethod.Body.ExceptionHandlers.FirstOrDefault(handler =>
                                        handler.HandlerType == ExceptionHandlerType.Finally &&
                                        handler.TryStart.Offset <= instr.Offset && instr.Offset < handler.TryEnd.Offset &&
                                        !(((Instruction)instr.Operand).Offset >= handler.TryStart.Offset && ((Instruction)instr.Operand).Offset < handler.TryEnd.Offset))
                                    : null;
                                if (leaveHandler is not null && label.TryGetValue(leaveHandler.HandlerStart.Offset, out var finallyBlock))
                                {
                                    RegisterFinallyContinuation(leaveHandler, branch);
                                    builder.BuildBr(finallyBlock);
                                }
                                else
                                {
                                    SaveStack(branch);
                                    builder.BuildBr(branch);
                                }
                                terminatedBlocks.Add(builder.InsertBlock);
                            }
                            break;
                        case Code.Localloc:
                            stack.Push(builder.BuildArrayAlloca(LLVMTypeRef.Int8, ConvertValue(builder, stack.Pop(), sizeType, false)));
                            break;
                        case Code.Ldtoken:
                            {
                                var tokenType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                var type = localTypes["System.Type"];
                                var typeObject = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2,
                                    runtimeMethods[RuntimeMethod.Newobj].Item1,
                                    [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(type), false)]);
                                InitializeRuntimeType(builder, typeObject, type);
                                StoreField(builder, typeObject, type.Fields.First(field => field.Name == "Name"), BuildStringValue(builder, tokenType.Name));
                                StoreField(builder, typeObject, type.Fields.First(field => field.Name == "Namespace"),
                                    string.IsNullOrEmpty(tokenType.Namespace) ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)) : BuildStringValue(builder, tokenType.Namespace));
                                StoreField(builder, typeObject, type.Fields.First(field => field.Name == "FullName"), BuildStringValue(builder, tokenType.FullName.Replace('/', '+')));
                                stack.Push(typeObject);
                            }
                            break;
                        case Code.Box:
                            {
                                var value = stack.Pop();
                                var valueType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                if (IsManagedReferenceType(valueType))
                                {
                                    var reference = ConvertValue(builder, value, exceptionPointerType);
                                    stack.Push(reference);
                                    TrackType(reference, valueType);
                                    break;
                                }
                                var box = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2, runtimeMethods[RuntimeMethod.Newobj].Item1,
                                    [LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(valueType)), false)]);
                                if (IsValueType(valueType) && value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                                    CopyValue(builder, box, value, GetTypeSize(valueType));
                                else
                                {
                                    var storage = builder.BuildAlloca(value.TypeOf);
                                    builder.BuildStore(value, storage);
                                    CopyValue(builder, box, storage, GetTypeSize(valueType));
                                }
                                stack.Push(box);
                                TrackType(box, valueType);
                            }
                            break;
                        case Code.Unbox:
                        case Code.Unbox_Any:
                            {
                                var value = stack.Pop();
                                var valueType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                if (instr.OpCode.Code == Code.Unbox_Any && (valueType.MetadataType is MetadataType.Boolean or MetadataType.SByte or MetadataType.Byte or MetadataType.Char or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.IntPtr or MetadataType.UIntPtr or MetadataType.Single or MetadataType.Double))
                                    stack.Push(builder.BuildLoad2(GetLLVMTypeRef(valueType), value));
                                else if (instr.OpCode.Code == Code.Unbox_Any && !IsValueType(valueType))
                                    stack.Push(ConvertValue(builder, value, GetLLVMTypeRef(valueType)));
                                else
                                    stack.Push(value);
                            }
                            break;
                        case Code.Switch:
                            {
                                var value = stack.Pop();
                                var targets = (Instruction[])instr.Operand;
                                var defaultBlock = instr.Next is null ? label[targets[0].Offset] : label[instr.Next.Offset];
                                var switchBlock = builder.InsertBlock;
                                for (uint i = 0; i < targets.Length; i++)
                                    SaveStack(label[targets[i].Offset]);
                                SaveStack(defaultBlock);
                                var switchValue = builder.BuildSwitch(value, defaultBlock, (uint)targets.Length);
                                for (uint i = 0; i < targets.Length; i++)
                                    switchValue.AddCase(LLVMValueRef.CreateConstInt(sizeType, i, false), label[targets[i].Offset]);
                                if (instr.Next is not null)
                                {
                                    builder.PositionAtEnd(defaultBlock);
                                    RestoreStack(defaultBlock);
                                }
                                terminatedBlocks.Add(switchBlock);
                            }
                            break;
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                            {
                                var branchStart = (Instruction)instr.Operand;

                                switch (instr.OpCode.Code)
                                {
                                    case Code.Brfalse:
                                    case Code.Brfalse_S:
                                    case Code.Brtrue:
                                    case Code.Brtrue_S:
                                        {
                                            var value = stack.Pop();
                                            var zero = LLVMValueRef.CreateConstNull(value.TypeOf);
                                            var condition = builder.BuildICmp(
                                                instr.OpCode.Code is Code.Brtrue or Code.Brtrue_S
                                                    ? LLVMIntPredicate.LLVMIntNE
                                                    : LLVMIntPredicate.LLVMIntEQ,
                                                value,
                                                zero);

                                            var fallthrough = instr.Next is null ? label[branchStart.Offset] : label[instr.Next.Offset];
                                            var branch = label[branchStart.Offset]; // Label added above
                                            SaveStack(branch);
                                            SaveStack(fallthrough);
                                            builder.BuildCondBr(condition, branch, fallthrough);
                                            terminatedBlocks.Add(builder.InsertBlock);
                                            if (instr.Next is not null)
                                            {
                                                builder.PositionAtEnd(fallthrough);
                                                RestoreStack(fallthrough);
                                            }
                                        }
                                        break;

                                    case Code.Br:
                                    case Code.Br_S:
                                        {
                                            var branch = label[branchStart.Offset]; // Label added above

                                            SaveStack(branch);
                                            builder.BuildBr(branch);
                                            terminatedBlocks.Add(builder.InsertBlock);
                                        }
                                        break;

                                    default:
                                        {
                                            var val2 = stack.Pop();
                                            var val1 = stack.Pop();
                                             var condition = BuildComparison(builder, instr.OpCode.Code, val1, val2);
                                             var fallthrough = instr.Next is null ? label[branchStart.Offset] : label[instr.Next.Offset];
                                             SaveStack(label[branchStart.Offset]);
                                             SaveStack(fallthrough);
                                             builder.BuildCondBr(condition, label[branchStart.Offset], fallthrough);
                                             terminatedBlocks.Add(builder.InsertBlock);
                                             if (instr.Next is not null)
                                             {
                                                 builder.PositionAtEnd(fallthrough);
                                                 RestoreStack(fallthrough);
                                             }
                                        }
                                        break;
                                }
                            }
                            break;
                        case Code.Ceq:
                        case Code.Cgt:
                        case Code.Cgt_Un:
                        case Code.Clt:
                        case Code.Clt_Un:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                 var cond = BuildComparison(builder, instr.OpCode.Code, val1, val2);
                                 var result = builder.BuildZExt(cond, sizeType);

                                stack.Push(result);
                            }
                            break;
                        case Code.Sub:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = val1.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                    ? builder.BuildGEP2(LLVMTypeRef.Int8, val1, [ConvertValue(builder, val2, sizeType, true)])
                                    : builder.BuildSub(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Add_Ovf:
                        case Code.Add_Ovf_Un:
                        case Code.Add:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = val1.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                    ? builder.BuildGEP2(LLVMTypeRef.Int8, val1, [ConvertValue(builder, val2, sizeType, true)])
                                    : val2.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                        ? builder.BuildGEP2(LLVMTypeRef.Int8, val2, [ConvertValue(builder, val1, sizeType, true)])
                                        : builder.BuildAdd(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Sub_Ovf:
                        case Code.Sub_Ovf_Un:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(val1.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                    ? builder.BuildGEP2(LLVMTypeRef.Int8, val1, [builder.BuildNeg(ConvertValue(builder, val2, sizeType, true))])
                                    : builder.BuildSub(val1, val2));
                            }
                            break;
                        case Code.Mul_Ovf:
                        case Code.Mul_Ovf_Un:
                        case Code.Mul:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildMul(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Div:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildSDiv(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Div_Un:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildUDiv(val1, val2));
                            }
                            break;
                        case Code.Rem:
                        case Code.Rem_Un:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(instr.OpCode.Code == Code.Rem
                                    ? builder.BuildSRem(val1, val2)
                                    : builder.BuildURem(val1, val2));
                            }
                            break;
                        case Code.And:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildAnd(val1, val2));
                            }
                            break;
                        case Code.Or:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildOr(val1, val2));
                            }
                            break;
                        case Code.Xor:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildXor(val1, val2));
                            }
                            break;
                        case Code.Shl:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildShl(val1, val2));
                            }
                            break;
                        case Code.Shr:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildAShr(val1, val2));
                            }
                            break;
                        case Code.Shr_Un:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                stack.Push(builder.BuildLShr(val1, val2));
                            }
                            break;
                        case Code.Neg:
                            stack.Push(builder.BuildNeg(stack.Pop()));
                            break;
                        case Code.Not:
                            stack.Push(builder.BuildNot(stack.Pop()));
                            break;
                        case Code.Conv_I1:
                        case Code.Conv_Ovf_I1:
                        case Code.Conv_Ovf_I1_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int8));
                            break;
                        case Code.Conv_U1:
                        case Code.Conv_Ovf_U1:
                        case Code.Conv_Ovf_U1_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int8, false));
                            break;
                        case Code.Conv_I2:
                        case Code.Conv_Ovf_I2:
                        case Code.Conv_Ovf_I2_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int16));
                            break;
                        case Code.Conv_U2:
                        case Code.Conv_Ovf_U2:
                        case Code.Conv_Ovf_U2_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int16, false));
                            break;
                        case Code.Conv_I4:
                        case Code.Conv_Ovf_I4:
                        case Code.Conv_Ovf_I4_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int32));
                            break;
                        case Code.Conv_U4:
                        case Code.Conv_Ovf_U4:
                        case Code.Conv_Ovf_U4_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int32, false));
                            break;
                        case Code.Conv_I8:
                        case Code.Conv_Ovf_I8:
                        case Code.Conv_Ovf_I8_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int64));
                            break;
                        case Code.Conv_U8:
                        case Code.Conv_Ovf_U8:
                        case Code.Conv_Ovf_U8_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Int64, false));
                            break;
                        case Code.Conv_I:
                        case Code.Conv_Ovf_I:
                        case Code.Conv_Ovf_I_Un:
                        case Code.Conv_U:
                        case Code.Conv_Ovf_U:
                        case Code.Conv_Ovf_U_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), sizeType, instr.OpCode.Code is not (Code.Conv_U or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un)));
                            break;
                        case Code.Conv_R4:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Float));
                            break;
                        case Code.Conv_R8:
                        case Code.Conv_R_Un:
                            stack.Push(ConvertValue(builder, stack.Pop(), LLVMTypeRef.Double));
                            break;
                        case Code.Dup:
                            {
                                var value = stack.Pop();
                                stack.Push(value);
                                stack.Push(value);
                            }
                            break;
                        default:
                            NotImplemented(instr);
                            break;
                        }
                    previousInstruction = instr;
                    }

                entryBuilder.PositionAtEnd(allocaBlock);
                entryBuilder.BuildBr(entry);

                foreach (var block in label.Values.Distinct())
                {
                    if (terminatedBlocks.Contains(block))
                        continue;
                    builder.PositionAtEnd(block);
                    var returnType = SubstituteGenericParameter(method.Value.Item3.ReturnType, method.Value.Item3);
                    if (returnType.MetadataType == MetadataType.Void)
                        builder.BuildRetVoid();
                    else
                        builder.BuildRet(LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType)));
                    terminatedBlocks.Add(block);
                }
                if (!terminatedBlocks.Contains(entry))
                {
                    builder.PositionAtEnd(entry);
                    var returnType = SubstituteGenericParameter(method.Value.Item3.ReturnType, method.Value.Item3);
                    if (returnType.MetadataType == MetadataType.Void)
                        builder.BuildRetVoid();
                    else
                        builder.BuildRet(LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType)));
                }
            }
        }
    }

    IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in GetAllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    LLVMValueRef GetArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef index, LLVMTypeRef elementType)
    {
        var nativeIndex = ConvertValue(builder, index, sizeType, false);
        var elementOffset = builder.BuildMul(nativeIndex, LLVMValueRef.CreateConstInt(sizeType, (ulong)GetLLVMTypeSize(elementType), false));
        var dataOffset = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(GetArrayLengthField().DeclaringType), false);
        var byteOffset = builder.BuildAdd(dataOffset, elementOffset);
        return builder.BuildGEP2(LLVMTypeRef.Int8, array, [byteOffset]);
    }

    LLVMValueRef GetMultiArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef[] indices, LLVMTypeRef elementType)
    {
        var elementIndex = LLVMValueRef.CreateConstInt(sizeType, 0, false);
        for (int i = 0; i < indices.Length; i++)
        {
            var lengthField = localTypes["System.Array"].Fields.First(field => field.Name == $"_length{i}");
            var length = builder.BuildLoad2(sizeType, GetFieldAddress(builder, array, lengthField));
            elementIndex = builder.BuildAdd(builder.BuildMul(elementIndex, length), ConvertValue(builder, indices[i], sizeType, false));
        }
        var elementOffset = builder.BuildMul(elementIndex, LLVMValueRef.CreateConstInt(sizeType, (ulong)GetLLVMTypeSize(elementType), false));
        var dataOffset = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false);
        return builder.BuildGEP2(LLVMTypeRef.Int8, array, [builder.BuildAdd(dataOffset, elementOffset)]);
    }

    LLVMValueRef GetStringConstant(LLVMBuilderRef builder, string value)
    {
        if (!stringConstants.TryGetValue(value, out var constant))
        {
            constant = builder.BuildGlobalStringPtr(value);
            stringConstants.Add(value, constant);
        }
        return constant;
    }

    LLVMValueRef BuildStringValue(LLVMBuilderRef builder, string value)
    {
        var array = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newarr].Item2,
            runtimeMethods[RuntimeMethod.Newarr].Item1,
            [LLVMValueRef.CreateConstInt(sizeType, (ulong)(value.Length + 1), false),
             LLVMValueRef.CreateConstInt(sizeType, (ulong)GetMetadataTypeSize(MetadataType.Char), false),
             LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false)]);
        StoreField(builder, array, GetArrayLengthField(), LLVMValueRef.CreateConstInt(sizeType, (ulong)value.Length, false));
        for (int i = 0; i < value.Length; i++)
        {
            var address = GetArrayElementAddress(builder, array, LLVMValueRef.CreateConstInt(sizeType, (ulong)i, false), LLVMTypeRef.Int16);
            builder.BuildStore(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int16, value[i], false), address);
        }
        var stringType = localTypes["System.String"];
        var stringObject = builder.BuildCall2(runtimeMethods[RuntimeMethod.Newobj].Item2,
            runtimeMethods[RuntimeMethod.Newobj].Item1,
            [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(stringType), false)]);
        InitializeRuntimeType(builder, stringObject, stringType);
        var constructor = moduleMethods.Values.First(method =>
            method.Item3.DeclaringType.FullName == "System.String" && method.Item3.Name == ".ctor" && method.Item3.Parameters.Count == 1);
        builder.BuildCall2(constructor.Item2, constructor.Item1, [stringObject, array]);
        return stringObject;
    }

    LLVMValueRef GetFieldAddress(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field)
    {
        var offset = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetFieldOffset(field), false);
        return builder.BuildGEP2(LLVMTypeRef.Int8, obj, [offset]);
    }

    Tuple<LLVMValueRef, LLVMTypeRef> CreateLocalStorage(LLVMBuilderRef builder, TypeReference type)
    {
        if (IsValueType(type))
        {
            var pointerType = LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
            var storageType = LLVMTypeRef.CreateArray(LLVMTypeRef.Int8, (uint)Math.Max(1, GetTypeSize(type)));
            var storage = builder.BuildBitCast(builder.BuildAlloca(storageType), pointerType);
            var local = builder.BuildAlloca(pointerType);
            builder.BuildStore(storage, local);
            return new(local, pointerType);
        }

        var llvmType = GetLLVMTypeRef(type);
        return new(builder.BuildAlloca(llvmType), llvmType);
    }

    void CopyValue(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef source, int size)
    {
        var pointerType = LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
        var destinationPointer = destination.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            ? (destination.TypeOf.Equals(pointerType) ? destination : builder.BuildBitCast(destination, pointerType))
            : builder.BuildAlloca(destination.TypeOf);
        var sourcePointer = source.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            ? (source.TypeOf.Equals(pointerType) ? source : builder.BuildBitCast(source, pointerType))
            : builder.BuildAlloca(source.TypeOf);
        if (destination.TypeOf.Kind != LLVMTypeKind.LLVMPointerTypeKind)
            builder.BuildStore(source, destinationPointer);
        if (source.TypeOf.Kind != LLVMTypeKind.LLVMPointerTypeKind)
            builder.BuildStore(source, sourcePointer);
        unsafe
        {
            LLVM.BuildMemCpy(builder, destinationPointer, 1, sourcePointer, 1,
                LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
        }
    }

    void StoreField(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, LLVMValueRef value)
    {
        if (IsValueType(field.FieldType))
        {
            CopyValue(builder, GetFieldAddress(builder, obj, field), value, GetTypeSize(field.FieldType));
            return;
        }
        var fieldType = GetLLVMTypeRef(field.FieldType);
        builder.BuildStore(ConvertValue(builder, value, fieldType), GetFieldAddress(builder, obj, field));
    }

    FieldDefinition GetArrayLengthField()
    {
        return localTypes["System.Array"].Fields.First(field => field.Name == "Length");
    }

    string GetRuntimeTypeKey(TypeReference type)
    {
        if (type is GenericInstanceType generic)
            return generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(GetRuntimeTypeKey)) + ">";
        return type.FullName;
    }

    ulong GetRuntimeTypeId(TypeReference type)
    {
        var runtimeType = type is GenericInstanceType generic && generic.Resolve()?.IsClass == true
            ? generic.ElementType
            : type;
        var key = GetRuntimeTypeKey(runtimeType);
        if (!runtimeTypeIds.TryGetValue(key, out var id))
        {
            id = nextRuntimeTypeId++;
            runtimeTypeIds.Add(key, id);
        }
        return id;
    }

    void InitializeRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj, TypeReference type)
    {
        if (obj == default || IsValueType(type))
            return;
        StoreField(builder, obj, GetObjectMethodTableField(),
            LLVMValueRef.CreateConstInt(sizeType, GetRuntimeTypeId(type), false));
    }

    bool IsRuntimeTypeCompatible(TypeDefinition runtimeType, TypeReference targetType)
    {
        var targetDefinition = targetType.Resolve();
        if (targetDefinition is null)
            return false;
        if (targetDefinition.IsInterface)
            return ImplementsInterface(runtimeType, targetDefinition);
        for (var current = runtimeType; current is not null;)
        {
            if (SameTypeName(current, targetType))
                return true;
            if (current.BaseType is null || !localTypes.TryGetValue(current.BaseType.FullName, out current))
                break;
        }
        return false;
    }

    List<(TypeDefinition RuntimeType, MethodReference Implementation)> GetVirtualImplementations(MethodReference targetMethod)
    {
        var implementations = new List<(TypeDefinition RuntimeType, MethodReference Implementation)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in localTypes.Values.Where(candidate => !candidate.IsInterface && !candidate.IsValueType)
                     .OrderByDescending(GetTypeDepth))
        {
            if (!IsRuntimeTypeCompatible(type, targetMethod.DeclaringType))
                continue;
            var implementation = FindMethodImplementation(type, targetMethod);
            if (implementation is null || !implementation.HasBody || !seen.Add(GetRuntimeTypeKey(type)))
                continue;
            implementations.Add((type, implementation));
        }
        return implementations;
    }

    bool IsKnownRuntimeType(TypeReference type)
    {
        return type.Resolve() is { } definition && localTypes.ContainsKey(definition.FullName);
    }

    int GetTypeDepth(TypeDefinition type)
    {
        int depth = 0;
        for (var current = type; current.BaseType is not null && localTypes.ContainsKey(current.BaseType.FullName); current = localTypes[current.BaseType.FullName])
            depth++;
        return depth;
    }

    FieldDefinition GetLocalFieldByName(string name, string typeName)
    {
        return localTypes[typeName].Fields.First(field => field.Name == name);
    }

    FieldDefinition GetDelegateField(TypeReference type, string name)
    {
        var current = type.Resolve();
        while (current is not null)
        {
            var field = current.Fields.FirstOrDefault(candidate => candidate.Name == name && !candidate.IsStatic);
            if (field is not null)
                return field;
            current = current.BaseType?.Resolve();
        }
        throw new NotSupportedException($"Delegate field is not defined: {type.FullName}.{name}");
    }

    bool IsDelegateType(TypeReference type)
    {
        for (var current = type.Resolve(); current is not null;)
        {
            if (current.FullName == "System.Delegate" || current.FullName == "System.MulticastDelegate")
                return true;
            if (current.BaseType is null)
                break;
            current = current.BaseType.Resolve();
        }
        return false;
    }

    LLVMTypeRef GetLLVMTypeRef(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetLLVMTypeRef(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetLLVMTypeRef(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetLLVMTypeRef(pinned.ElementType);
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetLLVMTypeRef(enumUnderlyingType);
        if (type is ByReferenceType byReference)
            return LLVMTypeRef.CreatePointer(GetLLVMTypeRef(byReference.ElementType), 0);
        if (type is PointerType)
            return LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
        if (type is ArrayType)
            return LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
        if (type is GenericParameter)
            return sizeType;
        return GetLLVMTypeRefFromMetadataType(type.MetadataType);
    }

    TypeReference? GetEnumUnderlyingType(TypeReference type)
    {
        var resolved = type.Resolve();
        return resolved is { IsEnum: true } definition
            ? definition.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType
            : null;
    }

    bool IsNoReturnMethod(MethodReference method, Dictionary<string, MethodDefinition> methods)
    {
        var definition = FindLocalMethod(method, methods);
        if (definition is null || !definition.HasBody || definition.Body.Instructions.Count == 0)
            return false;
        var last = definition.Body.Instructions.LastOrDefault(instruction => instruction.OpCode.Code is not Code.Nop);
        return last?.OpCode.Code is Code.Throw or Code.Rethrow;
    }

    MethodDefinition? FindLocalMethod(MethodReference reference, Dictionary<string, MethodDefinition> methods)
    {
        if (methods.TryGetValue(reference.FullName, out var exact))
            return exact;
        var declaringName = reference.DeclaringType is GenericInstanceType generic
            ? generic.ElementType.FullName
            : reference.DeclaringType.FullName;
        return methods.Values.FirstOrDefault(candidate =>
            candidate.DeclaringType.FullName == declaringName &&
            candidate.Name == reference.Name &&
            candidate.Parameters.Count == reference.Parameters.Count);
    }

    TypeReference SubstituteGenericParameter(TypeReference type, MethodReference method)
    {
        if (type is not GenericParameter parameter)
            return ResolveGenericType(type, method);
        if (parameter.Type == GenericParameterType.Type && method.DeclaringType is GenericInstanceType declaringType && parameter.Position < declaringType.GenericArguments.Count)
            return declaringType.GenericArguments[parameter.Position];
        if (parameter.Type == GenericParameterType.Method && method is GenericInstanceMethod genericMethod && parameter.Position < genericMethod.GenericArguments.Count)
            return genericMethod.GenericArguments[parameter.Position];
        return type;
    }

    TypeReference? GetMethodVariableType(MethodReference method, int index)
    {
        var definition = method.Resolve();
        if (definition?.HasBody != true || index < 0 || index >= definition.Body.Variables.Count)
            return null;
        return SubstituteGenericParameter(definition.Body.Variables[index].VariableType, method);
    }

    FieldDefinition GetObjectMethodTableField()
    {
        return localTypes["System.Object"].Fields.First(field => field.Name == "m_pMethodTable");
    }

    int GetObjectHeaderSize()
    {
        var field = GetObjectMethodTableField();
        return GetFieldOffset(field) + GetTypeSize(field.FieldType);
    }

    FieldDefinition GetLocalField(FieldReference field)
    {
        var resolvedField = field.Resolve();
        if (resolvedField is not null && localTypes.ContainsKey(resolvedField.DeclaringType.FullName))
            return resolvedField;
        var declaringType = field.DeclaringType is GenericInstanceType genericType
            ? genericType.ElementType
            : field.DeclaringType;
        if (!localTypes.TryGetValue(declaringType.FullName, out var type))
            throw new NotSupportedException($"Field is not defined in the input module: {field.FullName}");
        while (true)
        {
            var definition = type.Fields.FirstOrDefault(candidate => candidate.Name == field.Name);
            if (definition is not null)
                return definition;
            if (type.BaseType is null || !localTypes.TryGetValue(type.BaseType.FullName, out type))
                break;
        }
        throw new NotSupportedException($"Field is not defined in the input module: {field.FullName}");
    }

    TypeReference SubstituteFieldType(FieldReference field, MethodReference? context = null)
    {
        var resolved = field.Resolve();
        var declaringType = field.DeclaringType as GenericInstanceType;
        var contextDeclaringType = context?.DeclaringType as GenericInstanceType;
        if (contextDeclaringType is not null && (declaringType is null || declaringType.GenericArguments.Any(argument => argument is GenericParameter)))
            declaringType = contextDeclaringType;
        var fieldType = resolved?.FieldType ?? field.FieldType;
        if (declaringType is not null)
        {
            if (fieldType is GenericParameter parameter && parameter.Position < declaringType.GenericArguments.Count)
                fieldType = declaringType.GenericArguments[parameter.Position];
            else if (fieldType is GenericInstanceType fieldGeneric && fieldGeneric.GenericArguments.Any(argument => argument is GenericParameter))
            {
                var arguments = fieldGeneric.GenericArguments.Select(argument => argument is GenericParameter genericParameter &&
                    genericParameter.Position < declaringType.GenericArguments.Count
                        ? declaringType.GenericArguments[genericParameter.Position]
                        : argument).ToArray();
                fieldType = new GenericInstanceType(fieldGeneric.ElementType);
                for (int i = 0; i < arguments.Length; i++)
                    ((GenericInstanceType)fieldType).GenericArguments.Add(arguments[i]);
            }
        }
        return context is null ? fieldType : SubstituteGenericParameter(fieldType, context);
    }

    TypeReference ResolveGenericType(TypeReference type, MethodReference context)
    {
        if (type is GenericParameter parameter)
        {
            if (parameter.Type == GenericParameterType.Type && context.DeclaringType is GenericInstanceType declaring && parameter.Position < declaring.GenericArguments.Count)
                return declaring.GenericArguments[parameter.Position];
            if (parameter.Type == GenericParameterType.Method && context is GenericInstanceMethod method && parameter.Position < method.GenericArguments.Count)
                return method.GenericArguments[parameter.Position];
        }
        if (type is GenericInstanceType generic)
        {
            var result = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                result.GenericArguments.Add(ResolveGenericType(argument, context));
            return result;
        }
        return type;
    }

    TypeReference SubstituteGenericTypeArguments(TypeReference type, GenericInstanceType declaringType)
    {
        if (type is GenericParameter parameter && parameter.Type == GenericParameterType.Type &&
            parameter.Position < declaringType.GenericArguments.Count)
            return declaringType.GenericArguments[parameter.Position];
        if (type is GenericInstanceType generic)
        {
            var result = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                result.GenericArguments.Add(SubstituteGenericTypeArguments(argument, declaringType));
            return result;
        }
        if (type is ArrayType array)
            return new ArrayType(SubstituteGenericTypeArguments(array.ElementType, declaringType), array.Rank);
        if (type is ByReferenceType byReference)
            return new ByReferenceType(SubstituteGenericTypeArguments(byReference.ElementType, declaringType));
        if (type is PointerType pointer)
            return new PointerType(SubstituteGenericTypeArguments(pointer.ElementType, declaringType));
        return type;
    }

    int GetFieldOffset(FieldDefinition field)
    {
        var offset = IsValueType(field.DeclaringType) || field.DeclaringType.BaseType is null
            ? 0
            : GetBaseTypeSize(field.DeclaringType.BaseType);
        foreach (var candidate in field.DeclaringType.Fields.TakeWhile(candidate => candidate.Name != field.Name).Where(candidate => !candidate.IsStatic))
        {
            offset = AlignUp(offset, GetTypeAlignment(candidate.FieldType));
            offset += GetTypeSize(candidate.FieldType);
        }
        return AlignUp(offset, GetTypeAlignment(field.FieldType));
    }

    int GetBaseTypeSize(TypeReference? type)
    {
        if (type is null)
            return 0;
        return localTypes.TryGetValue(type.FullName, out var definition)
            ? GetTypeDefinitionSize(definition)
            : GetMetadataTypeSize(type.MetadataType);
    }

    int GetTypeSize(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetTypeSize(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetTypeSize(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetTypeSize(pinned.ElementType);
        if (type is GenericInstanceType genericInstance && localTypes.TryGetValue(genericInstance.ElementType.FullName, out var genericDefinition))
        {
            if (!IsValueType(genericDefinition))
                return pointerSize;
            var offset = 0;
            var alignment = 1;
            foreach (var field in genericDefinition.Fields.Where(field => !field.IsStatic))
            {
                var fieldType = SubstituteGenericTypeArguments(field.FieldType, genericInstance);
                var fieldAlignment = GetTypeAlignment(fieldType);
                offset = AlignUp(offset, fieldAlignment);
                offset += GetTypeSize(fieldType);
                alignment = Math.Max(alignment, fieldAlignment);
            }
            return AlignUp(offset, alignment);
        }
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetTypeSize(enumUnderlyingType);
        if (type.MetadataType is MetadataType.IntPtr or MetadataType.UIntPtr)
            return pointerSize;
        if (type.MetadataType is MetadataType.Boolean or MetadataType.SByte or MetadataType.Byte or MetadataType.Char or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double)
            return GetMetadataTypeSize(type.MetadataType);
        if (IsValueType(type) && localTypes.TryGetValue(type.FullName, out var valueTypeDefinition))
            return GetTypeDefinitionSize(valueTypeDefinition);
        if (type.MetadataType is MetadataType.Object or MetadataType.Array or MetadataType.Class or MetadataType.String or MetadataType.Pointer or MetadataType.ByReference)
            return pointerSize;
        if (localTypes.TryGetValue(type.FullName, out var definition))
            return GetTypeDefinitionSize(definition);
        return GetMetadataTypeSize(type.MetadataType);
    }

    int GetTypeDefinitionSize(TypeDefinition type)
    {
        var offset = IsValueType(type) || type.BaseType is null ? 0 : GetBaseTypeSize(type.BaseType);
        var alignment = GetTypeDefinitionAlignment(type);
        foreach (var field in type.Fields.Where(field => !field.IsStatic))
        {
            var fieldAlignment = GetTypeAlignment(field.FieldType);
            offset = AlignUp(offset, fieldAlignment);
            offset += GetTypeSize(field.FieldType);
            alignment = Math.Max(alignment, fieldAlignment);
        }
        if (type.FullName == "System.Object")
            offset = Math.Max(offset, GetObjectHeaderSize());
        return AlignUp(offset, alignment);
    }

    int GetTypeDefinitionAlignment(TypeDefinition type)
    {
        var alignment = IsValueType(type) || type.BaseType is null ? 1 : GetTypeAlignment(type.BaseType);
        foreach (var field in type.Fields.Where(field => !field.IsStatic))
            alignment = Math.Max(alignment, GetTypeAlignment(field.FieldType));
        return alignment;
    }

    bool IsValueType(TypeReference type)
    {
        if (type.FullName is "System.ValueType" or "System.Enum")
            return false;
        if (type.Resolve()?.IsEnum == true)
            return false;
        if (type.MetadataType is MetadataType.Void or MetadataType.Boolean or MetadataType.Char or
            MetadataType.SByte or MetadataType.Byte or MetadataType.Int16 or MetadataType.UInt16 or
            MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or
            MetadataType.Single or MetadataType.Double or MetadataType.IntPtr or MetadataType.UIntPtr)
            return false;
        if (type is GenericInstanceType generic)
            return generic.ElementType.Resolve()?.IsValueType == true;
        return type.MetadataType == MetadataType.ValueType ||
            localTypes.TryGetValue(type.FullName, out var definition) && definition.IsValueType;
    }

    bool IsManagedReferenceType(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return IsManagedReferenceType(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return IsManagedReferenceType(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return IsManagedReferenceType(pinned.ElementType);
        if (type is ArrayType || type.MetadataType is MetadataType.Object or MetadataType.Class or MetadataType.String or MetadataType.Array)
            return true;
        if (type is ByReferenceType or PointerType || GetEnumUnderlyingType(type) is not null)
            return false;
        return type.Resolve() is { IsValueType: false };
    }

    int GetTypeAlignment(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetTypeAlignment(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetTypeAlignment(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetTypeAlignment(pinned.ElementType);
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetTypeAlignment(enumUnderlyingType);
        if (type is GenericInstanceType genericInstance && IsValueType(genericInstance))
        {
            var genericDefinition = genericInstance.ElementType.Resolve();
            var alignment = 1;
            foreach (var field in genericDefinition?.Fields.Where(field => !field.IsStatic) ?? [])
                alignment = Math.Max(alignment, GetTypeAlignment(SubstituteGenericTypeArguments(field.FieldType, genericInstance)));
            return alignment;
        }
        if (IsValueType(type) && localTypes.TryGetValue(type.FullName, out var definition))
            return GetTypeDefinitionAlignment(definition);
        return (int)machine.CreateTargetDataLayout().ABIAlignmentOfType(GetLLVMTypeRefFromMetadataType(type.MetadataType));
    }

    int AlignUp(int value, int alignment)
    {
        return alignment <= 1 ? value : checked((value + alignment - 1) / alignment * alignment);
    }

    ulong GetLLVMTypeSize(LLVMTypeRef type)
    {
        return (ulong)machine.CreateTargetDataLayout().ABISizeOfType(type);
    }

    LLVMValueRef ConvertValue(LLVMBuilderRef builder, LLVMValueRef value, LLVMTypeRef target, bool signed = true)
    {
        var source = value.TypeOf;
        if (source.Equals(target)) return value;
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (source.IntWidth < target.IntWidth)
                return signed ? builder.BuildSExt(value, target) : builder.BuildZExt(value, target);
            return builder.BuildTrunc(value, target);
        }
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
            return signed ? builder.BuildSIToFP(value, target) : builder.BuildUIToFP(value, target);
        if (source.Kind == LLVMTypeKind.LLVMFloatTypeKind && target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            return signed ? builder.BuildFPToSI(value, target) : builder.BuildFPToUI(value, target);
        if (source.Kind == LLVMTypeKind.LLVMFloatTypeKind && target.Kind == LLVMTypeKind.LLVMDoubleTypeKind)
            return builder.BuildFPExt(value, target);
        if (source.Kind == LLVMTypeKind.LLVMDoubleTypeKind && target.Kind == LLVMTypeKind.LLVMFloatTypeKind)
            return builder.BuildFPTrunc(value, target);
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind == LLVMTypeKind.LLVMPointerTypeKind)
            return builder.BuildIntToPtr(value, target);
        if (source.Kind == LLVMTypeKind.LLVMPointerTypeKind && target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            return builder.BuildPtrToInt(value, target);
        return value;
    }

    LLVMValueRef BuildComparison(LLVMBuilderRef builder, Code code, LLVMValueRef left, LLVMValueRef right)
    {
        var leftType = left.TypeOf;
        var rightType = right.TypeOf;
        if (!leftType.Equals(rightType))
        {
            if (leftType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind ||
                rightType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
            {
                var target = leftType.Kind == LLVMTypeKind.LLVMDoubleTypeKind || rightType.Kind == LLVMTypeKind.LLVMDoubleTypeKind
                    ? LLVMTypeRef.Double
                    : LLVMTypeRef.Float;
                left = ConvertValue(builder, left, target);
                right = ConvertValue(builder, right, target);
            }
            else if (leftType.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                right = ConvertValue(builder, right, leftType);
            else if (rightType.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                left = ConvertValue(builder, left, rightType);
            else if (leftType.Kind == LLVMTypeKind.LLVMIntegerTypeKind && rightType.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            {
                var target = leftType.IntWidth >= rightType.IntWidth ? leftType : rightType;
                left = ConvertValue(builder, left, target);
                right = ConvertValue(builder, right, target);
            }
        }

        if (left.TypeOf.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
        {
            var predicate = code switch
            {
                Code.Ceq or Code.Beq or Code.Beq_S => LLVMRealPredicate.LLVMRealOEQ,
                Code.Cgt or Code.Cgt_Un or Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S => LLVMRealPredicate.LLVMRealOGT,
                Code.Clt or Code.Clt_Un or Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S => LLVMRealPredicate.LLVMRealOLT,
                Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S => LLVMRealPredicate.LLVMRealOGE,
                Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S => LLVMRealPredicate.LLVMRealOLE,
                Code.Bne_Un or Code.Bne_Un_S => LLVMRealPredicate.LLVMRealONE,
                _ => LLVMRealPredicate.LLVMRealOEQ
            };
            return builder.BuildFCmp(predicate, left, right);
        }

        var intPredicate = code switch
        {
            Code.Ceq or Code.Beq or Code.Beq_S => LLVMIntPredicate.LLVMIntEQ,
            Code.Cgt or Code.Bgt or Code.Bgt_S => LLVMIntPredicate.LLVMIntSGT,
            Code.Cgt_Un or Code.Bgt_Un or Code.Bgt_Un_S => LLVMIntPredicate.LLVMIntUGT,
            Code.Clt or Code.Blt or Code.Blt_S => LLVMIntPredicate.LLVMIntSLT,
            Code.Clt_Un or Code.Blt_Un or Code.Blt_Un_S => LLVMIntPredicate.LLVMIntULT,
            Code.Bge or Code.Bge_S => LLVMIntPredicate.LLVMIntSGE,
            Code.Bge_Un or Code.Bge_Un_S => LLVMIntPredicate.LLVMIntUGE,
            Code.Ble or Code.Ble_S => LLVMIntPredicate.LLVMIntSLE,
            Code.Ble_Un or Code.Ble_Un_S => LLVMIntPredicate.LLVMIntULE,
            Code.Bne_Un or Code.Bne_Un_S => LLVMIntPredicate.LLVMIntNE,
            _ => LLVMIntPredicate.LLVMIntEQ
        };
        return builder.BuildICmp(intPredicate, left, right);
    }

    string GetLabelName(Instruction instr) => $"IL_{instr.Offset.ToString("x2").PadLeft(4, '0').ToUpper()}";

    int GetMetadataTypeSize(MetadataType type) => type switch
    {
        MetadataType.Boolean => 1,
        MetadataType.SByte => 1,
        MetadataType.Byte => 1,
        MetadataType.Char => 2,
        MetadataType.Int16 => 2,
        MetadataType.UInt16 => 2,
        MetadataType.Int32 => 4,
        MetadataType.UInt32 => 4,
        MetadataType.Int64 => 8,
        MetadataType.UInt64 => 8,
        MetadataType.Single => 4,
        MetadataType.Double => 8,
        MetadataType.IntPtr => pointerSize,
        MetadataType.UIntPtr => pointerSize,
        MetadataType.Pointer => pointerSize,
        MetadataType.ByReference => pointerSize,
        MetadataType.Array => pointerSize,
        MetadataType.Class => pointerSize,
        MetadataType.Object => pointerSize,
        MetadataType.String => pointerSize,
        MetadataType.ValueType => pointerSize,
        _ => pointerSize
    };

    LLVMTypeRef GetLLVMTypeRefFromMetadataType(MetadataType type) => type switch
    {
        MetadataType.Void => LLVMTypeRef.Void,
        MetadataType.Boolean => LLVMTypeRef.Int8,
        MetadataType.SByte => LLVMTypeRef.Int8,
        MetadataType.Byte => LLVMTypeRef.Int8,
        MetadataType.Char => LLVMTypeRef.Int16,
        MetadataType.Int16 => LLVMTypeRef.Int16,
        MetadataType.UInt16 => LLVMTypeRef.Int16,
        MetadataType.Int32 => LLVMTypeRef.Int32,
        MetadataType.UInt32 => LLVMTypeRef.Int32,
        MetadataType.Int64 => LLVMTypeRef.Int64,
        MetadataType.UInt64 => LLVMTypeRef.Int64,
        MetadataType.Single => LLVMTypeRef.Float,
        MetadataType.Double => LLVMTypeRef.Double,
        MetadataType.IntPtr => sizeType,
        MetadataType.UIntPtr => sizeType,
        MetadataType.Pointer => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.ByReference => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.Array => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.Class => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.Object => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.String => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.ValueType => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        _ => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)
    };

    void NotImplemented(Instruction instr)
    {
        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
        else throw new NotImplementedException();
    }

    int GetMethodParameterCount(MethodReference method)
    {
        int count = method.Parameters.Count;
        if (method.HasThis) count++;
        return count;
    }

    MethodReference ResolveCallTarget(MethodReference targetMethod)
    {
        var declaringTypeName = targetMethod.DeclaringType is GenericInstanceType genericDeclaringType
            ? genericDeclaringType.ElementType.FullName
            : targetMethod.DeclaringType.FullName;
        if (!localTypes.TryGetValue(declaringTypeName, out var declaringType) || !declaringType.IsInterface)
            return targetMethod;

        foreach (var type in localTypes.Values.Where(type => !type.IsInterface))
        {
            if (!ImplementsInterface(type, declaringType))
                continue;

            var implementation = FindMethodImplementation(type, targetMethod);
            if (implementation is not null)
                return implementation;
        }

        return targetMethod;
    }

    MethodReference ResolveVirtualTarget(MethodReference targetMethod, TypeReference receiverType)
    {
        if (receiverType is ByReferenceType byReference)
            receiverType = byReference.ElementType;
        var definition = receiverType.Resolve();
        if (definition is null)
            return ResolveCallTarget(targetMethod);
        var implementation = FindMethodImplementation(definition, targetMethod);
        return implementation ?? ResolveCallTarget(targetMethod);
    }

    bool ImplementsInterface(TypeDefinition type, TypeDefinition interfaceType)
    {
        for (var current = type; current is not null;)
        {
            if (current.Interfaces.Any(@interface => SameTypeName(@interface.InterfaceType, interfaceType)))
                return true;

            if (current.BaseType is null || !localTypes.TryGetValue(current.BaseType.FullName, out current))
                break;
        }

        return false;
    }

    bool SameTypeName(TypeReference left, TypeReference right)
    {
        static string ElementName(TypeReference type) => type is GenericInstanceType generic
            ? generic.ElementType.FullName
            : type.FullName;
        return ElementName(left) == ElementName(right);
    }

    MethodDefinition? FindMethodImplementation(TypeDefinition type, MethodReference targetMethod)
    {
        for (var current = type; current is not null;)
        {
            var implementation = current.Methods.FirstOrDefault(method =>
                !method.IsStatic &&
                (method.Name == targetMethod.Name || method.Name.EndsWith("." + targetMethod.Name, StringComparison.Ordinal)) &&
                method.Parameters.Count == targetMethod.Parameters.Count);
            if (implementation is not null)
                return implementation;

            if (current.BaseType is null || !localTypes.TryGetValue(current.BaseType.FullName, out current))
                break;
        }

        return null;
    }

    LLVMTypeRef CreateLLVMFunction(LLVMModuleRef module, MethodReference method)
    {
        List<LLVMTypeRef> paramTypes = new List<LLVMTypeRef>();
        if (method.HasThis)
        {
            // "this" will be a parameter
            paramTypes.Add(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
        }
        foreach (var p in method.Parameters)
        {
            paramTypes.Add(GetLLVMTypeRef(SubstituteGenericParameter(p.ParameterType, method)));
        }
        LLVMTypeRef returnType = GetLLVMTypeRef(SubstituteGenericParameter(method.ReturnType, method));
        var func = LLVMTypeRef.CreateFunction(returnType, paramTypes.ToArray());
        return func;
    }

    string GetFriendlyMethodName(MethodReference method, TypeReference? methodDeclareType = null)
    {
        const string member_access_operator = ".";
        const string separator = "_";
        TypeReference declareType = methodDeclareType ?? method.DeclaringType;

        List<string> names = new List<string>();
        if (declareType.Namespace != string.Empty) names.Add(declareType.Namespace.Replace(member_access_operator, separator));
        if (declareType.Name != string.Empty)
        {
            var typeName = declareType.Name.Replace(member_access_operator, separator)
                .Replace('/', separator[0])
                .Replace('[', separator[0])
                .Replace(']', separator[0])
                .Replace(',', separator[0]);
            if (declareType is GenericInstanceType genericType)
            {
                typeName += separator + string.Join(separator, genericType.GenericArguments.Select(GetFriendlyTypeName));
            }
            names.Add(typeName);
        }
        if (method.Name != string.Empty) names.Add(method.Name.Replace(member_access_operator, separator));
        if (method is GenericInstanceMethod genericMethod)
            names.AddRange(genericMethod.GenericArguments.Select(GetFriendlyTypeName));
        method.Parameters.ToList().ForEach(p => names.Add(p.ParameterType.MetadataType.ToString()));
        string friendlyMethodName = string.Join(separator, names);
        return friendlyMethodName;
    }

    string GetFriendlyTypeName(TypeReference type)
    {
        if (type is GenericInstanceType generic)
            return generic.ElementType.Name + "_" + string.Join("_", generic.GenericArguments.Select(GetFriendlyTypeName));
        if (type is ArrayType array)
            return GetFriendlyTypeName(array.ElementType) + "_Array";
        return type.FullName.Replace('.', '_').Replace('/', '_').Replace('`', '_');
    }

    string GetFriendlyFieldName(FieldReference field)
    {
        const string member_access_operator = ".";
        const string separator = "_";
        TypeReference declareType = field.DeclaringType;

        List<string> names = new List<string>();
        if (declareType.Namespace != string.Empty) names.Add(declareType.Namespace.Replace(member_access_operator, separator));
        if (declareType.Name != string.Empty) names.Add(declareType.Name.Replace(member_access_operator, separator));
        if (field.Name != string.Empty) names.Add(field.Name.Replace(member_access_operator, separator));
        string friendlyFieldName = string.Join(separator, names);
        return friendlyFieldName;
    }

    void RegisterMethodFunction(LLVMModuleRef module, MethodReference method, Collection<Instruction>? instructions)
    {
        if (method.DeclaringType is ArrayType array && array.Rank > 1 &&
            method.Name is ".ctor" or "Get" or "Set")
            return;
        if (method.DeclaringType.Resolve()?.IsInterface == true)
            return;
        if (IsDelegateType(method.DeclaringType) && (method.Name is ".ctor" or "Invoke"))
            return;

        TypeReference declareType = method.DeclaringType;
        string friendlyName = GetFriendlyMethodName(method, declareType);
        if (moduleMethods.ContainsKey(friendlyName)) return;

        var funcType = CreateLLVMFunction(module, method);
        var funcValue = module.AddFunction(friendlyName, funcType);
        moduleMethods.Add(friendlyName, new(funcValue, funcType, method, instructions));
    }
}

module.Dump();

if (!module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var verificationError))
    throw new InvalidOperationException(verificationError);

machine.EmitToFile(module, $"{Path.GetFileNameWithoutExtension(fileName)}.obj", LLVMCodeGenFileType.LLVMObjectFile);

enum RuntimeMethod
{
    Newobj,
    Newarr
}

sealed class ExceptionRegion
{
    public required int Start;
    public required int End;
    public required List<ExceptionHandler> Handlers;
    public required LLVMValueRef Frame;
    public required LLVMValueRef Buffer;
}
