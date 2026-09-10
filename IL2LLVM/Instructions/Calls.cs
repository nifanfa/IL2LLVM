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
                    var functionType = LLVMTypeRef.CreateFunction(GetLLVMTypeRef(callSite.ReturnType),
                        callSite.Parameters.Select(parameter => GetLLVMTypeRef(parameter.ParameterType)).ToArray());
                    var result = builder.BuildCall2(functionType, functionPointer, arguments);
                    if (!IsVoidType(callSite.ReturnType))
                    {
                        stack.Push(result);
                        methodContext.TrackType(result, SubstituteGenericParameter(callSite.ReturnType, methodContext.Method));
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

    internal bool TryTranslateManagedCallInstruction(MethodContext methodContext, Instruction instr)
    {
        var builder = methodContext.Builder;
        var entryBuilder = methodContext.EntryBuilder;
        var function = methodContext.Function;
        var method = methodContext.Method;
        var stack = methodContext.Stack;
        var trackedTypes = methodContext.TrackedTypes;
        var trackedFunctionTargets = methodContext.TrackedFunctionTargets;
        var terminatedBlocks = methodContext.TerminatedBlocks;
        var typeGetTypeFromHandleMethod = methodContext.TypeGetTypeFromHandleMethod;
        var StoreTemporaryRoot = methodContext.StoreTemporaryRoot;
        var SynchronizeEvaluationStackRoots = methodContext.SynchronizeEvaluationStackRoots;
        var SynchronizeRoots = methodContext.SynchronizeRoots;
        var TrackType = methodContext.TrackType;
        var BuildEntryAlloca = methodContext.BuildEntryAlloca;
        var BuildCheckedIntegerArithmetic = methodContext.BuildCheckedIntegerArithmetic;
        var EmitConditionalException = methodContext.EmitConditionalException;
        var CheckMultiArrayAccess = methodContext.CheckMultiArrayAccess;
        var GetDelegateFunctionPointer = methodContext.GetDelegateFunctionPointer;
        var BuildVirtualDispatch = methodContext.BuildVirtualDispatch;
        switch (instr.OpCode.Code)
        {
            case Code.Call:
            case Code.Callvirt:
            case Code.Newobj:
                {
                    MethodReference targetMethod = SpecializeMethodReference((MethodReference)instr.Operand, method);
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
                    var callConstrainedType = methodContext.ConstrainedType;
                    methodContext.ConstrainedType = null;
                    if (instr.OpCode.Code == Code.Callvirt && callConstrainedType is not null)
                    {
                        callTarget = ResolveVirtualTarget(targetMethod, callConstrainedType);
                        useRuntimeDispatch = false;
                    }
                    else if (instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis &&
                        (targetMethod.DeclaringType.Resolve()?.IsInterface == true || targetMethod.Resolve()?.IsVirtual == true))
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
                            ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(int8Type, 0))
                            : stack.Pop();
                        var functionField = GetDelegateField(targetMethod.DeclaringType, "_function");
                        var targetField = GetDelegateField(targetMethod.DeclaringType, "_target");
                        var nextField = GetDelegateField(targetMethod.DeclaringType, "_next");
                        var invokeReturnType = SubstituteGenericParameter(targetMethod.ReturnType, targetMethod);
                        var functionType = LLVMTypeRef.CreateFunction(GetLLVMTypeRef(invokeReturnType),
                            [LLVMTypeRef.CreatePointer(int8Type, 0), .. targetMethod.Parameters.Select(parameter => GetLLVMTypeRef(SubstituteGenericParameter(parameter.ParameterType, targetMethod)))]);
                        StoreTemporaryRoot(2, delegateObject, targetMethod.DeclaringType);
                        var currentSlot = BuildEntryAlloca(LLVMTypeRef.CreatePointer(int8Type, 0));
                        builder.BuildStore(delegateObject, currentSlot);
                        LLVMValueRef resultSlot = default;
                        if (!IsVoidType(invokeReturnType))
                            resultSlot = BuildEntryAlloca(GetLLVMTypeRef(invokeReturnType));
                        var dispatch = context.AppendBasicBlock(function, $"delegate.dispatch.{nextVirtualDispatchId++}");
                        var call = context.AppendBasicBlock(function, $"delegate.call.{nextVirtualDispatchId++}");
                        var continuation = context.AppendBasicBlock(function, $"delegate.cont.{nextVirtualDispatchId++}");
                        var source = builder.InsertBlock;
                        builder.BuildBr(dispatch);
                        terminatedBlocks.Add(source);

                        builder.PositionAtEnd(dispatch);
                        var current = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0), currentSlot);
                        builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, current,
                            LLVMValueRef.CreateConstNull(current.TypeOf)), continuation, call);
                        terminatedBlocks.Add(dispatch);

                        builder.PositionAtEnd(call);
                        var functionPointer = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
                            GetFieldAddress(builder, current, functionField));
                        var callArguments = new List<LLVMValueRef>
                        {
                            builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
                                GetFieldAddress(builder, current, targetField))
                        };
                        callArguments.AddRange(invokeArguments.Select((value, index) => ConvertValue(builder, value,
                            GetLLVMTypeRef(SubstituteGenericParameter(targetMethod.Parameters[index].ParameterType, targetMethod)))));
                        var invokeResult = builder.BuildCall2(functionType, functionPointer, callArguments.ToArray());
                        if (!IsVoidType(invokeReturnType))
                            builder.BuildStore(invokeResult, resultSlot);
                        var next = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
                            GetFieldAddress(builder, current, nextField));
                        builder.BuildStore(next, currentSlot);
                        builder.BuildBr(dispatch);
                        terminatedBlocks.Add(call);

                        builder.PositionAtEnd(continuation);
                        if (!IsVoidType(invokeReturnType))
                        {
                            var invokeValue = builder.BuildLoad2(GetLLVMTypeRef(invokeReturnType), resultSlot);
                            stack.Push(invokeValue);
                            TrackType(invokeValue, invokeReturnType);
                        }
                        break;
                    }
                    if (SameMethodDefinition(targetMethod, typeGetTypeFromHandleMethod))
                    {
                        stack.Push(stack.Count == 0
                            ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(int8Type, 0))
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
                            foreach (var dimension in dimensions)
                            {
                                var nativeDimension = ConvertValue(builder, dimension, sizeType);
                                EmitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, nativeDimension,
                                    LLVMValueRef.CreateConstInt(sizeType, 0, false)), coreLib.OverflowException);
                            }
                            var total = LLVMValueRef.CreateConstInt(sizeType, 1, false);
                            foreach (var dimension in dimensions)
                                total = BuildCheckedIntegerArithmetic(Code.Mul_Ovf_Un, total,
                                    ConvertValue(builder, dimension, sizeType, false));
                            SynchronizeEvaluationStackRoots();
                            var dataSize = BuildCheckedIntegerArithmetic(Code.Mul_Ovf_Un, total,
                                LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(multidimensionalArray.ElementType), false));
                            var allocationSize = BuildCheckedIntegerArithmetic(Code.Add_Ovf_Un,
                                LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(coreLib.Array), false), dataSize);
                            var array = BuildAllocationSize(builder,
                                allocationSize);
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
                            CheckMultiArrayAccess(array, indices);
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
                    bool byReferenceValueConstructor = false;

                    if (instr.OpCode.Code == Code.Newobj)
                    {
                        if (IsDelegateType(targetMethod.DeclaringType))
                        {
                            var functionValue = stack.Count == 0
                                ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(int8Type, 0))
                                : stack.Pop();
                            var delegateFunction = trackedFunctionTargets.TryGetValue(functionValue, out var functionTarget)
                                ? GetDelegateFunctionPointer(targetMethod.DeclaringType, functionTarget, functionValue)
                                : ConvertValue(builder, functionValue, LLVMTypeRef.CreatePointer(int8Type, 0));
                            var delegateTarget = stack.Count == 0
                                ? LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(int8Type, 0))
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
                        SynchronizeEvaluationStackRoots();
                        if (IsValueType(targetMethod.DeclaringType))
                        {
                            byReferenceValueConstructor = IsByReferenceValue(targetMethod.DeclaringType);
                            if (!byReferenceValueConstructor)
                            {
                                var storage = CreateLocalStorage(entryBuilder, targetMethod.DeclaringType);
                                ptr = builder.BuildLoad2(storage.Item2, storage.Item1);
                                unsafe
                                {
                                    LLVM.BuildMemSet(builder, ptr, LLVMValueRef.CreateConstNull(int8Type),
                                        LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(targetMethod.DeclaringType), false), 1);
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
                    if (m is null && callTarget.DeclaringType.Resolve()?.IsInterface == true)
                        m = new(default, CreateLLVMFunction(module, callTarget), callTarget, null);
                    if (m is null && FindLocalMethod(callTarget, localMethods) is { HasBody: true } definition)
                    {
                        RegisterMethodFunction(module, callTarget, definition.Body.Instructions);
                        m = GetRegisteredMethod(callTarget);
                    }
                    if (m is null)
                        throw new NotSupportedException($"Method is not defined in the input module: {callTarget.FullName}, called from {method.FullName} at IL_{instr.Offset:X4}.");

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
                                ? LLVMTypeRef.CreatePointer(int8Type, 0)
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
                    for (int i = 0; i < callTargetArgs.Length; i++)
                    {
                        var parameterIndex = i - (instr.OpCode.Code == Code.Newobj ? 0 : targetMethod.HasThis ? 1 : 0);
                        var parameterType = parameterIndex < 0
                            ? null
                            : SubstituteGenericParameter(targetMethod.Parameters[parameterIndex].ParameterType, targetMethod);
                        if (parameterType is not null && IsValueType(parameterType) && !IsByReferenceValue(parameterType))
                        {
                            var storage = CreateLocalStorage(entryBuilder, parameterType);
                            var copy = builder.BuildLoad2(storage.Item2, storage.Item1);
                            CopyValue(builder, copy, callTargetArgs[i], GetTypeSize(parameterType));
                            callTargetArgs[i] = copy;
                            TrackType(copy, parameterType);
                        }
                        var expectedType = parameterType is null
                            ? LLVMTypeRef.CreatePointer(int8Type, 0)
                            : GetLLVMTypeRef(parameterType);
                        callTargetArgs[i] = ConvertValue(builder, callTargetArgs[i], expectedType);
                    }

                    if (byReferenceValueConstructor)
                    {
                        var referenceParameter = targetMethod.Parameters
                            .Select((parameter, index) => (Type: SubstituteGenericParameter(parameter.ParameterType, targetMethod), Index: index))
                            .SingleOrDefault(parameter => parameter.Type is ByReferenceType);
                        if (referenceParameter.Type is null)
                            throw new InvalidOperationException($"By-reference value constructor has no ref parameter: {targetMethod.FullName}.");
                        var reference = ConvertValue(builder, targetArgs[referenceParameter.Index],
                            GetLLVMTypeRef(targetMethod.DeclaringType));
                        stack.Push(reference);
                        TrackType(reference, targetMethod.DeclaringType);
                        break;
                    }

                    var callReturnType = SubstituteGenericParameter(targetMethod.ReturnType, targetMethod);
                    LLVMValueRef returnBuffer = default;
                    if (UsesValueReturnBuffer(callTarget))
                    {
                        var storage = CreateLocalStorage(entryBuilder, callReturnType);
                        returnBuffer = builder.BuildLoad2(storage.Item2, storage.Item1);
                        unsafe
                        {
                            LLVM.BuildMemSet(builder, returnBuffer, LLVMValueRef.CreateConstNull(int8Type),
                                LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(callReturnType), false), 1);
                        }
                    }

                    var rootArgs = new List<(LLVMValueRef Value, TypeReference? Type)>();
                    if (returnBuffer != default)
                        rootArgs.Add((returnBuffer, callReturnType));
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
                        callArgs = (LLVMValueRef[])targetArgs.Clone();
                        callArgs[0] = GetBoxedValueAddress(builder, targetArgs[0], virtualContractType);
                    }

                    var useVirtualDispatch = instr.OpCode.Code == Code.Callvirt && targetMethod.HasThis &&
                        (useRuntimeDispatch || targetFunc == default);
                    if (returnBuffer != default && !useVirtualDispatch)
                        callArgs = [returnBuffer, .. callArgs];

                    var result = useVirtualDispatch
                        ? BuildVirtualDispatch(targetMethod, callArgs, targetFuncCreated, targetFunc,
                            GetVirtualImplementations(targetMethod, virtualContractType ?? targetMethod.DeclaringType),
                            true, returnBuffer)
                        : builder.BuildCall2(targetFuncCreated, targetFunc, callArgs);
                    if (returnBuffer != default)
                        result = returnBuffer;
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
                        result = PromoteSmallIntegerLoad(builder, result,
                            SubstituteGenericParameter(targetMethod.ReturnType, targetMethod));
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
            default:
                return false;
        }
        return true;
    }
}
