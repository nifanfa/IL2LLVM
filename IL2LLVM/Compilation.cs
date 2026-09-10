sealed class Compilation : TranslationComponent
{
    readonly Arrays arrays;
    readonly Branches branches;
    readonly Calls calls;
    readonly Constants constants;
    readonly Exceptions exceptions;
    readonly Fields fields;
    readonly Memory memory;
    readonly Numeric numeric;
    readonly Prefixes prefixes;
    readonly Returns returns;
    readonly Stack stack;
    readonly Strings strings;
    readonly Types types;
    readonly Variables variables;

    public Compilation(Translator translator) : base(translator)
    {
        arrays = new(translator);
        branches = new(translator);
        calls = new(translator);
        constants = new(translator);
        exceptions = new(translator);
        fields = new(translator);
        memory = new(translator);
        numeric = new(translator);
        prefixes = new(translator);
        returns = new(translator);
        stack = new(translator);
        strings = new(translator);
        types = new(translator);
        variables = new(translator);
    }

    internal void TranslateModule(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Expected an input file, output file, and target triple.");

        string fileName = Path.GetFullPath(args[0]);
        string outputFileName = Path.GetFullPath(args[1]);
        var (targetTriple, codeModel) = Target.ParseTargetSpecification(args[2]);

        context = LLVMContextRef.Create();
        module = context.CreateModuleWithName(Path.GetFileNameWithoutExtension(fileName));

        Translator.module.Target = targetTriple;

        machine = Target.CreateTargetMachine(targetTriple, codeModel);

        int1Type = context.Int1Type;
        int8Type = context.Int8Type;
        int16Type = context.Int16Type;
        int32Type = context.Int32Type;
        int64Type = context.Int64Type;
        floatType = context.FloatType;
        doubleType = context.DoubleType;
        voidType = context.VoidType;
        var targetData = machine.CreateTargetDataLayout();
        pointerSize = (int)targetData.ABISizeOfType(LLVMTypeRef.CreatePointer(int8Type, 0));
        sizeType = context.GetIntPtrType(targetData);

        moduleMethods = new();
        staticFields = new();
        staticFieldTypes = new(StringComparer.Ordinal);
        cctorGuards = new(StringComparer.Ordinal);
        localTypes = new(StringComparer.Ordinal);
        runtimeTypeIds = new(StringComparer.Ordinal);
        runtimeTypes = new(StringComparer.Ordinal);
        runtimeTypeObjects = new(StringComparer.Ordinal);
        staticStrings = new(StringComparer.Ordinal);
        staticStringArrays = new(StringComparer.Ordinal);
        staticUInt64Arrays = new(StringComparer.Ordinal);
        gcDescriptors = new(StringComparer.Ordinal);
        runtimeFieldData = new(StringComparer.Ordinal);
        missingVirtualFunctionPointers = new(StringComparer.Ordinal);
        delegateThunks = new(StringComparer.Ordinal);
        nextRuntimeTypeId = 1;
        nextVirtualDispatchId = 0;
        LLVMTypeRef exceptionPushType = default;
        LLVMValueRef exceptionPushFunction = default;
        LLVMTypeRef exceptionPopType = default;
        LLVMValueRef exceptionPopFunction = default;
        LLVMTypeRef exceptionBufferType = default;
        LLVMValueRef exceptionBufferFunction = default;
        LLVMTypeRef exceptionCurrentType = default;
        LLVMValueRef exceptionCurrentFunction = default;
        LLVMTypeRef setjmpType = default;
        LLVMValueRef setjmpFunction = default;
        LLVMTypeRef exceptionThrowType = default;
        LLVMValueRef exceptionThrowFunction = default;
        gcAllocateType = default;
        gcAllocateFunction = default;
        LLVMTypeRef gcPushType = default;
        LLVMValueRef gcPushFunction = default;
        LLVMTypeRef gcPopType = default;
        LLVMValueRef gcPopFunction = default;

        {
            var assembly = AssemblyDefinition.ReadAssembly(fileName);
            entryPoint = assembly.EntryPoint;
            localMethods = GetAllTypes(assembly.MainModule.Types)
                .SelectMany(t => t.Methods)
                .ToDictionary(m => m.FullName, StringComparer.Ordinal);
            localTypes = GetAllTypes(assembly.MainModule.Types)
                .ToDictionary(t => t.FullName, StringComparer.Ordinal);
            coreLib = new CoreLibMetadata(localTypes);
            var exceptionRuntimeType = coreLib.ExceptionRuntime;
            var objectPointerType = new PointerType(coreLib.Object);
            var objectReferenceSlotPointerType = new PointerType(objectPointerType);
            var exceptionFramePointerType = new PointerType(coreLib.ExceptionFrame);
            var jumpBufferPointerType = new PointerType(coreLib.JumpBuffer);
            var stackPointerPointerType = new PointerType(coreLib.StackPointer);
            var exceptionPushMethod = GetRequiredMethod(exceptionRuntimeType, "Push", false,
                coreLib.Void, exceptionFramePointerType, jumpBufferPointerType);
            var exceptionPopMethod = GetRequiredMethod(exceptionRuntimeType, "Pop", false,
                coreLib.Void, exceptionFramePointerType);
            var exceptionBufferMethod = GetRequiredMethod(exceptionRuntimeType, "GetBuffer", false,
                jumpBufferPointerType, exceptionFramePointerType);
            var exceptionTopMethod = GetRequiredMethod(exceptionRuntimeType, "GetTop", false,
                exceptionFramePointerType);
            var exceptionCurrentMethod = GetRequiredMethod(exceptionRuntimeType, "GetCurrent", false,
                coreLib.Exception);
            var setjmpMethod = GetRequiredMethod(exceptionRuntimeType, "SetJump", false,
                coreLib.Int32, jumpBufferPointerType, stackPointerPointerType);
            var longjmpMethod = GetRequiredMethod(exceptionRuntimeType, "LongJump", false,
                coreLib.Void, jumpBufferPointerType, coreLib.Int32);
            var exceptionAbortMethod = GetRequiredMethod(exceptionRuntimeType, "Abort", false,
                coreLib.Void);
            var exceptionThrowMethod = GetRequiredMethod(exceptionRuntimeType, "Throw", false,
                coreLib.Void, coreLib.Exception);
            RegisterMethodFunction(module, exceptionPushMethod, exceptionPushMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionPopMethod, exceptionPopMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionBufferMethod, exceptionBufferMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionTopMethod, exceptionTopMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionCurrentMethod, exceptionCurrentMethod.Body.Instructions);
            RegisterMethodFunction(module, setjmpMethod, null);
            RegisterMethodFunction(module, longjmpMethod, null);
            RegisterMethodFunction(module, exceptionAbortMethod, null);
            var exceptionPointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
            var stackSaveType = LLVMTypeRef.CreateFunction(exceptionPointerType, []);
            var stackSaveFunction = module.AddFunction("llvm.stacksave.p0", stackSaveType);
            RegisterMethodFunction(module, exceptionThrowMethod, exceptionThrowMethod.Body.Instructions);
            var gcHeapType = coreLib.GCHeap;
            var gcFramePointerType = new PointerType(coreLib.GCFrame);
            var gcRootPointerType = new PointerType(coreLib.GCRoot);
            var gcDescPointerType = new PointerType(coreLib.GCDesc);
            var gcAllocateMethod = GetRequiredMethod(gcHeapType, "Allocate", false, objectPointerType,
                coreLib.UIntPtr);
            var gcPushMethod = GetRequiredMethod(gcHeapType, "Push", false, coreLib.Void,
                gcFramePointerType, gcRootPointerType, coreLib.Int32);
            var gcPopMethod = GetRequiredMethod(gcHeapType, "Pop", false, coreLib.Void, gcFramePointerType);
            RegisterMethodFunction(module, gcAllocateMethod, gcAllocateMethod.Body.Instructions);
            RegisterMethodFunction(module, gcPushMethod, gcPushMethod.Body.Instructions);
            RegisterMethodFunction(module, gcPopMethod, gcPopMethod.Body.Instructions);
            var registeredGCAllocate = GetRegisteredMethod(gcAllocateMethod)!;
            gcAllocateFunction = registeredGCAllocate.Item1;
            gcAllocateType = registeredGCAllocate.Item2;
            var registeredGCPush = GetRegisteredMethod(gcPushMethod)!;
            gcPushFunction = registeredGCPush.Item1;
            gcPushType = registeredGCPush.Item2;
            var registeredGCPop = GetRegisteredMethod(gcPopMethod)!;
            gcPopFunction = registeredGCPop.Item1;
            gcPopType = registeredGCPop.Item2;
            var registeredExceptionPush = GetRegisteredMethod(exceptionPushMethod)!;
            exceptionPushFunction = registeredExceptionPush.Item1;
            exceptionPushType = registeredExceptionPush.Item2;
            var registeredExceptionPop = GetRegisteredMethod(exceptionPopMethod)!;
            exceptionPopFunction = registeredExceptionPop.Item1;
            exceptionPopType = registeredExceptionPop.Item2;
            var registeredExceptionBuffer = GetRegisteredMethod(exceptionBufferMethod)!;
            exceptionBufferFunction = registeredExceptionBuffer.Item1;
            exceptionBufferType = registeredExceptionBuffer.Item2;
            var registeredExceptionCurrent = GetRegisteredMethod(exceptionCurrentMethod)!;
            exceptionCurrentFunction = registeredExceptionCurrent.Item1;
            exceptionCurrentType = registeredExceptionCurrent.Item2;
            var registeredSetjmp = GetRegisteredMethod(setjmpMethod)!;
            setjmpFunction = registeredSetjmp.Item1;
            setjmpType = registeredSetjmp.Item2;
            ReadOnlySpan<byte> returnsTwiceName = "returns_twice"u8;
            unsafe
            {
                fixed (byte* name = returnsTwiceName)
                {
                    var kind = LLVM.GetEnumAttributeKindForName((sbyte*)name, (nuint)returnsTwiceName.Length);
                    var attribute = context.CreateEnumAttribute(kind, 0);
                    setjmpFunction.AddAttributeAtIndex(LLVMAttributeIndex.LLVMAttributeFunctionIndex, attribute);
                }
            }
            var registeredExceptionThrow = GetRegisteredMethod(exceptionThrowMethod)!;
            exceptionThrowFunction = registeredExceptionThrow.Item1;
            exceptionThrowType = registeredExceptionThrow.Item2;
            arrayEnumeratorTypes = localTypes.Values.Where(IsArrayEnumeratorDefinition).ToList();
            stringConstructor = GetRequiredConstructor(coreLib.String, new ArrayType(coreLib.Char));
            var typeGetTypeFromHandleMethod = GetRequiredMethod(coreLib.Type, "GetTypeFromHandle", false,
                coreLib.Type, coreLib.RuntimeTypeHandle);
            foreach (TypeDefinition type in GetAllTypes(assembly.MainModule.Types))
            {
                var fields = type.Fields;
                if (fields.Any())
                {
                    foreach (var field in fields)
                    {
                        if (field.IsStatic)
                            GetStaticField(field);
                    }
                }

                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody) continue;
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
                                MethodReference targetMethod = SpecializeMethodReference((MethodReference)instr.Operand, method);
                                MethodReference callTarget = ResolveCallTarget(targetMethod);
                                var localTarget = FindLocalMethod(targetMethod, localMethods);
                                var instructions = localTarget?.HasBody == true ? localTarget.Body.Instructions : new();
                                RegisterMethodFunction(module, targetMethod, instructions);
                                if (!ReferenceEquals(callTarget, targetMethod))
                                {
                                    var targetDefinition = FindLocalMethod(callTarget, localMethods);
                                    var targetInstructions = targetDefinition?.HasBody == true
                                        ? targetDefinition.Body.Instructions
                                        : new();
                                    RegisterMethodFunction(module, callTarget, targetInstructions);
                                }
                                if (instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis)
                                {
                                    foreach (var candidateType in localTypes.Values.Where(candidate => !candidate.IsInterface))
                                    {
                                        var implementation = FindMethodImplementation(candidateType, targetMethod);
                                        if (implementation is not null)
                                            RegisterMethodFunction(module, implementation, FindLocalMethod(implementation, localMethods)?.Body?.Instructions);
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            Queue<MethodReference> pendingReferences = new(moduleMethods.Values
                .SelectMany(method => (method.Item4 ?? [])
                    .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
                    .Select(instruction => SpecializeMethodReference((MethodReference)instruction.Operand, method.Item3))));
            HashSet<string> processedReferences = new(StringComparer.Ordinal);
            while (pendingReferences.Count != 0)
            {
                var reference = pendingReferences.Dequeue();
                if (!processedReferences.Add(GetFriendlyMethodName(reference)))
                    continue;
                var localTarget = FindLocalMethod(reference, localMethods);
                if (localTarget is null)
                    continue;
                var instructions = localTarget.HasBody ? localTarget.Body.Instructions : new();
                var before = moduleMethods.Count;
                RegisterMethodFunction(module, reference, instructions);
                var callTarget = ResolveCallTarget(reference);
                if (!ReferenceEquals(callTarget, reference))
                    RegisterMethodFunction(module, callTarget, FindLocalMethod(callTarget, localMethods)?.HasBody == true
                        ? FindLocalMethod(callTarget, localMethods)!.Body.Instructions
                        : new());
                if (moduleMethods.Count != before || localTarget.HasBody)
                {
                    foreach (var instruction in instructions.Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn))
                        pendingReferences.Enqueue(SpecializeMethodReference((MethodReference)instruction.Operand, reference));
                }
                if (reference.Resolve()?.IsVirtual == true || reference.Resolve()?.IsAbstract == true)
                {
                    foreach (var candidateType in localTypes.Values.Where(candidate => !candidate.IsInterface))
                    {
                        var implementation = FindMethodImplementation(candidateType, reference);
                        if (implementation is null)
                            continue;
                        var implementationDefinition = FindLocalMethod(implementation, localMethods);
                        RegisterMethodFunction(module, implementation, implementationDefinition?.Body?.Instructions);
                        foreach (var instruction in implementationDefinition?.Body?.Instructions ?? [])
                            if (instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
                                pendingReferences.Enqueue(SpecializeMethodReference((MethodReference)instruction.Operand, implementation));
                    }
                }
            }

            foreach (var arrayEnumeratorType in arrayEnumeratorTypes)
                foreach (var method in arrayEnumeratorType.Methods.Where(method => method.HasBody))
                    RegisterMethodFunction(module, method, method.Body.Instructions);

            bool RegisterClosedVirtualMethods()
            {
                bool added = false;
                var targets = moduleMethods.Values.SelectMany(method => (method.Item4 ?? [])
                        .Where(instruction => instruction.OpCode.Code == Code.Callvirt)
                        .Select(instruction => SpecializeMethodReference((MethodReference)instruction.Operand, method.Item3)))
                    .Where(target => target.HasThis && target.Resolve() is { IsVirtual: true } or { IsAbstract: true })
                    .DistinctBy(target => GetFriendlyMethodName(target))
                    .ToList();
                foreach (var declaringType in moduleMethods.Values
                             .Select(method => method.Item3.DeclaringType)
                             .OfType<GenericInstanceType>()
                             .Where(type => !ContainsGenericParameter(type))
                             .DistinctBy(GetRuntimeTypeKey)
                             .ToList())
                {
                    foreach (var target in targets)
                    {
                        var implementation = FindMethodImplementation(declaringType, target);
                        if (implementation is null || GetRegisteredMethod(implementation) is not null)
                            continue;
                        var definition = FindLocalMethod(implementation, localMethods);
                        if (definition?.HasBody != true)
                            continue;
                        RegisterMethodFunction(module, implementation, definition.Body.Instructions);
                        added = true;
                    }
                }
                return added;
            }

            while (RegisterClosedVirtualMethods())
            {
            }

            foreach (var type in localTypes.Values)
                GetRuntimeTypeId(type);
            foreach (var method in moduleMethods.Values)
            {
                foreach (var instruction in method.Item4 ?? [])
                {
                    switch (instruction.OpCode.Code)
                    {
                        case Code.Newarr:
                            GetRuntimeTypeId(new ArrayType(SubstituteGenericParameter((TypeReference)instruction.Operand, method.Item3)));
                            break;
                        case Code.Newobj:
                            GetRuntimeTypeId(SpecializeMethodReference((MethodReference)instruction.Operand, method.Item3).DeclaringType);
                            break;
                        case Code.Box:
                        case Code.Ldtoken when instruction.Operand is TypeReference:
                            GetRuntimeTypeId(SubstituteGenericParameter((TypeReference)instruction.Operand, method.Item3));
                            break;
                    }
                }
            }
            GetRuntimeTypeId(new ArrayType(coreLib.Char));
            while (runtimeTypes.Values.Any(type => type.Resolve()?.IsInterface != true && !IsVoidType(type) &&
                !runtimeTypeObjects.ContainsKey(GetRuntimeTypeKey(type))))
                foreach (var type in runtimeTypes.Values.Where(type => type.Resolve()?.IsInterface != true && !IsVoidType(type) &&
                    !runtimeTypeObjects.ContainsKey(GetRuntimeTypeKey(type))).ToArray())
                    GetRuntimeTypeObject(type);

            LLVMBuilderRef entryBuilder = default;
            HashSet<LLVMValueRef> translatedMethods = new();
            while (true)
            {
                var method = moduleMethods.FirstOrDefault(candidate => candidate.Value.Item4?.Any() == true &&
                    !translatedMethods.Contains(candidate.Value.Item1));
                if (method.Value is null)
                    break;
                translatedMethods.Add(method.Value.Item1);
                if (method.Value.Item4?.Any() == true)
                {
                    var allocaBlock = context.AppendBasicBlock(method.Value.Item1, "alloca");
                    var entry = context.AppendBasicBlock(method.Value.Item1, GetLabelName(method.Value.Item4.First()));
                    var builder = context.CreateBuilder();
                    entryBuilder = context.CreateBuilder();
                    entryBuilder.PositionAtEnd(allocaBlock);
                    builder.PositionAtEnd(entry);
                    var cctorGuard = method.Value.Item3.Resolve() is not { IsConstructor: true } && !method.Value.Item3.HasThis
                        ? GetCctorGuard(method.Value.Item3.DeclaringType)
                        : null;
                    {
                        System.Collections.Generic.Stack<LLVMValueRef> stack = new();
                        Dictionary<LLVMBasicBlockRef, List<(LLVMBasicBlockRef Source, List<LLVMValueRef> Values)>> incomingStacks = new();
                        Dictionary<LLVMBasicBlockRef, List<List<TypeReference?>>> incomingStackTypes = new();
                        Dictionary<LLVMBasicBlockRef, Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>>> spillSlots = new();
                        Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>> local = new();
                        Dictionary<LLVMValueRef, TypeReference> trackedTypes = new();
                        Dictionary<LLVMValueRef, MethodReference> trackedFunctionTargets = new();
                        Dictionary<int, TypeReference> localRuntimeTypes = new();
                        HashSet<LLVMBasicBlockRef> terminatedBlocks = new();
                        Dictionary<LLVMBasicBlockRef, LLVMBasicBlockRef> finallyContinuations = new();
                        List<ExceptionRegion> exceptionRegions = new();
                        Dictionary<ExceptionHandler, (LLVMValueRef Slot, Dictionary<int, LLVMBasicBlockRef> Targets)> finallyStates = new();
                        Dictionary<int, (LLVMBasicBlockRef Handler, LLVMBasicBlockRef Next)> filterStates = new();
                        Dictionary<ExceptionHandler, LLVMValueRef> caughtExceptions = new();
                        SortedDictionary<int, LLVMBasicBlockRef> label = new();
                        int nextFinallyContinuation = 1;
                        uint unalignedAlignment = 0;
                        var methodDefinition = FindLocalMethod(method.Value.Item3, localMethods) ?? method.Value.Item3.Resolve();
                        if (methodDefinition?.Body.ExceptionHandlers.Any() == true)
                        {
                            ReadOnlySpan<byte> framePointerName = "frame-pointer"u8;
                            ReadOnlySpan<byte> framePointerValue = "all"u8;
                            unsafe
                            {
                                fixed (byte* name = framePointerName)
                                fixed (byte* value = framePointerValue)
                                {
                                    var attribute = new LLVMAttributeRef((IntPtr)LLVM.CreateStringAttribute(
                                        (LLVMOpaqueContext*)context.Handle, (sbyte*)name, (uint)framePointerName.Length,
                                        (sbyte*)value, (uint)framePointerValue.Length));
                                    method.Value.Item1.AddAttributeAtIndex(LLVMAttributeIndex.LLVMAttributeFunctionIndex, attribute);
                                }
                            }
                        }

                        void TrackType(LLVMValueRef value, TypeReference type)
                        {
                            if (value != default)
                                trackedTypes[value] = type;
                        }

                        LLVMValueRef GetDelegateFunctionPointer(TypeReference delegateType,
                            MethodReference targetMethod, LLVMValueRef targetFunction)
                        {
                            if (targetMethod.HasThis)
                                return targetFunction;

                            var delegateDefinition = delegateType.Resolve() ??
                                throw new NotSupportedException($"Delegate type is not defined: {delegateType.FullName}");
                            var invokeDefinition = delegateDefinition.Methods.Single(candidate => candidate.Name == "Invoke");
                            var invokeMethod = BindMethodToDeclaringType(invokeDefinition, delegateType, targetMethod);
                            var key = $"{GetFriendlyMethodName(targetMethod)}|{GetRuntimeTypeKey(delegateType)}";
                            if (delegateThunks.TryGetValue(key, out var existing))
                                return existing;

                            var invokeReturnType = SubstituteGenericParameter(invokeMethod.ReturnType, invokeMethod);
                            if (UsesValueReturnBuffer(targetMethod) || IsValueType(invokeReturnType))
                                throw new NotSupportedException($"Value-type delegate returns are not implemented: {invokeMethod.FullName}");
                            if (targetMethod.Parameters.Count != invokeMethod.Parameters.Count)
                                throw new NotSupportedException($"Closed static delegates are not implemented: {targetMethod.FullName}");

                            var parameterTypes = new List<LLVMTypeRef> { LLVMTypeRef.CreatePointer(int8Type, 0) };
                            parameterTypes.AddRange(invokeMethod.Parameters.Select(parameter =>
                                GetLLVMTypeRef(SubstituteGenericParameter(parameter.ParameterType, invokeMethod))));
                            var thunkType = LLVMTypeRef.CreateFunction(GetLLVMTypeRef(invokeReturnType), parameterTypes.ToArray());
                            var thunk = module.AddFunction($"__delegate_thunk_{GetStableSymbolSuffix(key)}", thunkType);
                            thunk.Linkage = LLVMLinkage.LLVMInternalLinkage;
                            delegateThunks.Add(key, thunk);

                            var thunkBuilder = context.CreateBuilder();
                            thunkBuilder.PositionAtEnd(context.AppendBasicBlock(thunk, "entry"));
                            var target = GetRegisteredMethod(targetMethod) ??
                                throw new NotSupportedException($"Method is not defined in the input module: {targetMethod.FullName}");
                            var arguments = invokeMethod.Parameters.Select((parameter, index) =>
                                ConvertValue(thunkBuilder, thunk.GetParam((uint)(index + 1)),
                                    GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[index].ParameterType, targetMethod))))
                                .ToArray();
                            var result = thunkBuilder.BuildCall2(target.Item2, targetFunction, arguments);
                            if (IsVoidType(invokeReturnType))
                                thunkBuilder.BuildRetVoid();
                            else
                                thunkBuilder.BuildRet(ConvertValue(thunkBuilder, result, GetLLVMTypeRef(invokeReturnType)));
                            thunkBuilder.Dispose();
                            return thunk;
                        }

                        LLVMValueRef BuildRuntimeTypeMatch(LLVMValueRef value, TypeReference targetType)
                        {
                            var typeId = GetObjectRuntimeTypeId(builder, value);
                            var matches = new List<LLVMValueRef>();
                            var seen = new HashSet<int>();
                            foreach (var candidate in runtimeTypes.Values.Where(candidate => candidate.Resolve()?.IsInterface != true && !IsVoidType(candidate)).ToArray())
                            {
                                if (!IsRuntimeTypeCompatible(candidate, targetType))
                                    continue;
                                var candidateId = GetRuntimeTypeId(candidate);
                                if (!seen.Add(candidateId))
                                    continue;
                                matches.Add(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, typeId,
                                    LLVMValueRef.CreateConstInt(sizeType, (ulong)candidateId, false)));
                            }
                            if (matches.Count == 0)
                                return LLVMValueRef.CreateConstInt(int1Type, 0, false);
                            var result = matches[0];
                            for (int i = 1; i < matches.Count; i++)
                                result = builder.BuildOr(result, matches[i]);
                            return result;
                        }

                        TypeReference? GetIndirectType(LLVMValueRef address)
                        {
                            if (!trackedTypes.TryGetValue(address, out var addressType))
                                return null;
                            return addressType switch
                            {
                                ByReferenceType byReference => byReference.ElementType,
                                PointerType pointer => pointer.ElementType,
                                _ => null
                            };
                        }

                        LLVMValueRef BuildVirtualDispatch(MethodReference targetMethod, LLVMValueRef[] targetArgs,
                            LLVMTypeRef targetFunctionType, LLVMValueRef targetFunction, List<(TypeReference RuntimeType, MethodReference Implementation)> implementations,
                            bool allowArraySpecial = true, LLVMValueRef returnBuffer = default)
                        {
                            LLVMValueRef[] GetImplementationArgs(TypeReference runtimeType, MethodReference implementation)
                            {
                                var implementationArgs = targetArgs;
                                var implementationType = implementation.DeclaringType.Resolve();
                                if (runtimeType.IsValueType && targetArgs.Length != 0 && implementationType?.IsValueType == true &&
                                    !coreLib.IsEnum(implementationType) && !coreLib.IsValueType(implementationType))
                                {
                                    implementationArgs = (LLVMValueRef[])targetArgs.Clone();
                                    implementationArgs[0] = GetBoxedValueAddress(builder, targetArgs[0], runtimeType);
                                }
                                return returnBuffer == default
                                    ? implementationArgs
                                    : [returnBuffer, .. implementationArgs];
                            }

                            if (allowArraySpecial && TryGetArrayEnumerator(targetMethod, out var enumerableElementType,
                                    out var arrayEnumeratorDefinition, out var constructor))
                            {
                                var constructorMethod = GetRegisteredMethod(constructor) ??
                                    throw new NotSupportedException($"Method is not defined in the input module: {constructor.FullName}");
                                var arrayReceiverValue = targetArgs[0];
                                var arrayTypeId = GetObjectRuntimeTypeId(builder, arrayReceiverValue);
                                var arrayType = new ArrayType(enumerableElementType);
                                var arrayId = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetRuntimeTypeId(arrayType), false);
                                var arrayBlock = context.AppendBasicBlock(method.Value.Item1, $"array.enum.{nextVirtualDispatchId++}");
                                var fallbackBlock = context.AppendBasicBlock(method.Value.Item1, $"array.enum.next.{nextVirtualDispatchId++}");
                                var arrayContinuation = context.AppendBasicBlock(method.Value.Item1, $"array.enum.cont.{nextVirtualDispatchId++}");
                                var arraySourceBlock = builder.InsertBlock;
                                builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, arrayTypeId, arrayId), arrayBlock, fallbackBlock);
                                terminatedBlocks.Add(arraySourceBlock);
                                builder.PositionAtEnd(arrayBlock);
                                var enumeratorType = new GenericInstanceType(arrayEnumeratorDefinition);
                                enumeratorType.GenericArguments.Add(enumerableElementType);
                                var enumerator = BuildAllocation(builder, GetObjectSize(enumeratorType));
                                InitializeRuntimeType(builder, enumerator, enumeratorType);
                                StoreTemporaryRoot(1, enumerator, enumeratorType);
                                builder.BuildCall2(constructorMethod.Item2, constructorMethod.Item1, [enumerator, arrayReceiverValue]);
                                builder.BuildBr(arrayContinuation);
                                builder.PositionAtEnd(fallbackBlock);
                                var arrayFallback = BuildVirtualDispatch(targetMethod, targetArgs, targetFunctionType, targetFunction, implementations, false, returnBuffer);
                                var arrayFallbackSource = builder.InsertBlock;
                                builder.BuildBr(arrayContinuation);
                                builder.PositionAtEnd(arrayContinuation);
                                var result = builder.BuildPhi(LLVMTypeRef.CreatePointer(int8Type, 0), "array.enum.result");
                                result.AddIncoming([enumerator, arrayFallback], [arrayBlock, arrayFallbackSource], 2);
                                return result;
                            }

                            var registeredImplementations = implementations
                                .Select(candidate => (candidate.RuntimeType, Method: GetRegisteredMethod(candidate.Implementation)))
                                .Where(candidate => candidate.Method is not null)
                                .ToList();
                            var distinctImplementations = registeredImplementations
                                .Select(candidate => candidate.Method!.Item1)
                                .Distinct()
                                .ToList();
                            if (implementations.Count == 0 || distinctImplementations.Count <= 1)
                            {
                                if (distinctImplementations.Count == 1)
                                {
                                    var implementation = registeredImplementations[0];
                                    var result = builder.BuildCall2(implementation.Method!.Item2, implementation.Method.Item1,
                                        GetImplementationArgs(implementation.RuntimeType, implementation.Method.Item3));
                                    return returnBuffer == default ? result : returnBuffer;
                                }
                                if (targetFunction != default)
                                {
                                    var functionArgs = returnBuffer == default ? targetArgs : [returnBuffer, .. targetArgs];
                                    var result = builder.BuildCall2(targetFunctionType, targetFunction, functionArgs);
                                    return returnBuffer == default ? result : returnBuffer;
                                }
                                var abort = GetRegisteredMethod(exceptionAbortMethod) ??
                                    throw new NotSupportedException($"Method is not defined in the input module: {exceptionAbortMethod.FullName}");
                                builder.BuildCall2(abort.Item2, abort.Item1, []);
                                builder.BuildUnreachable();
                                terminatedBlocks.Add(builder.InsertBlock);
                                return IsVoidType(targetMethod.ReturnType)
                                    ? default
                                    : returnBuffer == default
                                        ? LLVMValueRef.CreateConstNull(GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)))
                                        : returnBuffer;
                            }

                            var receiver = targetArgs[0];
                            var typeId = GetObjectRuntimeTypeId(builder, receiver);
                            var continuation = context.AppendBasicBlock(method.Value.Item1, $"virt.cont.{nextVirtualDispatchId++}");
                            var incomingValues = new List<LLVMValueRef>();
                            var incomingBlocks = new List<LLVMBasicBlockRef>();
                            var sourceBlock = builder.InsertBlock;
                            var sourceTerminated = false;

                            for (int i = 0; i < implementations.Count; i++)
                            {
                                var candidate = implementations[i];
                                var implementationMethod = GetRegisteredMethod(candidate.Implementation);
                                if (implementationMethod is null)
                                    continue;
                                var callBlock = context.AppendBasicBlock(method.Value.Item1, $"virt.call.{nextVirtualDispatchId++}");
                                var nextBlock = context.AppendBasicBlock(method.Value.Item1, $"virt.next.{nextVirtualDispatchId++}");
                                var candidateId = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetRuntimeTypeId(candidate.RuntimeType), false);
                                var matches = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, typeId, candidateId);
                                builder.BuildCondBr(matches, callBlock, nextBlock);
                                if (!sourceTerminated)
                                {
                                    terminatedBlocks.Add(sourceBlock);
                                    sourceTerminated = true;
                                }

                                builder.PositionAtEnd(callBlock);
                                var implementationArgs = GetImplementationArgs(candidate.RuntimeType, implementationMethod.Item3);
                                var result = builder.BuildCall2(implementationMethod.Item2, implementationMethod.Item1, implementationArgs);
                                if (!IsVoidType(targetMethod.ReturnType) && returnBuffer == default)
                                {
                                    incomingValues.Add(ConvertValue(builder, result, GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod))));
                                    incomingBlocks.Add(callBlock);
                                }
                                builder.BuildBr(continuation);
                                builder.PositionAtEnd(nextBlock);
                            }

                            if (!IsVoidType(targetMethod.ReturnType) && targetFunction != default && returnBuffer == default)
                            {
                                var fallback = ConvertValue(builder, builder.BuildCall2(targetFunctionType, targetFunction, targetArgs),
                                    GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)));
                                incomingValues.Add(fallback);
                                incomingBlocks.Add(builder.InsertBlock);
                                builder.BuildBr(continuation);
                            }
                            else if (targetFunction != default)
                            {
                                var functionArgs = returnBuffer == default ? targetArgs : [returnBuffer, .. targetArgs];
                                builder.BuildCall2(targetFunctionType, targetFunction, functionArgs);
                                builder.BuildBr(continuation);
                            }
                            else
                            {
                                var abort = GetRegisteredMethod(exceptionAbortMethod) ??
                                    throw new NotSupportedException($"Method is not defined in the input module: {exceptionAbortMethod.FullName}");
                                builder.BuildCall2(abort.Item2, abort.Item1, []);
                                builder.BuildUnreachable();
                                terminatedBlocks.Add(builder.InsertBlock);
                            }
                            builder.PositionAtEnd(continuation);
                            if (IsVoidType(targetMethod.ReturnType))
                                return default;
                            if (returnBuffer != default)
                                return returnBuffer;
                            var phi = builder.BuildPhi(GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)), "virt.result");
                            phi.AddIncoming(incomingValues.ToArray(), incomingBlocks.ToArray(), (uint)incomingValues.Count);
                            return phi;
                        }

                        LLVMValueRef BuildVirtualFunctionPointer(MethodReference targetMethod, LLVMValueRef receiver)
                        {
                            var implementations = GetVirtualImplementations(targetMethod, targetMethod.DeclaringType)
                                .Select(candidate => (candidate.RuntimeType, Method: GetRegisteredMethod(candidate.Implementation)))
                                .Where(candidate => candidate.Method is not null)
                                .ToList();
                            if (implementations.Count == 0)
                            {
                                var key = GetFriendlyMethodName(targetMethod);
                                if (!missingVirtualFunctionPointers.TryGetValue(key, out var missingFunction))
                                {
                                    var abortMethod = GetRegisteredMethod(exceptionAbortMethod) ??
                                        throw new NotSupportedException($"Method is not defined in the input module: {exceptionAbortMethod.FullName}");
                                    missingFunction = module.AddFunction($"__missing_virtual_{GetStableSymbolSuffix(key)}",
                                        CreateLLVMFunction(module, targetMethod));
                                    missingFunction.Linkage = LLVMLinkage.LLVMInternalLinkage;
                                    var block = context.AppendBasicBlock(missingFunction, "entry");
                                    var missingBuilder = context.CreateBuilder();
                                    missingBuilder.PositionAtEnd(block);
                                    missingBuilder.BuildCall2(abortMethod.Item2, abortMethod.Item1, []);
                                    missingBuilder.BuildUnreachable();
                                    missingVirtualFunctionPointers.Add(key, missingFunction);
                                }
                                return missingFunction;
                            }
                            if (implementations.Count == 1)
                                return implementations[0].Method!.Item1;

                            var runtimeTypeId = GetObjectRuntimeTypeId(builder, receiver);
                            var continuation = context.AppendBasicBlock(method.Value.Item1, $"virt.ftn.cont.{nextVirtualDispatchId++}");
                            var incomingValues = new List<LLVMValueRef>();
                            var incomingBlocks = new List<LLVMBasicBlockRef>();
                            foreach (var implementation in implementations)
                            {
                                var callBlock = context.AppendBasicBlock(method.Value.Item1, $"virt.ftn.value.{nextVirtualDispatchId++}");
                                var nextBlock = context.AppendBasicBlock(method.Value.Item1, $"virt.ftn.next.{nextVirtualDispatchId++}");
                                var typeId = LLVMValueRef.CreateConstInt(sizeType,
                                    (ulong)GetRuntimeTypeId(implementation.RuntimeType), false);
                                builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, runtimeTypeId, typeId),
                                    callBlock, nextBlock);
                                terminatedBlocks.Add(builder.InsertBlock);

                                builder.PositionAtEnd(callBlock);
                                incomingValues.Add(implementation.Method!.Item1);
                                incomingBlocks.Add(callBlock);
                                builder.BuildBr(continuation);
                                terminatedBlocks.Add(callBlock);

                                builder.PositionAtEnd(nextBlock);
                            }
                            var abort = GetRegisteredMethod(exceptionAbortMethod) ??
                                throw new NotSupportedException($"Method is not defined in the input module: {exceptionAbortMethod.FullName}");
                            builder.BuildCall2(abort.Item2, abort.Item1, []);
                            builder.BuildUnreachable();
                            terminatedBlocks.Add(builder.InsertBlock);
                            builder.PositionAtEnd(continuation);
                            var function = builder.BuildPhi(LLVMTypeRef.CreatePointer(int8Type, 0), "virt.ftn");
                            function.AddIncoming(incomingValues.ToArray(), incomingBlocks.ToArray(), (uint)incomingValues.Count);
                            return function;
                        }

                        LLVMValueRef BuildEntryAlloca(LLVMTypeRef type, uint alignment = 0)
                        {
                            entryBuilder.PositionAtEnd(allocaBlock);
                            var value = entryBuilder.BuildAlloca(type);
                            if (alignment != 0)
                                value.Alignment = alignment;
                            return value;
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
                            if (!incomingStackTypes.TryGetValue(block, out var types))
                            {
                                types = new();
                                incomingStackTypes[block] = types;
                            }
                            types.Add(stackValues.Select(value => trackedTypes.TryGetValue(value, out var type) ? type : null).ToList());
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
                                {
                                    var value = builder.BuildLoad2(slot.Item2, slot.Item1);
                                    stack.Push(value);
                                    if (incomingStackTypes.TryGetValue(block, out var types))
                                    {
                                        var type = types.Select(values => i < values.Count ? values[i] : null)
                                            .FirstOrDefault(candidate => candidate is not null);
                                        if (type is not null)
                                            TrackType(value, type);
                                    }
                                }
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
                            if (!incomingStackTypes.TryGetValue(block, out var types))
                            {
                                types = new();
                                incomingStackTypes[block] = types;
                            }
                            types.Add([coreLib.Exception]);
                        }

                        LLVMValueRef BuildExceptionMatch(LLVMValueRef exception, TypeReference? targetType)
                        {
                            if (targetType is null)
                                return LLVMValueRef.CreateConstInt(int1Type, 1, false);
                            var matches = new List<LLVMValueRef>();
                            foreach (var candidate in runtimeTypes.Values.Where(candidate => candidate.Resolve() is { IsInterface: false, IsValueType: false }).ToArray())
                            {
                                if (!IsRuntimeTypeCompatible(candidate, targetType))
                                    continue;
                                matches.Add(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ,
                                    GetObjectRuntimeTypeId(builder, exception),
                                    LLVMValueRef.CreateConstInt(sizeType, (ulong)GetRuntimeTypeId(candidate), false)));
                            }
                            if (matches.Count == 0)
                                return LLVMValueRef.CreateConstInt(int1Type, 0, false);
                            var result = matches[0];
                            for (int i = 1; i < matches.Count; i++)
                                result = builder.BuildOr(result, matches[i]);
                            return result;
                        }

                        void EmitConditionalException(LLVMValueRef condition, TypeReference exceptionType)
                        {
                            var source = builder.InsertBlock;
                            var throwBlock = context.AppendBasicBlock(method.Value.Item1, $"throw.{nextVirtualDispatchId++}");
                            var continuation = context.AppendBasicBlock(method.Value.Item1, $"throw.cont.{nextVirtualDispatchId++}");
                            builder.BuildCondBr(condition, throwBlock, continuation);
                            terminatedBlocks.Add(source);

                            builder.PositionAtEnd(throwBlock);
                            SynchronizeEvaluationStackRoots();
                            var exception = BuildAllocation(builder, GetObjectSize(exceptionType));
                            InitializeRuntimeType(builder, exception, exceptionType);
                            builder.BuildCall2(exceptionThrowType, exceptionThrowFunction, [exception]);
                            builder.BuildUnreachable();
                            terminatedBlocks.Add(throwBlock);
                            builder.PositionAtEnd(continuation);
                        }

                        void CheckMultiArrayAccess(LLVMValueRef array, LLVMValueRef[] indices)
                        {
                            EmitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, array,
                                LLVMValueRef.CreateConstNull(array.TypeOf)), coreLib.NullReferenceException);
                            var lengths = builder.BuildLoad2(GetLLVMTypeRef(GetArrayLengthsField().FieldType),
                                GetFieldAddress(builder, array, GetArrayLengthsField()));
                            for (int index = 0; index < indices.Length; index++)
                            {
                                var dimensionLength = ConvertValue(builder, builder.BuildLoad2(int32Type,
                                    GetArrayElementAddress(builder, lengths,
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)index, false), int32Type)), sizeType, false);
                                var dimensionIndex = ConvertValue(builder, indices[index], sizeType);
                                EmitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE,
                                    dimensionIndex, dimensionLength), coreLib.IndexOutOfRangeException);
                            }
                        }

                        LLVMValueRef BuildCheckedIntegerArithmetic(Code code, LLVMValueRef left, LLVMValueRef right)
                        {
                            var operands = NormalizeBinaryOperands(builder, left, right);
                            var type = operands.Left.TypeOf;
                            if (type.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
                                throw new InvalidProgramException($"Checked arithmetic requires integers: {code}.");
                            var unsigned = code is Code.Add_Ovf_Un or Code.Sub_Ovf_Un or Code.Mul_Ovf_Un;
                            var wideType = context.GetIntType(type.IntWidth * 2);
                            var wideLeft = unsigned
                                ? builder.BuildZExt(operands.Left, wideType)
                                : builder.BuildSExt(operands.Left, wideType);
                            var wideRight = unsigned
                                ? builder.BuildZExt(operands.Right, wideType)
                                : builder.BuildSExt(operands.Right, wideType);
                            var wideResult = code switch
                            {
                                Code.Add_Ovf or Code.Add_Ovf_Un => builder.BuildAdd(wideLeft, wideRight),
                                Code.Sub_Ovf or Code.Sub_Ovf_Un => builder.BuildSub(wideLeft, wideRight),
                                Code.Mul_Ovf or Code.Mul_Ovf_Un => builder.BuildMul(wideLeft, wideRight),
                                _ => throw new InvalidOperationException(code.ToString())
                            };
                            var result = builder.BuildTrunc(wideResult, type);
                            var restored = unsigned
                                ? builder.BuildZExt(result, wideType)
                                : builder.BuildSExt(result, wideType);
                            EmitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, wideResult, restored),
                                coreLib.OverflowException);
                            return result;
                        }

                        void RegisterFinallyContinuation(ExceptionHandler handler, LLVMBasicBlockRef target)
                        {
                            var state = finallyStates[handler];
                            int id = nextFinallyContinuation++;
                            state.Targets[id] = target;
                            builder.BuildStore(LLVMValueRef.CreateConstInt(int32Type, (uint)id, false), state.Slot);
                            finallyStates[handler] = state;
                        }

                        void EmitExceptionSetup(ExceptionRegion region)
                        {
                            var source = builder.InsertBlock;
                            var normal = context.AppendBasicBlock(method.Value.Item1, $"eh.normal.{nextVirtualDispatchId++}");
                            var dispatch = context.AppendBasicBlock(method.Value.Item1, $"eh.dispatch.{nextVirtualDispatchId++}");
                            builder.BuildCall2(exceptionPushType, exceptionPushFunction, [region.Frame, region.Buffer]);
                            var jumpResult = builder.BuildCall2(setjmpType, setjmpFunction,
                                [builder.BuildCall2(exceptionBufferType, exceptionBufferFunction, [region.Frame]),
                                    builder.BuildCall2(stackSaveType, stackSaveFunction, [])]);
                            builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, jumpResult,
                                LLVMValueRef.CreateConstInt(int32Type, 0, false)), normal, dispatch);
                            terminatedBlocks.Add(source);

                            builder.PositionAtEnd(dispatch);
                            builder.BuildCall2(exceptionPopType, exceptionPopFunction, [region.Frame]);
                            var exception = builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, []);
                            var chain = dispatch;
                            for (int index = 0; index < region.Handlers.Count; index++)
                            {
                                var handler = region.Handlers[index];
                                builder.PositionAtEnd(chain);
                                var next = context.AppendBasicBlock(method.Value.Item1, $"eh.next.{nextVirtualDispatchId++}");
                                if (handler.HandlerType == ExceptionHandlerType.Filter)
                                {
                                    var filterBlock = label[handler.FilterStart!.Offset];
                                    builder.BuildStore(exception, caughtExceptions[handler]);
                                    SaveException(filterBlock, exception);
                                    builder.BuildBr(filterBlock);
                                    terminatedBlocks.Add(chain);
                                    var endfilter = region.Handlers[index].FilterStart is null
                                        ? null
                                        : methodDefinition is not null
                                            ? methodDefinition.Body.Instructions.FirstOrDefault(instruction =>
                                                instruction.OpCode.Code == Code.Endfilter &&
                                                instruction.Offset >= handler.FilterStart.Offset && instruction.Offset < handler.HandlerStart.Offset)
                                            : null;
                                    if (endfilter is not null)
                                        filterStates[endfilter.Offset] = (label[handler.HandlerStart.Offset], next);
                                }
                                else if (handler.HandlerType == ExceptionHandlerType.Catch)
                                {
                                    var match = context.AppendBasicBlock(method.Value.Item1, $"eh.match.{nextVirtualDispatchId++}");
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
                                    var rethrow = context.AppendBasicBlock(method.Value.Item1, $"eh.rethrow.{nextVirtualDispatchId++}");
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

                        var usesValueReturnBuffer = UsesValueReturnBuffer(method.Value.Item3);
                        var valueReturnBuffer = usesValueReturnBuffer
                            ? method.Value.Item1.GetParam(0)
                            : default;

                        LLVMValueRef GetMethodParameter(int index)
                        {
                            return method.Value.Item1.GetParam((uint)(index + (usesValueReturnBuffer ? 1 : 0)));
                        }

                        for (int i = 0; i < GetMethodParameterCount(method.Value.Item3); i++)
                        {
                            var argument = GetMethodParameter(i);
                            local[-1 - i] = new(BuildEntryAlloca(argument.TypeOf), argument.TypeOf);
                            entryBuilder.PositionAtEnd(allocaBlock);
                            entryBuilder.BuildStore(argument, local[-1 - i].Item1);
                        }
                        label.Add(method.Value.Item4.First().Offset, entry);

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
                                            label.TryAdd(branchStart.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(branchStart)));
                                        }
                                        if (next is not null && !label.ContainsKey(next.Offset))
                                        {
                                            label.TryAdd(next.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(next))); // fallthrough
                                        }
                                        break;
                                    }
                                case Code.Switch:
                                    {
                                        foreach (var branchStart in (Instruction[])instr.Operand)
                                            if (!label.ContainsKey(branchStart.Offset))
                                                label.TryAdd(branchStart.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(branchStart)));
                                        if (instr.Next is not null && !label.ContainsKey(instr.Next.Offset))
                                            label.TryAdd(instr.Next.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(instr.Next)));
                                        break;
                                    }
                            }
                        }

                        if (methodDefinition?.HasBody == true)
                        {
                            foreach (var handler in methodDefinition.Body.ExceptionHandlers)
                            {
                                if (!label.ContainsKey(handler.HandlerStart.Offset))
                                    label.TryAdd(handler.HandlerStart.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(handler.HandlerStart)));
                                if (handler.FilterStart is not null && !label.ContainsKey(handler.FilterStart.Offset))
                                    label.TryAdd(handler.FilterStart.Offset, context.AppendBasicBlock(method.Value.Item1, GetLabelName(handler.FilterStart)));
                            }

                            foreach (var regionGroup in methodDefinition.Body.ExceptionHandlers
                                         .GroupBy(handler => (handler.TryStart.Offset, handler.TryEnd.Offset)))
                            {
                                var frame = BuildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                                    (uint)GetTypeSize(coreLib.ExceptionFrame)), (uint)pointerSize);
                                var jumpBufferType = coreLib.JumpBuffer;
                                var buffer = BuildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                                    (uint)GetTypeSize(jumpBufferType)), 16);
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
                                    finallyStates[handler] = (BuildEntryAlloca(int32Type), new());
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

                        var fixedRoots = new List<(LLVMValueRef Address, TypeReference? Descriptor)>();

                        void AddRoot(Tuple<LLVMValueRef, LLVMTypeRef> storage, TypeReference type, bool initialize)
                        {
                            if (IsByReferenceValue(type))
                            {
                                if (initialize)
                                    builder.BuildStore(LLVMValueRef.CreateConstNull(storage.Item2), storage.Item1);
                                fixedRoots.Add((storage.Item1, null));
                                return;
                            }
                            if (IsManagedReferenceType(type))
                            {
                                if (initialize)
                                    builder.BuildStore(LLVMValueRef.CreateConstNull(storage.Item2), storage.Item1);
                                fixedRoots.Add((storage.Item1, null));
                                return;
                            }
                            if (!IsValueType(type) || !GetGCReferenceOffsets(type).Any())
                                return;
                            var value = builder.BuildLoad2(storage.Item2, storage.Item1);
                            if (initialize)
                            {
                                unsafe
                                {
                                    LLVM.BuildMemSet(builder, value, LLVMValueRef.CreateConstNull(int8Type),
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(type), false), 1);
                                }
                            }
                            fixedRoots.Add((value, type));
                        }

                        for (int index = 0; index < GetMethodParameterCount(method.Value.Item3); index++)
                        {
                            var parameterType = method.Value.Item3.HasThis && index == 0
                                ? method.Value.Item3.DeclaringType
                                : SubstituteGenericParameter(method.Value.Item3.Parameters[index - (method.Value.Item3.HasThis ? 1 : 0)].ParameterType,
                                    method.Value.Item3);
                            AddRoot(local[-1 - index], parameterType, false);
                        }
                        if (methodDefinition?.HasBody == true)
                        {
                            foreach (var variable in methodDefinition.Body.Variables)
                                AddRoot(local[variable.Index], SubstituteGenericParameter(variable.VariableType, method.Value.Item3), true);
                        }
                        foreach (var handler in caughtExceptions)
                            AddRoot(new Tuple<LLVMValueRef, LLVMTypeRef>(handler.Value, exceptionPointerType), coreLib.Exception, true);

                        var tracksGCFrames = !SameTypeDefinition(method.Value.Item3.DeclaringType, gcHeapType);
                        var maxStack = Math.Max(1, methodDefinition?.Body.MaxStackSize ?? 1);
                        var stackRootCapacity = maxStack + 2;
                        const int temporaryRootCount = 4;
                        var rootEntryCount = fixedRoots.Count + stackRootCapacity + temporaryRootCount;
                        var rootEntryType = LLVMTypeRef.CreateArray(exceptionPointerType, (uint)(rootEntryCount * 2));
                        var rootEntries = BuildEntryAlloca(rootEntryType, (uint)pointerSize);
                        var rootFrame = BuildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                            (uint)GetTypeSize(coreLib.GCFrame)), (uint)pointerSize);
                        var rootSpills = Enumerable.Range(0, stackRootCapacity).Select(_ => BuildEntryAlloca(exceptionPointerType)).ToArray();
                        var temporaryRootSpills = Enumerable.Range(0, temporaryRootCount)
                            .Select(_ => BuildEntryAlloca(exceptionPointerType)).ToArray();

                        LLVMValueRef GetRootEntryAddress(int index)
                        {
                            return builder.BuildGEP2(rootEntryType, rootEntries,
                                [LLVMValueRef.CreateConstInt(sizeType, 0, false),
                         LLVMValueRef.CreateConstInt(sizeType, (ulong)(index * 2), false)]);
                        }

                        void StoreRootEntry(int index, LLVMValueRef address, TypeReference? descriptor)
                        {
                            var entry = GetRootEntryAddress(index);
                            builder.BuildStore(ConvertValue(builder, address, exceptionPointerType), entry);
                            var descriptorAddress = builder.BuildGEP2(exceptionPointerType, entry,
                                [LLVMValueRef.CreateConstInt(sizeType, 1, false)]);
                            var descriptorValue = descriptor is null
                                ? LLVMValueRef.CreateConstNull(exceptionPointerType)
                                : GetGCDescriptor(descriptor);
                            builder.BuildStore(ConvertValue(builder, descriptorValue, exceptionPointerType), descriptorAddress);
                        }

                        for (int index = 0; index < fixedRoots.Count; index++)
                            StoreRootEntry(index, fixedRoots[index].Address, fixedRoots[index].Descriptor);
                        for (int index = fixedRoots.Count; index < rootEntryCount; index++)
                            StoreRootEntry(index, LLVMValueRef.CreateConstNull(exceptionPointerType), null);

                        void SynchronizeRoots(IEnumerable<(LLVMValueRef Value, TypeReference? Type)> roots)
                        {
                            var values = roots.ToArray();
                            for (int index = 0; index < stackRootCapacity; index++)
                            {
                                var rootIndex = fixedRoots.Count + index;
                                if (index >= values.Length || values[index].Type is null)
                                {
                                    StoreRootEntry(rootIndex, LLVMValueRef.CreateConstNull(exceptionPointerType), null);
                                    continue;
                                }
                                var value = values[index].Value;
                                var type = values[index].Type!;
                                if (IsByReferenceValue(type) || IsManagedReferenceType(type))
                                {
                                    builder.BuildStore(ConvertValue(builder, value, exceptionPointerType), rootSpills[index]);
                                    StoreRootEntry(rootIndex, rootSpills[index], null);
                                }
                                else if (IsValueType(type) && GetGCReferenceOffsets(type).Any())
                                {
                                    StoreRootEntry(rootIndex, value, type);
                                }
                                else
                                {
                                    StoreRootEntry(rootIndex, LLVMValueRef.CreateConstNull(exceptionPointerType), null);
                                }
                            }
                        }

                        void SynchronizeEvaluationStackRoots()
                        {
                            SynchronizeRoots(stack.Reverse().Select(value =>
                                (value, trackedTypes.TryGetValue(value, out var type) ? type : null)));
                        }

                        void StoreTemporaryRoot(int index, LLVMValueRef value, TypeReference type)
                        {
                            var rootIndex = fixedRoots.Count + stackRootCapacity + index;
                            if (IsByReferenceValue(type) || IsManagedReferenceType(type))
                            {
                                builder.BuildStore(ConvertValue(builder, value, exceptionPointerType), temporaryRootSpills[index]);
                                StoreRootEntry(rootIndex, temporaryRootSpills[index], null);
                            }
                            else if (IsValueType(type) && GetGCReferenceOffsets(type).Any())
                            {
                                StoreRootEntry(rootIndex, value, type);
                            }
                            else
                            {
                                StoreRootEntry(rootIndex, LLVMValueRef.CreateConstNull(exceptionPointerType), null);
                            }
                        }

                        void PopGCFrame()
                        {
                            if (tracksGCFrames)
                                builder.BuildCall2(gcPopType, gcPopFunction, [rootFrame]);
                        }

                        var rootEntriesPointer = GetRootEntryAddress(0);
                        if (tracksGCFrames)
                        {
                            builder.BuildCall2(gcPushType, gcPushFunction,
                                [rootFrame, rootEntriesPointer, LLVMValueRef.CreateConstInt(int32Type, (ulong)rootEntryCount, false)]);
                        }

                        if (cctorGuard is not null)
                            builder.BuildCall2(LLVMTypeRef.CreateFunction(voidType, []), cctorGuard.Value.Function, []);

                        var emittedExceptionSetups = new HashSet<ExceptionRegion>();
                        var methodContext = new MethodContext
                        {
                            Builder = builder,
                            EntryBuilder = entryBuilder,
                            Function = method.Value.Item1,
                            Method = method.Value.Item3,
                            Stack = stack,
                            TrackedTypes = trackedTypes,
                            TrackedFunctionTargets = trackedFunctionTargets,
                            TerminatedBlocks = terminatedBlocks,
                            TypeGetTypeFromHandleMethod = typeGetTypeFromHandleMethod,
                            StoreTemporaryRoot = StoreTemporaryRoot,
                            SynchronizeEvaluationStackRoots = SynchronizeEvaluationStackRoots,
                            SynchronizeRoots = SynchronizeRoots,
                            TrackType = TrackType,
                            BuildEntryAlloca = type => BuildEntryAlloca(type),
                            BuildCheckedIntegerArithmetic = BuildCheckedIntegerArithmetic,
                            EmitConditionalException = EmitConditionalException,
                            CheckMultiArrayAccess = CheckMultiArrayAccess,
                            GetDelegateFunctionPointer = GetDelegateFunctionPointer,
                            BuildVirtualDispatch = BuildVirtualDispatch,
                            BuildVirtualFunctionPointer = BuildVirtualFunctionPointer
                        };
                        foreach (var instr in method.Value.Item4)
                        {
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
                                case Code.Jmp:
                                case Code.Ckfinite:
                                case Code.Arglist:
                                case Code.Mkrefany:
                                case Code.Refanyval:
                                case Code.Refanytype:
                                default:
                                    if (constants.TryTranslateConstantInstruction(builder, instr, stack) ||
                                        fields.TryTranslateFieldInstruction(builder, instr, method.Value.Item3, stack, TrackType) ||
                                        arrays.TryTranslateArrayInstruction(builder, entryBuilder, instr, method.Value.Item3, stack, trackedTypes, TrackType, BuildCheckedIntegerArithmetic, EmitConditionalException, SynchronizeEvaluationStackRoots) ||
                                        variables.TryTranslateVariableInstruction(builder, entryBuilder, instr, method.Value.Item3, stack, local, trackedTypes, localRuntimeTypes, GetMethodParameter, TrackType) ||
                                        branches.TryTranslateBranchInstruction(builder, instr, stack, label, terminatedBlocks, SaveStack, RestoreStack) ||
                                        types.TryTranslateTypeInstruction(builder, entryBuilder, method.Value.Item1, instr, method.Value.Item3, stack, terminatedBlocks, BuildRuntimeTypeMatch, TrackType, StoreTemporaryRoot, SynchronizeEvaluationStackRoots, exceptionThrowType, exceptionThrowFunction) ||
                                        exceptions.TryTranslateExceptionInstruction(builder, method.Value.Item1, instr, methodDefinition, stack, terminatedBlocks, caughtExceptions, finallyStates, filterStates, label, exceptionRegions, RegisterFinallyContinuation, SaveStack, exceptionThrowType, exceptionThrowFunction, exceptionCurrentType, exceptionCurrentFunction, exceptionPopType, exceptionPopFunction) ||
                                        calls.TryTranslateCallInstruction(methodContext, instr) ||
                                        prefixes.TryTranslatePrefixInstruction(methodContext, instr, ref unalignedAlignment) ||
                                        returns.TryTranslateReturnInstruction(builder, instr, method.Value.Item3, stack, usesValueReturnBuffer, valueReturnBuffer, PopGCFrame, terminatedBlocks) ||
                                        strings.TryTranslateStringInstruction(builder, instr, stack, SynchronizeEvaluationStackRoots, StoreTemporaryRoot, TrackType) ||
                                        calls.TryTranslateManagedCallInstruction(methodContext, instr) ||
                                        this.stack.TryTranslateStackInstruction(instr, stack) ||
                                        memory.TryTranslateMemoryInstruction(builder, entryBuilder, instr, method.Value.Item3, stack, GetIndirectType, TrackType, ref unalignedAlignment) ||
                                        numeric.TryTranslateNumericInstruction(builder, instr, stack, BuildCheckedIntegerArithmetic, EmitConditionalException))
                                        break;
                                    throw new NotImplementedException("How did you reach that? This can't be happening...");
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
                            PopGCFrame();
                            if (usesValueReturnBuffer)
                            {
                                unsafe
                                {
                                    LLVM.BuildMemSet(builder, valueReturnBuffer, LLVMValueRef.CreateConstNull(int8Type),
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(returnType), false), 1);
                                }
                                builder.BuildRetVoid();
                            }
                            else if (IsVoidType(returnType))
                                builder.BuildRetVoid();
                            else
                                builder.BuildRet(LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType)));
                            terminatedBlocks.Add(block);
                        }
                        if (!terminatedBlocks.Contains(entry))
                        {
                            builder.PositionAtEnd(entry);
                            var returnType = SubstituteGenericParameter(method.Value.Item3.ReturnType, method.Value.Item3);
                            PopGCFrame();
                            if (usesValueReturnBuffer)
                            {
                                unsafe
                                {
                                    LLVM.BuildMemSet(builder, valueReturnBuffer, LLVMValueRef.CreateConstNull(int8Type),
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(returnType), false), 1);
                                }
                                builder.BuildRetVoid();
                            }
                            else if (IsVoidType(returnType))
                                builder.BuildRetVoid();
                            else
                                builder.BuildRet(LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType)));
                        }
                    }
                }
            }

            var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
            var staticRoots = new List<(LLVMValueRef Address, LLVMValueRef Descriptor)>();
            foreach (var (name, storage) in staticFields)
            {
                var fieldType = staticFieldTypes[name];
                if (IsManagedReferenceType(fieldType))
                    staticRoots.Add((LLVMValueRef.CreateConstPointerCast(storage.Item1, pointerType), LLVMValueRef.CreateConstNull(pointerType)));
                else if (IsValueType(fieldType) && GetGCReferenceOffsets(fieldType).Any())
                    staticRoots.Add((LLVMValueRef.CreateConstPointerCast(storage.Item1, pointerType),
                        LLVMValueRef.CreateConstPointerCast(GetGCDescriptor(fieldType), pointerType)));
            }
            var staticRootType = context.GetStructType([pointerType, pointerType, pointerType], false);
            var nextStaticRoot = LLVMValueRef.CreateConstNull(pointerType);
            for (int index = staticRoots.Count - 1; index >= 0; index--)
            {
                var staticRoot = AddInternalGlobal(staticRootType, $"__gc_static_root_{index}");
                staticRoot.Initializer = LLVMValueRef.CreateConstNamedStruct(staticRootType, [nextStaticRoot,
                    staticRoots[index].Address, staticRoots[index].Descriptor]);
                nextStaticRoot = LLVMValueRef.CreateConstPointerCast(staticRoot, pointerType);
            }
            var staticRootHead = GetStaticField(coreLib.GCStaticRootsField);
            var staticRootHeadStorage = staticRootHead.Item1;
            staticRootHeadStorage.Initializer = LLVMValueRef.CreateConstPointerCast(nextStaticRoot, staticRootHead.Item2);

            if (!module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var verificationError))
                throw new InvalidOperationException(verificationError);

            machine.EmitToFile(module, outputFileName, LLVMCodeGenFileType.LLVMObjectFile);
        }
    }

}
