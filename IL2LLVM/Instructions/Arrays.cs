sealed class Arrays(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateArrayInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder, Instruction instruction,
        MethodReference method, System.Collections.Generic.Stack<LLVMValueRef> stack, Dictionary<LLVMValueRef, TypeReference> trackedTypes,
        Action<LLVMValueRef, TypeReference> trackType,
        Func<Code, LLVMValueRef, LLVMValueRef, LLVMValueRef> buildCheckedIntegerArithmetic,
        Action<LLVMValueRef, TypeReference> emitConditionalException, Action synchronizeEvaluationStackRoots)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Newarr:
                {
                    var elementType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var elementSize = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(elementType));
                    var count = ConvertValue(builder, stack.Pop(), sizeType);
                    emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, count,
                        LLVMValueRef.CreateConstInt(sizeType, 0, false)), coreLib.OverflowException);
                    var baseSize = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(coreLib.Array), false);
                    var dataSize = buildCheckedIntegerArithmetic(Code.Mul_Ovf_Un, count, elementSize);
                    if (elementType.MetadataType == MetadataType.Char)
                        dataSize = buildCheckedIntegerArithmetic(Code.Add_Ovf_Un, dataSize, elementSize);
                    synchronizeEvaluationStackRoots();
                    var array = BuildAllocationSize(builder,
                        buildCheckedIntegerArithmetic(Code.Add_Ovf_Un, baseSize, dataSize));
                    StoreField(builder, array, GetArrayLengthField(), count);
                    var arrayType = new ArrayType(elementType);
                    InitializeRuntimeType(builder, array, arrayType);
                    stack.Push(array);
                    trackType(array, arrayType);
                    return true;
                }
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
                    CheckArrayAccess(builder, array, index, emitConditionalException);
                    var elementType = instruction.OpCode.Code == Code.Stelem_Any
                        ? SubstituteGenericParameter((TypeReference)instruction.Operand, method)
                        : null;
                    var llvmType = instruction.OpCode.Code switch
                    {
                        Code.Stelem_I => sizeType,
                        Code.Stelem_I1 => int8Type,
                        Code.Stelem_I2 => int16Type,
                        Code.Stelem_I4 => int32Type,
                        Code.Stelem_I8 => int64Type,
                        Code.Stelem_R4 => floatType,
                        Code.Stelem_R8 => doubleType,
                        Code.Stelem_Ref => LLVMTypeRef.CreatePointer(int8Type, 0),
                        Code.Stelem_Any => GetLLVMTypeRef(elementType!),
                        _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
                    };
                    var address = GetArrayElementAddress(builder, array, index, llvmType,
                        elementType is null ? null : GetTypeSize(elementType));
                    if (elementType is not null && IsValueType(elementType))
                        CopyValue(builder, address, value, GetTypeSize(elementType));
                    else
                        builder.BuildStore(ConvertValue(builder, value, llvmType), address);
                    return true;
                }
            case Code.Ldelem_I1:
            case Code.Ldelem_U1:
            case Code.Ldelem_I2:
            case Code.Ldelem_U2:
            case Code.Ldelem_I4:
            case Code.Ldelem_U4:
            case Code.Ldelem_I8:
            case Code.Ldelem_I:
            case Code.Ldelem_R4:
            case Code.Ldelem_R8:
            case Code.Ldelem_Ref:
            case Code.Ldelem_Any:
                {
                    var index = stack.Pop();
                    var array = stack.Pop();
                    CheckArrayAccess(builder, array, index, emitConditionalException);
                    var elementType = instruction.OpCode.Code == Code.Ldelem_Any
                        ? SubstituteGenericParameter((TypeReference)instruction.Operand, method)
                        : null;
                    var llvmType = instruction.OpCode.Code switch
                    {
                        Code.Ldelem_I1 or Code.Ldelem_U1 => int8Type,
                        Code.Ldelem_I2 or Code.Ldelem_U2 => int16Type,
                        Code.Ldelem_I4 or Code.Ldelem_U4 => int32Type,
                        Code.Ldelem_I8 => int64Type,
                        Code.Ldelem_I => sizeType,
                        Code.Ldelem_R4 => floatType,
                        Code.Ldelem_R8 => doubleType,
                        Code.Ldelem_Ref => LLVMTypeRef.CreatePointer(int8Type, 0),
                        Code.Ldelem_Any => GetLLVMTypeRef(elementType!),
                        _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
                    };
                    var address = GetArrayElementAddress(builder, array, index, llvmType,
                        elementType is null ? null : GetTypeSize(elementType));
                    LLVMValueRef value;
                    if (elementType is not null && IsValueType(elementType))
                    {
                        var storage = CreateLocalStorage(entryBuilder, elementType);
                        value = builder.BuildLoad2(storage.Item2, storage.Item1);
                        CopyValue(builder, value, address, GetTypeSize(elementType));
                    }
                    else
                        value = builder.BuildLoad2(llvmType, address);
                    if (instruction.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_U1 or Code.Ldelem_I2 or Code.Ldelem_U2)
                        value = ConvertValue(builder, value, int32Type,
                            instruction.OpCode.Code is Code.Ldelem_I1 or Code.Ldelem_I2);
                    else if (elementType is not null)
                        value = PromoteSmallIntegerLoad(builder, value, elementType);
                    stack.Push(value);
                    if (elementType is not null)
                        trackType(value, elementType);
                    else if (instruction.OpCode.Code == Code.Ldelem_Ref &&
                        trackedTypes.TryGetValue(array, out var arrayType) && arrayType is ArrayType knownArrayType)
                        trackType(value, knownArrayType.ElementType);
                    return true;
                }
            case Code.Ldelema:
                {
                    var index = stack.Pop();
                    var array = stack.Pop();
                    CheckArrayAccess(builder, array, index, emitConditionalException);
                    var elementType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var address = GetArrayElementAddress(builder, array, index, GetLLVMTypeRef(elementType), GetTypeSize(elementType));
                    stack.Push(address);
                    trackType(address, new ByReferenceType(elementType));
                    return true;
                }
            case Code.Ldlen:
                {
                    var array = stack.Pop();
                    emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, array,
                        LLVMValueRef.CreateConstNull(array.TypeOf)), coreLib.NullReferenceException);
                    var field = GetArrayLengthField();
                    stack.Push(builder.BuildLoad2(GetLLVMTypeRefFromMetadataType(field.FieldType.MetadataType),
                        GetFieldAddress(builder, array, field)));
                    return true;
                }
            default:
                return false;
        }
    }

    void CheckArrayAccess(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef index,
        Action<LLVMValueRef, TypeReference> emitConditionalException)
    {
        emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, array,
            LLVMValueRef.CreateConstNull(array.TypeOf)), coreLib.NullReferenceException);
        var nativeIndex = ConvertValue(builder, index, sizeType);
        var length = ConvertValue(builder, builder.BuildLoad2(int32Type,
            GetFieldAddress(builder, array, GetArrayLengthField())), sizeType, false);
        emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE, nativeIndex, length),
            coreLib.IndexOutOfRangeException);
    }
}
