sealed partial class Translator
{
    private LLVMContextRef context;
    private LLVMModuleRef module;
    private LLVMTargetMachineRef machine;
    private int pointerSize;
    private LLVMTypeRef sizeType;
    private Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>> moduleMethods = new();
    private Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef>> staticFields = new();
    private Dictionary<string, (LLVMValueRef Function, LLVMValueRef State)> cctorGuards = new(StringComparer.Ordinal);
    private Dictionary<string, MethodDefinition> localMethods = new(StringComparer.Ordinal);
    private Dictionary<string, TypeDefinition> localTypes = new(StringComparer.Ordinal);
    private Dictionary<string, ulong> runtimeTypeIds = new(StringComparer.Ordinal);
    private Dictionary<string, LLVMValueRef> gcDescriptors = new(StringComparer.Ordinal);
    private Dictionary<string, LLVMValueRef> runtimeFieldData = new(StringComparer.Ordinal);
    private Dictionary<string, LLVMValueRef> missingVirtualFunctionPointers = new(StringComparer.Ordinal);
    private List<TypeDefinition> arrayEnumeratorTypes = [];
    private MethodDefinition stringConstructor = null!;
    private LLVMTypeRef gcAllocateType;
    private LLVMValueRef gcAllocateFunction;
    private ulong nextRuntimeTypeId;
    private int nextVirtualDispatchId;

    public void Translate(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Expected an input file, output file, and target triple.");

        string fileName = Path.GetFullPath(args[0]);
        string outputFileName = Path.GetFullPath(args[1]);
        var targetSpecification = args[2].Split(';', StringSplitOptions.TrimEntries);
        if (targetSpecification.Length is < 1 or > 2 || string.IsNullOrEmpty(targetSpecification[0]))
            throw new ArgumentException("Target must be a target triple optionally followed by a code model.");
        string targetTriple = targetSpecification[0];
        var codeModel = targetSpecification.Length == 1 ? LLVMCodeModel.LLVMCodeModelDefault : targetSpecification[1] switch
        {
            "default" => LLVMCodeModel.LLVMCodeModelDefault,
            "tiny" => LLVMCodeModel.LLVMCodeModelTiny,
            "small" => LLVMCodeModel.LLVMCodeModelSmall,
            "kernel" => LLVMCodeModel.LLVMCodeModelKernel,
            "medium" => LLVMCodeModel.LLVMCodeModelMedium,
            "large" => LLVMCodeModel.LLVMCodeModelLarge,
            _ => throw new ArgumentException($"Unsupported LLVM code model '{targetSpecification[1]}'.")
        };

        LLVM.InitializeAllTargetInfos();
        LLVM.InitializeAllTargets();
        LLVM.InitializeAllTargetMCs();
        LLVM.InitializeAllAsmParsers();
        LLVM.InitializeAllAsmPrinters();

        context = LLVMContextRef.Global;
        module = context.CreateModuleWithName(Path.GetFileNameWithoutExtension(fileName));

        module.Target = targetTriple;

        var target = LLVMTargetRef.GetTargetFromTriple(module.Target);
        machine = target.CreateTargetMachine(module.Target, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
                                                 LLVMRelocMode.LLVMRelocStatic, codeModel);

        pointerSize = (int)machine.CreateTargetDataLayout().ABISizeOfType(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
        sizeType = LLVMTypeRef.CreateIntPtr(machine.CreateTargetDataLayout());

        moduleMethods = new();
        staticFields = new();
        cctorGuards = new(StringComparer.Ordinal);
        localTypes = new(StringComparer.Ordinal);
        runtimeTypeIds = new(StringComparer.Ordinal);
        gcDescriptors = new(StringComparer.Ordinal);
        runtimeFieldData = new(StringComparer.Ordinal);
        missingVirtualFunctionPointers = new(StringComparer.Ordinal);
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
        LLVMTypeRef gcRegisterStaticRootType = default;
        LLVMValueRef gcRegisterStaticRootFunction = default;

        {
            var assembly = AssemblyDefinition.ReadAssembly(fileName);
            localMethods = GetAllTypes(assembly.MainModule.Types)
                .SelectMany(t => t.Methods)
                .ToDictionary(m => m.FullName, StringComparer.Ordinal);
            localTypes = GetAllTypes(assembly.MainModule.Types)
                .ToDictionary(t => t.FullName, StringComparer.Ordinal);
            var exceptionRuntimeType = localTypes["System.Runtime.ExceptionRuntime"];
            var voidPointerType = new PointerType(localTypes["System.Void"]);
            var exceptionPushMethod = GetRequiredMethod(exceptionRuntimeType, "Push", false,
                localTypes["System.Void"], voidPointerType, voidPointerType);
            var exceptionPopMethod = GetRequiredMethod(exceptionRuntimeType, "Pop", false,
                localTypes["System.Void"], voidPointerType);
            var exceptionBufferMethod = GetRequiredMethod(exceptionRuntimeType, "GetBuffer", false,
                voidPointerType, voidPointerType);
            var exceptionTopMethod = GetRequiredMethod(exceptionRuntimeType, "GetTop", false,
                voidPointerType);
            var exceptionCurrentMethod = GetRequiredMethod(exceptionRuntimeType, "GetCurrent", false,
                localTypes["System.Exception"]);
            var setjmpMethod = GetRequiredMethod(exceptionRuntimeType, "SetJump", false,
                localTypes["System.Int32"], voidPointerType);
            var longjmpMethod = GetRequiredMethod(exceptionRuntimeType, "LongJump", false,
                localTypes["System.Void"], voidPointerType, localTypes["System.Int32"]);
            var exceptionAbortMethod = GetRequiredMethod(exceptionRuntimeType, "Abort", false,
                localTypes["System.Void"]);
            var exceptionThrowMethod = GetRequiredMethod(exceptionRuntimeType, "Throw", false,
                localTypes["System.Void"], localTypes["System.Exception"]);
            RegisterMethodFunction(module, exceptionPushMethod, exceptionPushMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionPopMethod, exceptionPopMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionBufferMethod, exceptionBufferMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionTopMethod, exceptionTopMethod.Body.Instructions);
            RegisterMethodFunction(module, exceptionCurrentMethod, exceptionCurrentMethod.Body.Instructions);
            RegisterMethodFunction(module, setjmpMethod, null);
            RegisterMethodFunction(module, longjmpMethod, null);
            RegisterMethodFunction(module, exceptionAbortMethod, null);
            var exceptionPointerType = LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);
            RegisterMethodFunction(module, exceptionThrowMethod, exceptionThrowMethod.Body.Instructions);
            var gcHeapType = localTypes["System.Runtime.GCHeap"];
            var gcFramePointerType = new PointerType(localTypes["System.Runtime.GCFrame"]);
            var gcRootPointerType = new PointerType(localTypes["System.Runtime.GCRoot"]);
            var gcDescPointerType = new PointerType(localTypes["System.GCDesc"]);
            var gcAllocateMethod = GetRequiredMethod(gcHeapType, "Allocate", false, voidPointerType,
                localTypes["System.UIntPtr"]);
            var gcPushMethod = GetRequiredMethod(gcHeapType, "Push", false, localTypes["System.Void"],
                gcFramePointerType, gcRootPointerType, localTypes["System.Int32"]);
            var gcPopMethod = GetRequiredMethod(gcHeapType, "Pop", false, localTypes["System.Void"], gcFramePointerType);
            var gcRegisterStaticRootMethod = GetRequiredMethod(gcHeapType, "RegisterStaticRoot", false,
                localTypes["System.Void"], voidPointerType, gcDescPointerType);
            RegisterMethodFunction(module, gcAllocateMethod, gcAllocateMethod.Body.Instructions);
            RegisterMethodFunction(module, gcPushMethod, gcPushMethod.Body.Instructions);
            RegisterMethodFunction(module, gcPopMethod, gcPopMethod.Body.Instructions);
            RegisterMethodFunction(module, gcRegisterStaticRootMethod, gcRegisterStaticRootMethod.Body.Instructions);
            var registeredGCAllocate = GetRegisteredMethod(gcAllocateMethod)!;
            gcAllocateFunction = registeredGCAllocate.Item1;
            gcAllocateType = registeredGCAllocate.Item2;
            var registeredGCPush = GetRegisteredMethod(gcPushMethod)!;
            gcPushFunction = registeredGCPush.Item1;
            gcPushType = registeredGCPush.Item2;
            var registeredGCPop = GetRegisteredMethod(gcPopMethod)!;
            gcPopFunction = registeredGCPop.Item1;
            gcPopType = registeredGCPop.Item2;
            var registeredGCRegisterStaticRoot = GetRegisteredMethod(gcRegisterStaticRootMethod)!;
            gcRegisterStaticRootFunction = registeredGCRegisterStaticRoot.Item1;
            gcRegisterStaticRootType = registeredGCRegisterStaticRoot.Item2;
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
                    setjmpFunction.AddAttributeAtIndex(LLVMAttributeIndex.LLVMAttributeFunctionIndex,
                        context.CreateEnumAttribute(kind, 0));
                }
            }
            var registeredExceptionThrow = GetRegisteredMethod(exceptionThrowMethod)!;
            exceptionThrowFunction = registeredExceptionThrow.Item1;
            exceptionThrowType = registeredExceptionThrow.Item2;
            arrayEnumeratorTypes = localTypes.Values.Where(IsArrayEnumeratorDefinition).ToList();
            stringConstructor = GetRequiredConstructor(localTypes["System.String"],
                new ArrayType(localTypes["System.Char"]));
            var typeGetTypeFromHandleMethod = GetRequiredMethod(localTypes["System.Type"], "GetTypeFromHandle", false,
                localTypes["System.Type"], localTypes["System.RuntimeTypeHandle"]);
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

            bool ContainsGenericParameter(TypeReference type)
            {
                return type switch
                {
                    GenericParameter => true,
                    GenericInstanceType generic => generic.GenericArguments.Any(ContainsGenericParameter),
                    ArrayType array => ContainsGenericParameter(array.ElementType),
                    ByReferenceType byReference => ContainsGenericParameter(byReference.ElementType),
                    PointerType pointer => ContainsGenericParameter(pointer.ElementType),
                    RequiredModifierType requiredModifier => ContainsGenericParameter(requiredModifier.ElementType),
                    OptionalModifierType optionalModifier => ContainsGenericParameter(optionalModifier.ElementType),
                    PinnedType pinned => ContainsGenericParameter(pinned.ElementType),
                    _ => false
                };
            }

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
                    var allocaBlock = method.Value.Item1.AppendBasicBlock("alloca");
                    var entry = method.Value.Item1.AppendBasicBlock(GetLabelName(method.Value.Item4.First()));
                    var builder = context.CreateBuilder();
                    entryBuilder = context.CreateBuilder();
                    entryBuilder.PositionAtEnd(allocaBlock);
                    builder.PositionAtEnd(entry);
                    var cctorGuard = method.Value.Item3.Resolve() is not { IsConstructor: true } && !method.Value.Item3.HasThis
                        ? GetCctorGuard(method.Value.Item3.DeclaringType)
                        : null;
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
                        var methodDefinition = FindLocalMethod(method.Value.Item3, localMethods) ?? method.Value.Item3.Resolve();

                        void TrackType(LLVMValueRef value, TypeReference type)
                        {
                            if (value != default)
                                trackedTypes[value] = type;
                        }

                        LLVMValueRef BuildRuntimeTypeMatch(LLVMValueRef value, TypeReference targetType)
                        {
                            var methodTable = builder.BuildLoad2(sizeType,
                                GetFieldAddress(builder, value, GetObjectMethodTableField()));
                            var matches = new List<LLVMValueRef>();
                            var seen = new HashSet<ulong>();
                            foreach (var candidate in localTypes.Values)
                            {
                                if (!IsRuntimeTypeCompatible(candidate, targetType))
                                    continue;
                                var candidateId = GetRuntimeTypeId(candidate);
                                if (!seen.Add(candidateId))
                                    continue;
                                matches.Add(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, methodTable,
                                    LLVMValueRef.CreateConstInt(sizeType, candidateId, false)));
                            }
                            if (matches.Count == 0)
                                return LLVMValueRef.CreateConstInt(LLVMTypeRef.Int1, 0, false);
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
                            bool allowArraySpecial = true)
                        {
                            LLVMValueRef[] GetImplementationArgs(TypeReference runtimeType)
                            {
                                if (!runtimeType.IsValueType || targetArgs.Length == 0)
                                    return targetArgs;
                                var implementationArgs = (LLVMValueRef[])targetArgs.Clone();
                                implementationArgs[0] = GetBoxedValueAddress(builder, targetArgs[0]);
                                return implementationArgs;
                            }

                            if (allowArraySpecial && TryGetArrayEnumerator(targetMethod, out var enumerableElementType,
                                    out var arrayEnumeratorDefinition, out var constructor))
                            {
                                var constructorMethod = GetRegisteredMethod(constructor) ??
                                    throw new NotSupportedException($"Method is not defined in the input module: {constructor.FullName}");
                                var arrayReceiverValue = targetArgs[0];
                                var arrayMethodTable = builder.BuildLoad2(sizeType,
                                    GetFieldAddress(builder, arrayReceiverValue, GetObjectMethodTableField()));
                                var arrayType = new ArrayType(enumerableElementType);
                                var arrayId = LLVMValueRef.CreateConstInt(sizeType, GetRuntimeTypeId(arrayType), false);
                                var arrayBlock = method.Value.Item1.AppendBasicBlock($"array.enum.{nextVirtualDispatchId++}");
                                var fallbackBlock = method.Value.Item1.AppendBasicBlock($"array.enum.next.{nextVirtualDispatchId++}");
                                var arrayContinuation = method.Value.Item1.AppendBasicBlock($"array.enum.cont.{nextVirtualDispatchId++}");
                                var arraySourceBlock = builder.InsertBlock;
                                builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, arrayMethodTable, arrayId), arrayBlock, fallbackBlock);
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
                                var arrayFallback = BuildVirtualDispatch(targetMethod, targetArgs, targetFunctionType, targetFunction, implementations, false);
                                var arrayFallbackSource = builder.InsertBlock;
                                builder.BuildBr(arrayContinuation);
                                builder.PositionAtEnd(arrayContinuation);
                                var result = builder.BuildPhi(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), "array.enum.result");
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
                                    return builder.BuildCall2(implementation.Method!.Item2, implementation.Method.Item1,
                                        GetImplementationArgs(implementation.RuntimeType));
                                }
                                if (targetFunction != default)
                                    return builder.BuildCall2(targetFunctionType, targetFunction, targetArgs);
                                var abort = GetRegisteredMethod(exceptionAbortMethod) ??
                                    throw new NotSupportedException($"Method is not defined in the input module: {exceptionAbortMethod.FullName}");
                                builder.BuildCall2(abort.Item2, abort.Item1, []);
                                builder.BuildUnreachable();
                                terminatedBlocks.Add(builder.InsertBlock);
                                return IsVoidType(targetMethod.ReturnType)
                                    ? default
                                    : LLVMValueRef.CreateConstNull(GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)));
                            }

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
                                var implementationMethod = GetRegisteredMethod(candidate.Implementation);
                                if (implementationMethod is null)
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
                                var implementationArgs = GetImplementationArgs(candidate.RuntimeType);
                                var result = builder.BuildCall2(implementationMethod.Item2, implementationMethod.Item1, implementationArgs);
                                if (!IsVoidType(targetMethod.ReturnType))
                                {
                                    incomingValues.Add(ConvertValue(builder, result, GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod))));
                                    incomingBlocks.Add(callBlock);
                                }
                                builder.BuildBr(continuation);
                                builder.PositionAtEnd(nextBlock);
                            }

                            if (!IsVoidType(targetMethod.ReturnType) && targetFunction != default)
                            {
                                var fallback = ConvertValue(builder, builder.BuildCall2(targetFunctionType, targetFunction, targetArgs),
                                    GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.ReturnType, targetMethod)));
                                incomingValues.Add(fallback);
                                incomingBlocks.Add(builder.InsertBlock);
                                builder.BuildBr(continuation);
                            }
                            else if (targetFunction != default)
                            {
                                builder.BuildCall2(targetFunctionType, targetFunction, targetArgs);
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
                                    missingFunction = module.AddFunction($"__missing_virtual_{missingVirtualFunctionPointers.Count}",
                                        CreateLLVMFunction(module, targetMethod));
                                    missingFunction.Linkage = LLVMLinkage.LLVMInternalLinkage;
                                    var block = missingFunction.AppendBasicBlock("entry");
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

                            var methodTable = builder.BuildLoad2(sizeType,
                                GetFieldAddress(builder, receiver, GetObjectMethodTableField()));
                            var continuation = method.Value.Item1.AppendBasicBlock($"virt.ftn.cont.{nextVirtualDispatchId++}");
                            var incomingValues = new List<LLVMValueRef>();
                            var incomingBlocks = new List<LLVMBasicBlockRef>();
                            foreach (var implementation in implementations)
                            {
                                var callBlock = method.Value.Item1.AppendBasicBlock($"virt.ftn.value.{nextVirtualDispatchId++}");
                                var nextBlock = method.Value.Item1.AppendBasicBlock($"virt.ftn.next.{nextVirtualDispatchId++}");
                                var typeId = LLVMValueRef.CreateConstInt(sizeType,
                                    GetRuntimeTypeId(implementation.RuntimeType), false);
                                builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, methodTable, typeId),
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
                            var function = builder.BuildPhi(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), "virt.ftn");
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
                                [builder.BuildCall2(exceptionBufferType, exceptionBufferFunction, [region.Frame])]);
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
                                        if (next is not null && !label.ContainsKey(next.Offset))
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

                        if (methodDefinition?.HasBody == true)
                        {
                            foreach (var handler in methodDefinition.Body.ExceptionHandlers)
                            {
                                if (!label.ContainsKey(handler.HandlerStart.Offset))
                                    label.TryAdd(handler.HandlerStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(handler.HandlerStart)));
                                if (handler.FilterStart is not null && !label.ContainsKey(handler.FilterStart.Offset))
                                    label.TryAdd(handler.FilterStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(handler.FilterStart)));
                            }

                            foreach (var regionGroup in methodDefinition.Body.ExceptionHandlers
                                         .GroupBy(handler => (handler.TryStart.Offset, handler.TryEnd.Offset)))
                            {
                                var frame = BuildEntryAlloca(LLVMTypeRef.CreateArray(LLVMTypeRef.Int8, (uint)(pointerSize * 3)), 16);
                                var jumpBufferType = localTypes["System.Runtime.JumpBuffer"];
                                var buffer = BuildEntryAlloca(LLVMTypeRef.CreateArray(LLVMTypeRef.Int8,
                                    (uint)GetTypeSize(jumpBufferType)), (uint)pointerSize);
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

                        var fixedRoots = new List<(LLVMValueRef Address, TypeReference? Descriptor)>();

                        void AddRoot(Tuple<LLVMValueRef, LLVMTypeRef> storage, TypeReference type, bool initialize)
                        {
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
                                    LLVM.BuildMemSet(builder, value, LLVMValueRef.CreateConstNull(LLVMTypeRef.Int8),
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
                            AddRoot(new Tuple<LLVMValueRef, LLVMTypeRef>(handler.Value, exceptionPointerType), localTypes["System.Exception"], true);

                        var tracksGCFrames = !SameTypeDefinition(method.Value.Item3.DeclaringType, gcHeapType);
                        var maxStack = Math.Max(1, methodDefinition?.Body.MaxStackSize ?? 1);
                        var stackRootCapacity = maxStack + 1;
                        const int temporaryRootCount = 4;
                        var rootEntryCount = fixedRoots.Count + stackRootCapacity + temporaryRootCount;
                        var rootEntryType = LLVMTypeRef.CreateArray(exceptionPointerType, (uint)(rootEntryCount * 2));
                        var rootEntries = BuildEntryAlloca(rootEntryType, (uint)pointerSize);
                        var rootFrame = BuildEntryAlloca(LLVMTypeRef.CreateArray(LLVMTypeRef.Int8,
                            (uint)GetTypeSize(localTypes["System.Runtime.GCFrame"])), (uint)pointerSize);
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
                                if (IsManagedReferenceType(type))
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
                            if (IsManagedReferenceType(type))
                            {
                                builder.BuildStore(ConvertValue(builder, value, exceptionPointerType), temporaryRootSpills[index]);
                                StoreRootEntry(rootIndex, temporaryRootSpills[index], null);
                            }
                            else if (IsValueType(type) && GetGCReferenceOffsets(type).Any())
                            {
                                StoreRootEntry(rootIndex, value, type);
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
                                [rootFrame, rootEntriesPointer, LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, (ulong)rootEntryCount, false)]);

                            foreach (var staticField in localTypes.Values.SelectMany(type => type.Fields).Where(field => field.IsStatic))
                            {
                                var fieldType = staticField.FieldType;
                                var descriptor = IsManagedReferenceType(fieldType)
                                    ? null
                                    : IsValueType(fieldType) && GetGCReferenceOffsets(fieldType).Any()
                                        ? fieldType
                                        : null;
                                if (!IsManagedReferenceType(fieldType) && descriptor is null)
                                    continue;
                                var storage = GetStaticField(staticField);
                                builder.BuildCall2(gcRegisterStaticRootType, gcRegisterStaticRootFunction,
                                    [storage.Item1, descriptor is null
                                ? LLVMValueRef.CreateConstNull(exceptionPointerType)
                                : GetGCDescriptor(descriptor)]);
                            }
                        }

                        if (cctorGuard is not null)
                            builder.BuildCall2(LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, []), cctorGuard.Value.Function, []);

                        var emittedExceptionSetups = new HashSet<ExceptionRegion>();
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
                                        var arrayBaseSize = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false);
                                        SynchronizeEvaluationStackRoots();
                                        var ptr = BuildAllocationSize(builder, builder.BuildAdd(arrayBaseSize, builder.BuildMul(count, size)));
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
                                        MethodReference targetMethod = SpecializeMethodReference((MethodReference)instr.Operand, method.Value.Item3);
                                        if (instr.OpCode.Code == Code.Callvirt &&
                                            stack.Count != 0 && trackedTypes.TryGetValue(stack.Peek(), out var enumerableReceiver) &&
                                            enumerableReceiver is ArrayType arrayReceiver &&
                                            TryGetArrayEnumerator(targetMethod, out var enumerableElementType,
                                                out var arrayEnumeratorDefinition, out var constructor) &&
                                            GetRuntimeTypeKey(arrayReceiver.ElementType) == GetRuntimeTypeKey(enumerableElementType))
                                        {
                                            var constructorMethod = GetRegisteredMethod(constructor) ??
                                                throw new NotSupportedException($"Method is not defined in the input module: {constructor.FullName}");
                                            var array = stack.Pop();
                                            var enumeratorType = new GenericInstanceType(arrayEnumeratorDefinition);
                                            enumeratorType.GenericArguments.Add(arrayReceiver.ElementType);
                                            StoreTemporaryRoot(1, array, arrayReceiver);
                                            SynchronizeEvaluationStackRoots();
                                            var enumerator = BuildAllocation(builder, GetObjectSize(enumeratorType));
                                            InitializeRuntimeType(builder, enumerator, enumeratorType);
                                            StoreTemporaryRoot(0, enumerator, enumeratorType);
                                            builder.BuildCall2(constructorMethod.Item2, constructorMethod.Item1, [enumerator, array]);
                                            stack.Push(enumerator);
                                            TrackType(enumerator, enumeratorType);
                                            break;
                                        }
                                        MethodReference callTarget = ResolveCallTarget(targetMethod);
                                        bool useRuntimeDispatch = false;
                                        TypeReference? virtualContractType = null;
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
                                                virtualContractType = receiverType;
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
                                            if (!IsVoidType(invokeReturnType))
                                            {
                                                stack.Push(invokeResult);
                                                TrackType(invokeResult, invokeReturnType);
                                            }
                                            break;
                                        }
                                        if (SameMethodDefinition(targetMethod, typeGetTypeFromHandleMethod))
                                        {
                                            stack.Push(stack.Count == 0
                                                ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                                : stack.Pop());
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
                                                SynchronizeEvaluationStackRoots();
                                                var array = BuildAllocationSize(builder,
                                                    builder.BuildAdd(LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false),
                                                        builder.BuildMul(total, LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(multidimensionalArray.ElementType), false))));
                                                StoreField(builder, array, GetArrayLengthField(), total);
                                                InitializeRuntimeType(builder, array, multidimensionalArray);
                                                StoreTemporaryRoot(0, array, multidimensionalArray);
                                                var lengths = BuildArrayLengthTable(builder, dimensions);
                                                StoreField(builder, array, GetArrayLengthsField(), lengths);
                                                stack.Push(array);
                                                TrackType(array, multidimensionalArray);
                                                break;
                                            }
                                            if (targetMethod.Name is "Get" or "Set" or "Address")
                                            {
                                                LLVMValueRef value = default;
                                                if (targetMethod.Name == "Set")
                                                    value = stack.Pop();
                                                var indices = Enumerable.Range(0, multidimensionalArray.Rank)
                                                    .Select(_ => stack.Pop()).Reverse().ToArray();
                                                var array = stack.Pop();
                                                var address = GetMultiArrayElementAddress(builder, array, indices, elementType,
                                                    GetTypeSize(multidimensionalArray.ElementType));
                                                if (targetMethod.Name == "Set")
                                                {
                                                    if (IsValueType(multidimensionalArray.ElementType))
                                                        CopyValue(builder, address, value, GetTypeSize(multidimensionalArray.ElementType));
                                                    else
                                                        builder.BuildStore(ConvertValue(builder, value, elementType), address);
                                                }
                                                else if (targetMethod.Name == "Get")
                                                    stack.Push(builder.BuildLoad2(elementType, address));
                                                else
                                                {
                                                    stack.Push(address);
                                                    TrackType(address, new ByReferenceType(multidimensionalArray.ElementType));
                                                }
                                                break;
                                            }
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
                                                SynchronizeEvaluationStackRoots();
                                                ptr = BuildAllocation(builder, GetObjectSize(targetMethod.DeclaringType));
                                                InitializeRuntimeType(builder, ptr, targetMethod.DeclaringType);
                                                StoreTemporaryRoot(0, ptr, targetMethod.DeclaringType);
                                                builder.BuildStore(delegateFunction, GetFieldAddress(builder, ptr, GetDelegateField(targetMethod.DeclaringType, "_function")));
                                                StoreField(builder, ptr, GetDelegateField(targetMethod.DeclaringType, "_target"), delegateTarget);
                                                stack.Push(ptr);
                                                TrackType(ptr, targetMethod.DeclaringType);
                                                break;
                                            }
                                            var targetType = targetMethod.DeclaringType.Resolve();
                                            int size = targetType is not null && localTypes.ContainsKey(targetType.FullName)
                                                ? GetObjectSize(targetMethod.DeclaringType)
                                                : pointerSize;
                                            SynchronizeEvaluationStackRoots();
                                            ptr = BuildAllocation(builder, size);
                                            InitializeRuntimeType(builder, ptr, targetMethod.DeclaringType);
                                            StoreTemporaryRoot(0, ptr, targetMethod.DeclaringType);
                                        }

                                        var m = GetRegisteredMethod(callTarget);
                                        if (m is null && callTarget.DeclaringType.Resolve()?.IsInterface == true)
                                            m = new(default, CreateLLVMFunction(module, callTarget), callTarget, null);
                                        if (m is null && FindLocalMethod(callTarget, localMethods) is { HasBody: true } definition)
                                        {
                                            RegisterMethodFunction(module, callTarget, definition.Body.Instructions);
                                            m = GetRegisteredMethod(callTarget);
                                        }
                                        if (m is null)
                                            throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}, called from {method.Value.Item3.FullName} at IL_{instr.Offset:X4}.");

                                        var targetFuncCreated = m.Item2;
                                        var targetFunc = FindLocalMethod(callTarget, localMethods)?.IsAbstract == true ||
                                            FindLocalMethod(m.Item3, localMethods)?.IsAbstract == true ||
                                            callTarget.Resolve()?.IsAbstract == true || m.Item3.Resolve()?.IsAbstract == true
                                            ? default
                                            : m.Item1;
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
                                            var parameterIndex = i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0);
                                            if (parameterIndex >= 0 && IsLPWStrParameter(targetMethod, parameterIndex))
                                            {
                                                targetArgs[i] = GetStringDataPointer(builder, targetArgs[i]);
                                                continue;
                                            }
                                            var expectedType = parameterIndex < 0
                                                ? LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)
                                                : GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[parameterIndex].ParameterType, targetMethod));
                                            targetArgs[i] = ConvertValue(builder, targetArgs[i], expectedType);
                                        }
                                        if (ptr != default)
                                        {
                                            targetArgs = targetArgs.Length == 0
                                                ? [ptr]
                                                : [.. (LLVMValueRef[])[ptr], .. targetArgs];
                                        }

                                        var callArgs = targetArgs;
                                        if (instr.OpCode.Code == Code.Callvirt && !useRuntimeDispatch && callConstrainedType is null &&
                                            virtualContractType is not null && IsValueType(virtualContractType) && targetArgs.Length != 0)
                                        {
                                            callArgs = (LLVMValueRef[])targetArgs.Clone();
                                            callArgs[0] = GetBoxedValueAddress(builder, targetArgs[0]);
                                        }

                                        SynchronizeRoots(callArgs.Select((value, index) =>
                                        {
                                            if (ptr != default)
                                            {
                                                if (index == 0)
                                                    return (value, (TypeReference?)targetMethod.DeclaringType);
                                                index--;
                                            }
                                            if (instr.OpCode.Code != Code.Newobj && targetMethod.HasThis && index == 0)
                                                return (value, (TypeReference?)targetMethod.DeclaringType);
                                            var parameterIndex = index - (instr.OpCode.Code != Code.Newobj && targetMethod.HasThis ? 1 : 0);
                                            return (value, parameterIndex >= 0 && parameterIndex < targetMethod.Parameters.Count
                                                ? SubstituteGenericParameter(targetMethod.Parameters[parameterIndex].ParameterType, targetMethod)
                                                : null);
                                        }));

                                        var result = instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis &&
                                            (useRuntimeDispatch || targetFunc == default)
                                            ? BuildVirtualDispatch(targetMethod, targetArgs, targetFuncCreated, targetFunc,
                                                GetVirtualImplementations(targetMethod, virtualContractType ?? targetMethod.DeclaringType))
                                            : builder.BuildCall2(targetFuncCreated, targetFunc, callArgs);
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
                                        if (!IsVoidType(targetMethod.ReturnType))
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
                                        if (!IsVoidType(callSite.ReturnType))
                                            stack.Push(result);
                                    }
                                    break;
                                case Code.Ldftn:
                                case Code.Ldvirtftn:
                                    {
                                        MethodReference targetMethod = SpecializeMethodReference((MethodReference)instr.Operand, method.Value.Item3);
                                        MethodReference callTarget = ResolveCallTarget(targetMethod);
                                        if (instr.OpCode.Code == Code.Ldvirtftn && stack.Count != 0)
                                        {
                                            var receiver = stack.Pop();
                                            if (trackedTypes.TryGetValue(receiver, out var receiverType))
                                                callTarget = ResolveVirtualTarget(targetMethod, receiverType);
                                            if (callTarget.Resolve()?.IsAbstract == true ||
                                                callTarget.DeclaringType.Resolve()?.IsInterface == true)
                                            {
                                                stack.Push(BuildVirtualFunctionPointer(targetMethod, receiver));
                                                break;
                                            }
                                        }
                                        var registeredMethod = GetRegisteredMethod(callTarget) ??
                                            throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}");
                                        stack.Push(registeredMethod.Item1);
                                    }
                                    break;
                                case Code.Castclass:
                                case Code.Isinst:
                                    {
                                        var targetType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                        var value = stack.Count == 0
                                            ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0))
                                            : stack.Pop();
                                        value = ConvertValue(builder, value, exceptionPointerType);
                                        var nullBlock = method.Value.Item1.AppendBasicBlock($"cast.null.{nextVirtualDispatchId++}");
                                        var checkBlock = method.Value.Item1.AppendBasicBlock($"cast.check.{nextVirtualDispatchId++}");
                                        var matchBlock = method.Value.Item1.AppendBasicBlock($"cast.match.{nextVirtualDispatchId++}");
                                        var failBlock = method.Value.Item1.AppendBasicBlock($"cast.fail.{nextVirtualDispatchId++}");
                                        var continuation = method.Value.Item1.AppendBasicBlock($"cast.cont.{nextVirtualDispatchId++}");
                                        var sourceBlock = builder.InsertBlock;
                                        var isNull = builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, value,
                                            LLVMValueRef.CreateConstNull(exceptionPointerType));
                                        builder.BuildCondBr(isNull, nullBlock, checkBlock);
                                        terminatedBlocks.Add(sourceBlock);

                                        builder.PositionAtEnd(nullBlock);
                                        var nullResult = LLVMValueRef.CreateConstNull(exceptionPointerType);
                                        builder.BuildBr(continuation);

                                        builder.PositionAtEnd(checkBlock);
                                        var matches = BuildRuntimeTypeMatch(value, targetType);
                                        builder.BuildCondBr(matches, matchBlock, failBlock);
                                        terminatedBlocks.Add(checkBlock);

                                        builder.PositionAtEnd(matchBlock);
                                        builder.BuildBr(continuation);

                                        builder.PositionAtEnd(failBlock);
                                        if (instr.OpCode.Code == Code.Castclass)
                                        {
                                            var exceptionType = localTypes["System.InvalidCastException"];
                                            var exception = BuildAllocation(builder, GetObjectSize(exceptionType));
                                            InitializeRuntimeType(builder, exception, exceptionType);
                                            builder.BuildCall2(exceptionThrowType, exceptionThrowFunction, [exception]);
                                        }
                                        else
                                        {
                                            builder.BuildBr(continuation);
                                        }
                                        if (instr.OpCode.Code == Code.Castclass)
                                        {
                                            builder.BuildUnreachable();
                                            terminatedBlocks.Add(failBlock);
                                        }
                                        else
                                            terminatedBlocks.Add(failBlock);

                                        builder.PositionAtEnd(continuation);
                                        var result = builder.BuildPhi(exceptionPointerType, "cast.result");
                                        if (instr.OpCode.Code == Code.Castclass)
                                            result.AddIncoming([nullResult, value], [nullBlock, matchBlock], 2);
                                        else
                                            result.AddIncoming([nullResult, value, nullResult], [nullBlock, matchBlock, failBlock], 3);
                                        stack.Push(result);
                                        TrackType(result, targetType);
                                    }
                                    break;
                                case Code.Ret:
                                    var returnType = SubstituteGenericParameter(method.Value.Item3.ReturnType, method.Value.Item3);
                                    if (!IsVoidType(returnType))
                                    {
                                        var returnValue = ConvertValue(builder, stack.Count == 0
                                            ? LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType))
                                            : stack.Pop(), GetLLVMTypeRef(returnType));
                                        PopGCFrame();
                                        builder.BuildRet(returnValue);
                                    }
                                    else
                                    {
                                        PopGCFrame();
                                        builder.BuildRetVoid();
                                    }
                                    terminatedBlocks.Add(builder.InsertBlock);
                                    break;
                                case Code.Stfld:
                                    {
                                        var fieldReference = (FieldReference)instr.Operand;
                                        FieldDefinition field = GetLocalField(fieldReference);
                                        var fieldType = SubstituteFieldType(fieldReference, method.Value.Item3);
                                        var fieldDeclaringType = ResolveGenericType(fieldReference.DeclaringType, method.Value.Item3);
                                        var val = stack.Pop();
                                        var obj = stack.Pop();
                                        if (IsValueType(fieldType))
                                            CopyValue(builder, GetFieldAddress(builder, obj, field, fieldDeclaringType), val, GetTypeSize(fieldType));
                                        else
                                            builder.BuildStore(ConvertValue(builder, val, GetLLVMTypeRef(fieldType)), GetFieldAddress(builder, obj, field, fieldDeclaringType));
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
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
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
                                        var elementTypeReference = instr.OpCode.Code == Code.Stelem_Any
                                            ? SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3)
                                            : null;
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
                                            Code.Stelem_Any => GetLLVMTypeRef(elementTypeReference!),
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        };
                                        var address = GetArrayElementAddress(builder, array, index, type,
                                            elementTypeReference is null ? null : GetTypeSize(elementTypeReference));
                                        if (elementTypeReference is not null && IsValueType(elementTypeReference))
                                            CopyValue(builder, address, value, GetTypeSize(elementTypeReference));
                                        else
                                            builder.BuildStore(ConvertValue(builder, value, type), address);
                                    }
                                    break;
                                case Code.Stsfld:
                                case Code.Ldsflda:
                                    {
                                        FieldReference field = (FieldReference)instr.Operand;

                                        if (method.Value.Item3.Name != ".cctor")
                                        {
                                            var guard = GetCctorGuard(field.DeclaringType);
                                            if (guard is not null)
                                                builder.BuildCall2(LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, []), guard.Value.Function, []);
                                        }

                                        var ptr = GetStaticField(field, method.Value.Item3);
                                        if (instr.OpCode.Code == Code.Ldsflda)
                                            stack.Push(ptr.Item1);
                                        else
                                        {
                                            var value = stack.Count == 0 ? LLVMValueRef.CreateConstNull(ptr.Item2) : stack.Pop();
                                            var fieldType = SubstituteFieldType(field, method.Value.Item3);
                                            if (IsValueType(fieldType))
                                                CopyValue(builder, ptr.Item1, value, GetTypeSize(fieldType));
                                            else
                                                builder.BuildStore(ConvertValue(builder, value, ptr.Item2), ptr.Item1);
                                        }
                                    }
                                    break;
                                case Code.Ldsfld:
                                    {
                                        FieldReference field = (FieldReference)instr.Operand;
                                        if (method.Value.Item3.Name != ".cctor")
                                        {
                                            var guard = GetCctorGuard(field.DeclaringType);
                                            if (guard is not null)
                                                builder.BuildCall2(LLVMTypeRef.CreateFunction(LLVMTypeRef.Void, []), guard.Value.Function, []);
                                        }
                                        var ptr = GetStaticField(field, method.Value.Item3);
                                        var fieldType = SubstituteFieldType(field, method.Value.Item3);
                                        var value = IsValueType(fieldType)
                                            ? ptr.Item1
                                            : PromoteSmallIntegerLoad(builder, builder.BuildLoad2(ptr.Item2, ptr.Item1), fieldType);
                                        stack.Push(value);
                                        TrackType(value, fieldType);
                                    }
                                    break;
                                case Code.Ldstr:
                                    {
                                        var value = (string)instr.Operand;
                                        SynchronizeEvaluationStackRoots();
                                        var stringValue = BuildStringValue(builder, value,
                                            (temporary, type) => StoreTemporaryRoot(0, temporary, type));
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
                                        var fieldDeclaringType = ResolveGenericType(fieldReference.DeclaringType, method.Value.Item3);
                                        var obj = stack.Pop();
                                        var gep = GetFieldAddress(builder, obj, field, fieldDeclaringType);
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
                                            var value = PromoteSmallIntegerLoad(builder,
                                                builder.BuildLoad2(GetLLVMTypeRef(fieldType), gep), fieldType);
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
                                        var argumentIndex = instr.OpCode.Code switch
                                        {
                                            Code.Ldarg_0 => 0,
                                            Code.Ldarg_1 => 1,
                                            Code.Ldarg_2 => 2,
                                            Code.Ldarg_3 => 3,
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        };
                                        if (method.Value.Item3.HasThis && argumentIndex == 0)
                                        {
                                            var param = method.Value.Item1.GetParam((uint)argumentIndex);
                                            stack.Push(param);
                                            TrackType(param, method.Value.Item3.DeclaringType);
                                        }
                                        else
                                        {
                                            var parameterIndex = argumentIndex - (method.Value.Item3.HasThis ? 1 : 0);
                                            if (parameterIndex >= 0 && parameterIndex < method.Value.Item3.Parameters.Count)
                                            {
                                                var parameterType = SubstituteGenericParameter(
                                                    method.Value.Item3.Parameters[parameterIndex].ParameterType, method.Value.Item3);
                                                var param = local.TryGetValue(-1 - argumentIndex, out var storage)
                                                    ? builder.BuildLoad2(storage.Item2, storage.Item1)
                                                    : method.Value.Item1.GetParam((uint)argumentIndex);
                                                param = PromoteSmallIntegerLoad(builder, param, parameterType);
                                                TrackType(param, parameterType);
                                                stack.Push(param);
                                            }
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
                                        if (method.Value.Item3.HasThis && index == 0)
                                        {
                                            var argument = method.Value.Item1.GetParam((uint)index);
                                            stack.Push(argument);
                                            TrackType(argument, method.Value.Item3.DeclaringType);
                                        }
                                        else
                                        {
                                            var parameterIndex = index - (method.Value.Item3.HasThis ? 1 : 0);
                                            if (parameterIndex >= 0 && parameterIndex < method.Value.Item3.Parameters.Count)
                                            {
                                                var parameterType = SubstituteGenericParameter(
                                                    method.Value.Item3.Parameters[parameterIndex].ParameterType, method.Value.Item3);
                                                var argument = local.TryGetValue(-1 - index, out var storage)
                                                    ? builder.BuildLoad2(storage.Item2, storage.Item1)
                                                    : method.Value.Item1.GetParam((uint)index);
                                                argument = PromoteSmallIntegerLoad(builder, argument, parameterType);
                                                TrackType(argument, parameterType);
                                                stack.Push(argument);
                                            }
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
                                        var parameterIndex = index - (method.Value.Item3.HasThis ? 1 : 0);
                                        var parameterType = SubstituteGenericParameter(
                                            method.Value.Item3.Parameters[parameterIndex].ParameterType, method.Value.Item3);
                                        var llvmType = GetLLVMTypeRef(parameterType);
                                        local[-1 - index] = local.TryGetValue(-1 - index, out var arg)
                                            ? arg
                                            : new(entryBuilder.BuildAlloca(llvmType), llvmType);
                                        builder.BuildStore(ConvertValue(builder, value, llvmType), local[-1 - index].Item1);
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
                                        var elementTypeReference = instr.OpCode.Code == Code.Ldelem_Any
                                            ? SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3)
                                            : null;
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
                                            Code.Ldelem_Any => GetLLVMTypeRef(elementTypeReference!),
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        };
                                        var gep = GetArrayElementAddress(builder, array, index, type,
                                            elementTypeReference is null ? null : GetTypeSize(elementTypeReference));
                                        LLVMValueRef element;
                                        if (elementTypeReference is not null && IsValueType(elementTypeReference))
                                        {
                                            var storage = CreateLocalStorage(entryBuilder, elementTypeReference);
                                            element = builder.BuildLoad2(storage.Item2, storage.Item1);
                                            CopyValue(builder, element, gep, GetTypeSize(elementTypeReference));
                                        }
                                        else
                                            element = builder.BuildLoad2(type, gep);
                                        if (instr.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_U1 or Code.Ldelem_I2 or Code.Ldelem_U2)
                                            element = ConvertValue(builder, element, LLVMTypeRef.Int32,
                                                instr.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_I2);
                                        else if (elementTypeReference is not null)
                                            element = PromoteSmallIntegerLoad(builder, element, elementTypeReference);
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
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        };
                                        var localType = GetMethodVariableType(method.Value.Item3, offset);
                                        var load = builder.BuildLoad2(local[offset].Item2, local[offset].Item1);
                                        if (localType is not null)
                                        {
                                            load = PromoteSmallIntegerLoad(builder, load, localType);
                                            TrackType(load, localRuntimeTypes.TryGetValue(offset, out var runtimeType)
                                                ? runtimeType
                                                : localType);
                                        }
                                        stack.Push(load);
                                    }
                                    break;
                                case Code.Ldc_I4_M1:
                                    stack.Push(LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, unchecked((ulong)-1), true));
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
                                        var value = LLVMValueRef.CreateConstInt(LLVMTypeRef.Int32, (ulong)(instr.OpCode.Code switch
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
                                            Code.Ldc_I4_S => (sbyte)instr.Operand,
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        }));
                                        stack.Push(value);
                                    }
                                    break;
                                case Code.Ldelema:
                                    {
                                        var index = stack.Pop();
                                        var array = stack.Pop();
                                        var elementType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                        var address = GetArrayElementAddress(builder, array, index, GetLLVMTypeRef(elementType), GetTypeSize(elementType));
                                        stack.Push(address);
                                        TrackType(address, new ByReferenceType(elementType));
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
                                        {
                                            stack.Push(argumentValue);
                                            TrackType(argumentValue, SubstituteGenericParameter(
                                                method.Value.Item3.Parameters[index - (method.Value.Item3.HasThis ? 1 : 0)].ParameterType,
                                                method.Value.Item3));
                                        }
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
                                        var address = ConvertValue(builder, stack.Pop(), exceptionPointerType);
                                        var indirectType = GetIndirectType(address);
                                        var type = instr.OpCode.Code switch
                                        {
                                            Code.Ldind_I1 or Code.Ldind_U1 => LLVMTypeRef.Int8,
                                            Code.Ldind_I2 or Code.Ldind_U2 => LLVMTypeRef.Int16,
                                            Code.Ldind_I4 or Code.Ldind_U4 => LLVMTypeRef.Int32,
                                            Code.Ldind_I8 => LLVMTypeRef.Int64,
                                            Code.Ldind_R4 => LLVMTypeRef.Float,
                                            Code.Ldind_R8 => LLVMTypeRef.Double,
                                            Code.Ldind_I when indirectType is PointerType => GetLLVMTypeRef(indirectType),
                                            Code.Ldind_I => sizeType,
                                            Code.Ldind_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
                                        };
                                        var value = builder.BuildLoad2(type, address);
                                        if (instr.OpCode.Code is Code.Ldind_I1 or Code.Ldind_U1 or Code.Ldind_I2 or Code.Ldind_U2)
                                            value = ConvertValue(builder, value, LLVMTypeRef.Int32,
                                                instr.OpCode.Code is Code.Ldind_I1 or Code.Ldind_I2);
                                        stack.Push(value);
                                        if (indirectType is not null)
                                            TrackType(value, indirectType);
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
                                        var address = ConvertValue(builder, stack.Pop(), exceptionPointerType);
                                        var indirectType = GetIndirectType(address);
                                        var type = instr.OpCode.Code switch
                                        {
                                            Code.Stind_I1 => LLVMTypeRef.Int8,
                                            Code.Stind_I2 => LLVMTypeRef.Int16,
                                            Code.Stind_I4 => LLVMTypeRef.Int32,
                                            Code.Stind_I8 => LLVMTypeRef.Int64,
                                            Code.Stind_R4 => LLVMTypeRef.Float,
                                            Code.Stind_R8 => LLVMTypeRef.Double,
                                            Code.Stind_I when indirectType is PointerType => GetLLVMTypeRef(indirectType),
                                            Code.Stind_I => sizeType,
                                            Code.Stind_Ref => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
                                            _ => throw new InvalidOperationException(instr.OpCode.Code.ToString())
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
                                    var activeCatch = methodDefinition is not null
                                        ? methodDefinition.Body.ExceptionHandlers
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
                                    var finallyHandler = methodDefinition is not null
                                        ? methodDefinition.Body.ExceptionHandlers.FirstOrDefault(handler =>
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
                                        var leaveHandler = methodDefinition is not null
                                            ? methodDefinition.Body.ExceptionHandlers.FirstOrDefault(handler =>
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
                                case Code.Sizeof:
                                    {
                                        var type = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                        stack.Push(LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(type), false));
                                    }
                                    break;
                                case Code.Ldtoken:
                                    {
                                        if (instr.Operand is TypeReference tokenType)
                                        {
                                            tokenType = SubstituteGenericParameter(tokenType, method.Value.Item3);
                                            var type = localTypes["System.Type"];
                                            SynchronizeEvaluationStackRoots();
                                            var typeObject = BuildAllocation(builder, GetTypeDefinitionSize(type));
                                            InitializeRuntimeType(builder, typeObject, type);
                                            StoreTemporaryRoot(0, typeObject, type);
                                            StoreField(builder, typeObject, type.Fields.First(field => field.Name == "Name"), BuildStringValue(builder, tokenType.Name,
                                                (temporary, temporaryType) => StoreTemporaryRoot(1, temporary, temporaryType)));
                                            StoreField(builder, typeObject, type.Fields.First(field => field.Name == "Namespace"),
                                                string.IsNullOrEmpty(tokenType.Namespace) ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)) : BuildStringValue(builder, tokenType.Namespace,
                                                    (temporary, temporaryType) => StoreTemporaryRoot(1, temporary, temporaryType)));
                                            StoreField(builder, typeObject, type.Fields.First(field => field.Name == "FullName"), BuildStringValue(builder, tokenType.FullName.Replace('/', '+'),
                                                (temporary, temporaryType) => StoreTemporaryRoot(1, temporary, temporaryType)));
                                            stack.Push(typeObject);
                                        }
                                        else if (instr.Operand is FieldReference field)
                                        {
                                            var handle = GetRuntimeFieldHandle(builder, entryBuilder, field);
                                            stack.Push(handle);
                                            TrackType(handle, localTypes["System.RuntimeFieldHandle"]);
                                        }
                                        else
                                            stack.Push(LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0)));
                                    }
                                    break;
                                case Code.Box:
                                    {
                                        var value = stack.Pop();
                                        var valueType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                        StoreTemporaryRoot(0, value, valueType);
                                        SynchronizeEvaluationStackRoots();
                                        if (TryGetNullableElementType(valueType, out var nullableElementType))
                                        {
                                            var nullableDefinition = valueType.Resolve() ??
                                                throw new NotSupportedException($"Nullable type is not defined: {valueType.FullName}");
                                            var hasValueField = nullableDefinition.Fields.First(field => field.Name == "_hasValue");
                                            var valueField = nullableDefinition.Fields.First(field => field.Name == "_value");
                                            var hasValue = builder.BuildLoad2(GetLLVMTypeRef(hasValueField.FieldType),
                                                GetFieldAddress(builder, value, hasValueField, valueType));
                                            var valueBlock = method.Value.Item1.AppendBasicBlock($"nullable.box.value.{nextVirtualDispatchId++}");
                                            var nullBlock = method.Value.Item1.AppendBasicBlock($"nullable.box.null.{nextVirtualDispatchId++}");
                                            var continuation = method.Value.Item1.AppendBasicBlock($"nullable.box.cont.{nextVirtualDispatchId++}");
                                            var sourceBlock = builder.InsertBlock;
                                            builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, hasValue,
                                                LLVMValueRef.CreateConstNull(hasValue.TypeOf)), valueBlock, nullBlock);
                                            terminatedBlocks.Add(sourceBlock);

                                            builder.PositionAtEnd(valueBlock);
                                            var nullableValueAddress = GetFieldAddress(builder, value, valueField, valueType);
                                            var nullableValue = IsValueType(nullableElementType)
                                                ? nullableValueAddress
                                                : builder.BuildLoad2(GetLLVMTypeRef(nullableElementType), nullableValueAddress);
                                            var nullableBox = BuildBoxedValue(builder, nullableValue, nullableElementType);
                                            builder.BuildBr(continuation);

                                            builder.PositionAtEnd(nullBlock);
                                            var nullValue = LLVMValueRef.CreateConstNull(exceptionPointerType);
                                            builder.BuildBr(continuation);

                                            builder.PositionAtEnd(continuation);
                                            var boxResult = builder.BuildPhi(exceptionPointerType, "nullable.box");
                                            boxResult.AddIncoming([nullableBox, nullValue], [valueBlock, nullBlock], 2);
                                            stack.Push(boxResult);
                                            TrackType(boxResult, nullableElementType);
                                            break;
                                        }
                                        if (IsManagedReferenceType(valueType))
                                        {
                                            var reference = ConvertValue(builder, value, exceptionPointerType);
                                            stack.Push(reference);
                                            TrackType(reference, valueType);
                                            break;
                                        }
                                        var box = BuildBoxedValue(builder, value, valueType);
                                        stack.Push(box);
                                        TrackType(box, valueType);
                                    }
                                    break;
                                case Code.Unbox:
                                case Code.Unbox_Any:
                                    {
                                        var value = stack.Pop();
                                        var valueType = SubstituteGenericParameter((TypeReference)instr.Operand, method.Value.Item3);
                                        var boxedValue = !IsManagedReferenceType(valueType) ? GetBoxedValueAddress(builder, value) : value;
                                        if (instr.OpCode.Code == Code.Unbox_Any && (valueType.MetadataType is MetadataType.Boolean or MetadataType.SByte or MetadataType.Byte or MetadataType.Char or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.IntPtr or MetadataType.UIntPtr or MetadataType.Single or MetadataType.Double))
                                            stack.Push(builder.BuildLoad2(GetLLVMTypeRef(valueType), boxedValue));
                                        else if (instr.OpCode.Code == Code.Unbox_Any && GetEnumUnderlyingType(valueType) is not null)
                                            stack.Push(builder.BuildLoad2(GetLLVMTypeRef(valueType), boxedValue));
                                        else if (instr.OpCode.Code == Code.Unbox_Any && !IsValueType(valueType))
                                            stack.Push(ConvertValue(builder, value, GetLLVMTypeRef(valueType)));
                                        else
                                            stack.Push(boxedValue);
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
                                            switchValue.AddCase(LLVMValueRef.CreateConstInt(value.TypeOf, i, false), label[targets[i].Offset]);
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
                                        var result = builder.BuildZExt(cond, LLVMTypeRef.Int32);

                                        stack.Push(result);
                                    }
                                    break;
                                case Code.Sub:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var result = val1.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                            ? builder.BuildGEP2(LLVMTypeRef.Int8, val1,
                                                [builder.BuildNeg(ConvertValue(builder, val2, sizeType, true))])
                                            : NormalizeBinaryOperands(builder, val1, val2) is var operands
                                                ? IsFloatingValue(operands.Left)
                                                    ? builder.BuildFSub(operands.Left, operands.Right)
                                                    : builder.BuildSub(operands.Left, operands.Right)
                                                : default;
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
                                                : NormalizeBinaryOperands(builder, val1, val2) is var operands
                                                    ? IsFloatingValue(operands.Left)
                                                        ? builder.BuildFAdd(operands.Left, operands.Right)
                                                        : builder.BuildAdd(operands.Left, operands.Right)
                                                    : default;
                                        stack.Push(result);
                                    }
                                    break;
                                case Code.Sub_Ovf:
                                case Code.Sub_Ovf_Un:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        if (val1.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                                            stack.Push(builder.BuildGEP2(LLVMTypeRef.Int8, val1, [builder.BuildNeg(ConvertValue(builder, val2, sizeType, true))]));
                                        else
                                        {
                                            var operands = NormalizeBinaryOperands(builder, val1, val2);
                                            stack.Push(IsFloatingValue(operands.Left)
                                                ? builder.BuildFSub(operands.Left, operands.Right)
                                                : builder.BuildSub(operands.Left, operands.Right));
                                        }
                                    }
                                    break;
                                case Code.Mul_Ovf:
                                case Code.Mul_Ovf_Un:
                                case Code.Mul:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        var result = IsFloatingValue(operands.Left)
                                            ? builder.BuildFMul(operands.Left, operands.Right)
                                            : builder.BuildMul(operands.Left, operands.Right);
                                        stack.Push(result);
                                    }
                                    break;
                                case Code.Div:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        var result = IsFloatingValue(operands.Left)
                                            ? builder.BuildFDiv(operands.Left, operands.Right)
                                            : builder.BuildSDiv(operands.Left, operands.Right);
                                        stack.Push(result);
                                    }
                                    break;
                                case Code.Div_Un:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(IsFloatingValue(operands.Left)
                                            ? builder.BuildFDiv(operands.Left, operands.Right)
                                            : builder.BuildUDiv(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Rem:
                                case Code.Rem_Un:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(IsFloatingValue(operands.Left)
                                            ? builder.BuildFRem(operands.Left, operands.Right)
                                            : instr.OpCode.Code == Code.Rem
                                            ? builder.BuildSRem(operands.Left, operands.Right)
                                            : builder.BuildURem(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.And:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildAnd(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Or:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildOr(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Xor:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildXor(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Shl:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildShl(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Shr:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildAShr(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Shr_Un:
                                    {
                                        var val2 = stack.Pop();
                                        var val1 = stack.Pop();
                                        var operands = NormalizeBinaryOperands(builder, val1, val2);
                                        stack.Push(builder.BuildLShr(operands.Left, operands.Right));
                                    }
                                    break;
                                case Code.Neg:
                                    {
                                        var value = stack.Pop();
                                        stack.Push(IsFloatingValue(value) ? builder.BuildFNeg(value) : builder.BuildNeg(value));
                                    }
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
                            PopGCFrame();
                            if (IsVoidType(returnType))
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
                            if (IsVoidType(returnType))
                                builder.BuildRetVoid();
                            else
                                builder.BuildRet(LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType)));
                        }
                    }
                }
            }
            if (!module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var verificationError))
                throw new InvalidOperationException(verificationError);

            machine.EmitToFile(module, outputFileName, LLVMCodeGenFileType.LLVMObjectFile);
        }
    }

}
