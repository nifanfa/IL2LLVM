sealed class Calls(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateCallInstruction(MethodContext methodContext, Instruction instruction)
    {
        var builder = methodContext.Builder;
        var stack = methodContext.Stack;
        switch (instruction.OpCode.Code)
        {
            case Code.Calli:
                {
                    var callSite = (CallSite)instruction.Operand;
                    var functionPointer = stack.Pop();
                    var arguments = Enumerable.Range(0, callSite.Parameters.Count)
                        .Select(_ => stack.Pop()).Reverse().ToArray();
                    var parameterTypes = callSite.Parameters
                        .Select(parameter => GetCallType(SubstituteGenericParameter(
                            parameter.ParameterType, methodContext.Method))).ToArray();
                    for (int index = 0; index < arguments.Length; index++)
                    {
                        var parameterType = SubstituteGenericParameter(callSite.Parameters[index].ParameterType,
                            methodContext.Method);
                        arguments[index] = IsValueType(parameterType) && !IsByReferenceValue(parameterType)
                            ? builder.BuildLoad2(parameterTypes[index], arguments[index])
                            : ConvertValue(builder, arguments[index], parameterTypes[index]);
                    }
                    var callSiteReturnType = SubstituteGenericParameter(callSite.ReturnType, methodContext.Method);
                    var returnType = GetCallType(callSiteReturnType);
                    var functionType = LLVMTypeRef.CreateFunction(
                        returnType, parameterTypes);
                    var result = builder.BuildCall2(functionType, functionPointer, arguments);
                    if (!IsVoidType(callSiteReturnType))
                    {
                        if (IsValueType(callSiteReturnType) && !IsByReferenceValue(callSiteReturnType))
                        {
                            var storage = CreateLocalStorage(methodContext.EntryBuilder, callSiteReturnType);
                            var address = builder.BuildLoad2(storage.Item2, storage.Item1);
                            builder.BuildStore(result, address);
                            result = address;
                        }
                        else
                            result = PromoteSmallIntegerLoad(builder, result, callSiteReturnType);
                        stack.Push(result);
                        methodContext.TrackType(result, callSiteReturnType);
                    }
                    return true;
                }
            case Code.Ldftn:
            case Code.Ldvirtftn:
                {
                    var targetMethod = SpecializeMethodReference((MethodReference)instruction.Operand, methodContext.Method);
                    var callTarget = ResolveCallTarget(targetMethod);
                    if (instruction.OpCode.Code == Code.Ldvirtftn && stack.Count != 0)
                    {
                        var receiver = stack.Pop();
                        if (methodContext.TrackedTypes.TryGetValue(receiver, out var receiverType))
                            callTarget = ResolveVirtualTarget(targetMethod, receiverType);
                        if (callTarget.Resolve()?.IsAbstract == true || callTarget.DeclaringType.Resolve()?.IsInterface == true)
                        {
                            stack.Push(methodContext.BuildVirtualFunctionPointer(targetMethod, receiver));
                            return true;
                        }
                    }
                    var registeredMethod = GetRegisteredMethod(callTarget) ??
                        throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}");
                    stack.Push(registeredMethod.Item1);
                    methodContext.TrackedFunctionTargets[registeredMethod.Item1] = callTarget;
                    return true;
                }
            default:
                return false;
        }
    }

    internal bool TryTranslateMethodCallInstruction(MethodContext methodContext, Instruction instr)
    {
        var builder = methodContext.Builder;
        var entryBuilder = methodContext.EntryBuilder;
        var method = methodContext.Method;
        var stack = methodContext.Stack;
        var trackedTypes = methodContext.TrackedTypes;
        var trackedFunctionTargets = methodContext.TrackedFunctionTargets;
        var terminatedBlocks = methodContext.TerminatedBlocks;
        var StoreTemporaryRoot = methodContext.StoreTemporaryRoot;
        var SynchronizeEvaluationStackRoots = methodContext.SynchronizeEvaluationStackRoots;
        var SynchronizeRoots = methodContext.SynchronizeRoots;
        var TrackType = methodContext.TrackType;
        var GetDelegateFunctionPointer = methodContext.GetDelegateFunctionPointer;
        var BuildVirtualDispatch = methodContext.BuildVirtualDispatch;
        switch (instr.OpCode.Code)
        {
            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
                {
                    MethodReference targetMethod = SpecializeMethodReference((MethodReference)instr.Operand, method);
                    MethodReference callTarget = ResolveCallTarget(targetMethod);
                    bool useRuntimeDispatch = false;
                    TypeReference? virtualContractType = null;
                    var callConstrainedType = methodContext.ConstrainedType;
                    methodContext.ConstrainedType = null;
                    if (instr.OpCode.Code == Code.Callvirt && callConstrainedType is not null)
                    {
                        callTarget = ResolveVirtualTarget(targetMethod, callConstrainedType);
                        useRuntimeDispatch = false;
                    }
                    else if (instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis &&
                        (targetMethod.DeclaringType.Resolve()?.IsInterface == true || FindMethodDefinition(targetMethod)?.IsVirtual == true))
                    {
                        var stackValues = stack.ToArray();
                        if (stackValues.Length > targetMethod.Parameters.Count &&
                            trackedTypes.TryGetValue(stackValues[targetMethod.Parameters.Count], out var receiverType))
                        {
                            callTarget = ResolveVirtualTarget(targetMethod, receiverType);
                            virtualContractType = receiverType;
                            useRuntimeDispatch = IsKnownRuntimeType(receiverType) &&
                                receiverType.Resolve()?.IsSealed != true;
                        }
                        else
                            useRuntimeDispatch = true;
                    }
                    var arrayRuntimeMethodKind = GetArrayRuntimeMethodKind(targetMethod);
                    var isArrayFactoryConstructor = instr.OpCode.Code == Code.Newobj &&
                        arrayRuntimeMethodKind == ArrayRuntimeMethodKind.Constructor;
                    LLVMValueRef ptr = default;
                    bool byReferenceValueConstructor = false;
                    bool scalarValueConstructor = false;

                    if (instr.OpCode.Code == Code.Newobj && !isArrayFactoryConstructor)
                    {
                        var targetType = targetMethod.DeclaringType.Resolve();
                        scalarValueConstructor = targetType?.IsValueType == true &&
                            !IsValueType(targetMethod.DeclaringType) &&
                            !IsByReferenceValue(targetMethod.DeclaringType);
                        SynchronizeEvaluationStackRoots();
                        if (IsValueType(targetMethod.DeclaringType) || scalarValueConstructor)
                        {
                            byReferenceValueConstructor = IsByReferenceValue(targetMethod.DeclaringType);
                            if (!byReferenceValueConstructor)
                            {
                                var storage = CreateLocalStorage(entryBuilder, targetMethod.DeclaringType);
                                if (scalarValueConstructor)
                                {
                                    ptr = storage.Item1;
                                    builder.BuildStore(LLVMValueRef.CreateConstNull(storage.Item2), ptr);
                                }
                                else
                                {
                                    ptr = builder.BuildLoad2(storage.Item2, storage.Item1);
                                    FillMemory(builder, ptr, LLVMValueRef.CreateConstNull(int8Type),
                                        LLVMValueRef.CreateConstInt(sizeType,
                                            (ulong)GetTypeSize(targetMethod.DeclaringType), false));
                                }
                            }
                        }
                        else
                        {
                            int size = targetType is not null && localTypes.ContainsKey(targetType.FullName)
                                ? GetObjectSize(targetMethod.DeclaringType)
                                : pointerSize;
                            ptr = BuildAllocation(builder, size);
                            InitializeRuntimeType(builder, ptr, targetMethod.DeclaringType);
                            StoreTemporaryRoot(0, ptr, targetMethod.DeclaringType);
                        }
                    }

                    var m = GetRegisteredMethod(callTarget);
                    var callDefinition = FindMethodDefinition(callTarget);
                    if (m is null && FindLocalMethod(callTarget, localMethods) is { HasBody: true } definition)
                    {
                        RegisterMethodFunction(module, callTarget, definition.Body.Instructions);
                        m = GetRegisteredMethod(callTarget);
                    }
                    if (m is null && (callTarget.DeclaringType.Resolve()?.IsInterface == true ||
                        callDefinition?.IsAbstract == true))
                        m = new(default, CreateLLVMFunction(module, callTarget), callTarget, null);
                    if (m is null)
                        throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}, called from {method.FullName} at IL_{instr.Offset:X4}.");

                    var targetFuncCreated = m.Item2;
                    var targetFunc = m.Item4?.Any() == true || !(
                        FindLocalMethod(callTarget, localMethods)?.IsAbstract == true ||
                        FindLocalMethod(m.Item3, localMethods)?.IsAbstract == true ||
                        callTarget.Resolve()?.IsAbstract == true || m.Item3.Resolve()?.IsAbstract == true)
                        ? m.Item1
                        : default;
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
                                ? LLVMTypeRef.CreatePointer(int8Type, 0)
                                : GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[Math.Max(0, i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0))].ParameterType, targetMethod))));
                    }
                    targetArgsList.Reverse();
                    LLVMValueRef[] targetArgs = targetArgsList.ToArray();
                    if (instr.OpCode.Code == Code.Newobj && IsRuntimeDelegateConstructor(targetMethod) &&
                        targetArgs.Length >= 2)
                    {
                        var functionValue = targetArgs[^1];
                        targetArgs[^1] = trackedFunctionTargets.TryGetValue(functionValue, out var functionTarget)
                            ? GetDelegateFunctionPointer(targetMethod.DeclaringType, functionTarget, functionValue)
                            : ConvertValue(builder, functionValue, LLVMTypeRef.CreatePointer(int8Type, 0));
                    }
                    if (callConstrainedType is not null && targetArgs.Length != 0 &&
                        IsManagedReferenceType(callConstrainedType) &&
                        trackedTypes.TryGetValue(targetArgs[0], out var constrainedReceiverType) &&
                        constrainedReceiverType is ByReferenceType)
                    {
                        targetArgs[0] = builder.BuildLoad2(GetLLVMTypeRef(callConstrainedType), targetArgs[0]);
                        TrackType(targetArgs[0], callConstrainedType);
                    }
                    var callTargetArgs = (LLVMValueRef[])targetArgs.Clone();
                    if (callConstrainedType is not null && targetArgs.Length != 0 &&
                        GetEnumUnderlyingType(callConstrainedType) is { } constrainedEnumType &&
                        (coreLib.IsEnum(callTarget.DeclaringType) || coreLib.IsValueType(callTarget.DeclaringType) ||
                            coreLib.IsObject(callTarget.DeclaringType)))
                    {
                        var enumValue = builder.BuildLoad2(GetLLVMTypeRef(constrainedEnumType), targetArgs[0]);
                        var boxedEnum = BuildBoxedValue(builder, enumValue, callConstrainedType);
                        targetArgs[0] = boxedEnum;
                        callTargetArgs[0] = boxedEnum;
                        TrackType(boxedEnum, callConstrainedType);
                    }
                    else if (callConstrainedType is not null && targetArgs.Length != 0 &&
                        IsValueType(callConstrainedType) && !IsValueType(callTarget.DeclaringType))
                    {
                        var boxedValue = BuildBoxedValue(builder, targetArgs[0], callConstrainedType);
                        targetArgs[0] = boxedValue;
                        callTargetArgs[0] = boxedValue;
                        TrackType(boxedValue, callConstrainedType);
                    }
                    for (int i = 0; i < callTargetArgs.Length; i++)
                    {
                        var parameterIndex = i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0);
                        var parameterType = parameterIndex < 0
                            ? null
                            : SubstituteGenericParameter(targetMethod.Parameters[parameterIndex].ParameterType, targetMethod);
                        if (parameterType is not null && IsValueType(parameterType) && !IsByReferenceValue(parameterType))
                        {
                            var callType = GetCallType(parameterType);
                            if (GetTypeSize(parameterType) <= pointerSize)
                            {
                                callTargetArgs[i] = callTargetArgs[i].TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                                    ? builder.BuildLoad2(callType, callTargetArgs[i])
                                    : ConvertValue(builder, callTargetArgs[i], callType);
                            }
                            else
                            {
                                var storage = CreateLocalStorage(entryBuilder, parameterType);
                                var copy = builder.BuildLoad2(storage.Item2, storage.Item1);
                                CopyValue(builder, copy, callTargetArgs[i], GetTypeSize(parameterType));
                                TrackType(copy, parameterType);
                                callTargetArgs[i] = builder.BuildLoad2(callType, copy);
                            }
                        }
                        var expectedType = parameterType is null
                            ? LLVMTypeRef.CreatePointer(int8Type, 0)
                            : GetCallType(parameterType);
                        callTargetArgs[i] = ConvertValue(builder, callTargetArgs[i], expectedType);
                    }

                    if (targetMethod.CallingConvention == MethodCallingConvention.VarArg &&
                        UsesArgumentList(callTarget))
                    {
                        var receiverCount = instr.OpCode.Code != Code.Newobj && targetMethod.HasThis ? 1 : 0;
                        var fixedArgumentCount = (callTarget.Resolve()?.Parameters.Count ?? targetMethod.Parameters.Count) + receiverCount;
                        var variableArgumentCount = callTargetArgs.Length - fixedArgumentCount;
                        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
                        LLVMValueRef variableArguments = LLVMValueRef.CreateConstNull(pointerType);
                        if (variableArgumentCount > 0)
                        {
                            var descriptorSize = GetTypeSize(coreLib.VariableArgument);
                            var descriptors = methodContext.BuildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                                (uint)(descriptorSize * variableArgumentCount)));
                            variableArguments = builder.BuildBitCast(descriptors, pointerType);
                            for (int index = 0; index < variableArgumentCount; index++)
                            {
                                var argumentIndex = fixedArgumentCount + index;
                                var parameterIndex = argumentIndex - receiverCount;
                                var parameterType = SubstituteGenericParameter(
                                    targetMethod.Parameters[parameterIndex].ParameterType, targetMethod);
                                var valueStorage = methodContext.BuildEntryAlloca(callTargetArgs[argumentIndex].TypeOf);
                                builder.BuildStore(callTargetArgs[argumentIndex], valueStorage);
                                var descriptor = builder.BuildGEP2(int8Type, variableArguments,
                                [
                                    LLVMValueRef.CreateConstInt(sizeType,
                                        (ulong)(descriptorSize * index), false)
                                ]);
                                StoreField(builder, descriptor, coreLib.VariableArgumentValueField,
                                    builder.BuildBitCast(valueStorage, pointerType));
                                var typeHandleStorage = methodContext.BuildEntryAlloca(LLVMTypeRef.CreateArray(
                                    int8Type, (uint)Math.Max(1, GetTypeSize(coreLib.RuntimeTypeHandle))));
                                var typeHandle = builder.BuildBitCast(typeHandleStorage, pointerType);
                                StoreField(builder, typeHandle, coreLib.RuntimeTypeHandleTypeField,
                                    GetRuntimeTypeObject(parameterType));
                                StoreField(builder, descriptor, coreLib.VariableArgumentTypeField, typeHandle);
                            }
                        }
                        callTargetArgs =
                        [
                            .. callTargetArgs.Take(fixedArgumentCount),
                            variableArguments,
                            LLVMValueRef.CreateConstInt(int32Type, (ulong)variableArgumentCount, false),
                            .. callTargetArgs.Skip(fixedArgumentCount)
                        ];
                    }

                    if (byReferenceValueConstructor)
                    {
                        var referenceParameter = targetMethod.Parameters
                            .Select((parameter, index) => (Type: SubstituteGenericParameter(parameter.ParameterType, targetMethod), Index: index))
                            .SingleOrDefault(parameter => parameter.Type is ByReferenceType or PointerType);
                        if (referenceParameter.Type is null)
                            throw new InvalidOperationException($"By-reference value constructor has no ref parameter: {targetMethod.FullName}.");
                        var reference = ConvertValue(builder, targetArgs[referenceParameter.Index],
                            GetLLVMTypeRef(targetMethod.DeclaringType));
                        stack.Push(reference);
                        TrackType(reference, targetMethod.DeclaringType);
                        break;
                    }

                    var callReturnType = SubstituteGenericParameter(targetMethod.ReturnType, targetMethod);
                    var rootArgs = new List<(LLVMValueRef Value, TypeReference? Type)>();
                    if (ptr != default)
                        rootArgs.Add((ptr, targetMethod.DeclaringType));
                    for (int index = 0; index < targetArgs.Length; index++)
                    {
                        if (instr.OpCode.Code != Code.Newobj && targetMethod.HasThis && index == 0)
                        {
                            rootArgs.Add((targetArgs[index], targetMethod.DeclaringType));
                            continue;
                        }
                        var parameterIndex = index - (instr.OpCode.Code != Code.Newobj && targetMethod.HasThis ? 1 : 0);
                        rootArgs.Add((targetArgs[index], parameterIndex >= 0 && parameterIndex < targetMethod.Parameters.Count
                            ? SubstituteGenericParameter(targetMethod.Parameters[parameterIndex].ParameterType, targetMethod)
                            : null));
                    }
                    rootArgs.AddRange(stack.Reverse().Select(value =>
                        (value, trackedTypes.TryGetValue(value, out var type) ? type : null)));
                    SynchronizeRoots(rootArgs);

                    if (ptr != default)
                    {
                        callTargetArgs = callTargetArgs.Length == 0
                            ? [ptr]
                            : [.. (LLVMValueRef[])[ptr], .. callTargetArgs];
                    }

                    var callArgs = callTargetArgs;
                    if (instr.OpCode.Code == Code.Callvirt && !useRuntimeDispatch && callConstrainedType is null &&
                        virtualContractType is not null && IsValueType(virtualContractType) && targetArgs.Length != 0)
                    {
                        callArgs = (LLVMValueRef[])callTargetArgs.Clone();
                        callArgs[0] = GetBoxedValueAddress(builder, targetArgs[0], virtualContractType);
                    }

                    // A managed method may return the address of one of its value
                    // parameters (for example an unsafe address-of operator).  The
                    // callee's parameter storage ceases to exist when it returns,
                    // so do not use that pointer as the caller's result.  Preserve
                    // the value in caller-owned storage before making the call.
                    var returnsParameterAddress = TryGetReturnedParameterAddress(targetMethod,
                        out var returnedParameterIndex);
                    LLVMValueRef returnedParameterStorage = default;
                    TypeReference? returnedParameterType = null;
                    if (returnsParameterAddress && returnedParameterIndex >= 0 &&
                        returnedParameterIndex < targetMethod.Parameters.Count)
                    {
                        returnedParameterType = SubstituteGenericParameter(
                            targetMethod.Parameters[returnedParameterIndex].ParameterType, targetMethod);
                        var argumentIndex = targetMethod.HasThis ? returnedParameterIndex + 1 : returnedParameterIndex;
                        if (argumentIndex < callTargetArgs.Length)
                        {
                            var storage = CreateLocalStorage(entryBuilder, returnedParameterType);
                            if (IsValueType(returnedParameterType) && !IsByReferenceValue(returnedParameterType))
                            {
                                returnedParameterStorage = builder.BuildLoad2(storage.Item2, storage.Item1);
                                CopyValue(builder, returnedParameterStorage, callTargetArgs[argumentIndex],
                                    GetTypeSize(returnedParameterType));
                            }
                            else
                            {
                                builder.BuildStore(ConvertValue(builder, callTargetArgs[argumentIndex], storage.Item2),
                                    storage.Item1);
                                returnedParameterStorage = storage.Item1;
                            }
                        }
                    }

                    var useVirtualDispatch = instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis &&
                        (useRuntimeDispatch || targetFunc == default);
                    var result = useVirtualDispatch
                        ? BuildVirtualDispatch(targetMethod, callArgs, targetFuncCreated, targetFunc,
                            GetVirtualImplementations(targetMethod, virtualContractType ?? targetMethod.DeclaringType))
                        : builder.BuildCall2(targetFuncCreated, targetFunc, callArgs);
                    if (result != default && returnsParameterAddress &&
                        returnedParameterIndex >= 0 && returnedParameterIndex < targetMethod.Parameters.Count)
                    {
                        if (returnedParameterStorage != default)
                            result = returnedParameterStorage;
                        else
                        {
                            var returnedType = returnedParameterType ?? SubstituteGenericParameter(
                                targetMethod.Parameters[returnedParameterIndex].ParameterType, targetMethod);
                            var storage = CreateLocalStorage(entryBuilder, returnedType);
                            var destination = builder.BuildLoad2(storage.Item2, storage.Item1);
                            CopyValue(builder, destination, result, GetTypeSize(returnedType));
                            result = destination;
                        }
                    }
                    if (isArrayFactoryConstructor)
                    {
                        stack.Push(result);
                        TrackType(result, targetMethod.DeclaringType);
                        break;
                    }
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
                        if (IsValueType(callReturnType) && !IsByReferenceValue(callReturnType))
                        {
                            var storage = CreateLocalStorage(entryBuilder, callReturnType);
                            var address = builder.BuildLoad2(storage.Item2, storage.Item1);
                            builder.BuildStore(result, address);
                            result = address;
                        }
                        result = PromoteSmallIntegerLoad(builder, result,
                            callReturnType);
                        stack.Push(result);
                        TrackType(result, callReturnType);
                    }
                    if (ptr != default)
                    {
                        var constructedValue = scalarValueConstructor
                            ? builder.BuildLoad2(GetLLVMTypeRef(targetMethod.DeclaringType), ptr)
                            : ptr;
                        stack.Push(constructedValue);
                        TrackType(constructedValue, targetMethod.DeclaringType);
                    }
                    if (!useVirtualDispatch && IsNoReturnMethod(callTarget, localMethods))
                    {
                        builder.BuildUnreachable();
                        terminatedBlocks.Add(builder.InsertBlock);
                    }
                }
                break;
            default:
                return false;
        }
        return true;
    }

}
