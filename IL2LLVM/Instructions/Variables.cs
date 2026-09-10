sealed class Variables(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateVariableInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder, Instruction instruction,
        MethodReference method, System.Collections.Generic.Stack<LLVMValueRef> stack,
        Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>> locals,
        Dictionary<LLVMValueRef, TypeReference> trackedTypes, Dictionary<int, TypeReference> localRuntimeTypes,
        Func<int, LLVMValueRef> getMethodParameter, Action<LLVMValueRef, TypeReference> trackType)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0:
            case Code.Stloc_1:
            case Code.Stloc_2:
            case Code.Stloc_3:
            case Code.Stloc:
            case Code.Stloc_S:
                {
                    var index = GetLocalIndex(instruction);
                    var variableType = GetMethodVariableType(method, index);
                    var value = stack.Count != 0
                        ? stack.Pop()
                        : LLVMValueRef.CreateConstNull(variableType is not null &&
                            GetLLVMTypeRef(variableType).Kind == LLVMTypeKind.LLVMPointerTypeKind
                                ? GetLLVMTypeRef(variableType)
                                : sizeType);
                    if (variableType is not null && IsValueType(variableType) && !IsByReferenceValue(variableType))
                    {
                        var storage = locals.TryGetValue(index, out var existing)
                            ? existing
                            : CreateLocalStorage(entryBuilder, variableType);
                        locals.TryAdd(index, storage);
                        CopyValue(builder, builder.BuildLoad2(storage.Item2, storage.Item1), value, GetTypeSize(variableType));
                    }
                    else
                    {
                        var storageType = variableType is null ? value.TypeOf : GetLLVMTypeRef(variableType);
                        var storage = locals.TryGetValue(index, out var existing)
                            ? existing
                            : new Tuple<LLVMValueRef, LLVMTypeRef>(entryBuilder.BuildAlloca(storageType), storageType);
                        builder.BuildStore(ConvertValue(builder, value, storage.Item2), storage.Item1);
                        locals.TryAdd(index, storage);
                    }
                    if (trackedTypes.TryGetValue(value, out var runtimeType))
                        localRuntimeTypes[index] = runtimeType;
                    return true;
                }
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            case Code.Ldloc:
            case Code.Ldloc_S:
                {
                    var index = GetLocalIndex(instruction);
                    var localType = GetMethodVariableType(method, index);
                    var value = builder.BuildLoad2(locals[index].Item2, locals[index].Item1);
                    if (localType is not null)
                    {
                        value = PromoteSmallIntegerLoad(builder, value, localType);
                        trackType(value, localRuntimeTypes.TryGetValue(index, out var runtimeType)
                            ? runtimeType
                            : localType);
                    }
                    stack.Push(value);
                    return true;
                }
            case Code.Ldloca:
            case Code.Ldloca_S:
                {
                    var variable = (VariableDefinition)instruction.Operand;
                    var variableType = SubstituteGenericParameter(variable.VariableType, method);
                    var storage = locals.TryGetValue(variable.Index, out var existing)
                        ? existing
                        : CreateLocalStorage(entryBuilder, variableType);
                    locals.TryAdd(variable.Index, storage);
                    var address = IsValueType(variableType) && !IsByReferenceValue(variableType)
                        ? builder.BuildLoad2(storage.Item2, storage.Item1)
                        : storage.Item1;
                    stack.Push(address);
                    trackType(address, new ByReferenceType(variableType));
                    return true;
                }
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            case Code.Ldarg:
            case Code.Ldarg_S:
                {
                    var index = GetArgumentIndex(instruction, method);
                    if (method.HasThis && index == 0)
                    {
                        var value = getMethodParameter(index);
                        stack.Push(value);
                        trackType(value, method.DeclaringType);
                        return true;
                    }
                    var parameterIndex = index - (method.HasThis ? 1 : 0);
                    if (parameterIndex < 0 || parameterIndex >= method.Parameters.Count)
                        return true;
                    var parameterType = SubstituteGenericParameter(method.Parameters[parameterIndex].ParameterType, method);
                    var argument = locals.TryGetValue(-1 - index, out var storage)
                        ? builder.BuildLoad2(storage.Item2, storage.Item1)
                        : getMethodParameter(index);
                    argument = PromoteSmallIntegerLoad(builder, argument, parameterType);
                    trackType(argument, parameterType);
                    stack.Push(argument);
                    return true;
                }
            case Code.Starg:
            case Code.Starg_S:
                {
                    var index = GetArgumentIndex(instruction, method);
                    var value = stack.Pop();
                    var parameterIndex = index - (method.HasThis ? 1 : 0);
                    var parameterType = SubstituteGenericParameter(method.Parameters[parameterIndex].ParameterType, method);
                    var llvmType = GetLLVMTypeRef(parameterType);
                    var storage = locals.TryGetValue(-1 - index, out var existing)
                        ? existing
                        : new Tuple<LLVMValueRef, LLVMTypeRef>(entryBuilder.BuildAlloca(llvmType), llvmType);
                    locals[-1 - index] = storage;
                    builder.BuildStore(ConvertValue(builder, value, llvmType), storage.Item1);
                    return true;
                }
            case Code.Ldarga:
            case Code.Ldarga_S:
                {
                    var index = GetArgumentIndex(instruction, method);
                    if (!locals.TryGetValue(-1 - index, out var storage))
                    {
                        var argument = getMethodParameter(index);
                        storage = new(entryBuilder.BuildAlloca(argument.TypeOf), argument.TypeOf);
                        builder.BuildStore(argument, storage.Item1);
                        locals[-1 - index] = storage;
                    }
                    var parameterType = method.HasThis && index == 0
                        ? method.DeclaringType
                        : SubstituteGenericParameter(method.Parameters[index - (method.HasThis ? 1 : 0)].ParameterType, method);
                    var address = IsValueType(parameterType) && !IsByReferenceValue(parameterType)
                        ? builder.BuildLoad2(storage.Item2, storage.Item1)
                        : storage.Item1;
                    stack.Push(address);
                    trackType(address, new ByReferenceType(parameterType));
                    return true;
                }
            default:
                return false;
        }
    }

    static int GetLocalIndex(Instruction instruction)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => 0,
            Code.Ldloc_1 or Code.Stloc_1 => 1,
            Code.Ldloc_2 or Code.Stloc_2 => 2,
            Code.Ldloc_3 or Code.Stloc_3 => 3,
            Code.Ldloc or Code.Ldloc_S or Code.Stloc or Code.Stloc_S => ((VariableDefinition)instruction.Operand).Index,
            _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
        };
    }

    static int GetArgumentIndex(Instruction instruction, MethodReference method)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => 0,
            Code.Ldarg_1 => 1,
            Code.Ldarg_2 => 2,
            Code.Ldarg_3 => 3,
            _ => instruction.Operand is ParameterDefinition parameter
                ? parameter.Index + (method.HasThis ? 1 : 0)
                : Convert.ToInt32(instruction.Operand)
        };
    }
}
