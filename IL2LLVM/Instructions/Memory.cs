sealed class Memory(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateMemoryInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder, Instruction instruction,
        MethodReference method, Stack<LLVMValueRef> stack, Func<LLVMValueRef, TypeReference?> getIndirectType,
        Action<LLVMValueRef, TypeReference> trackType, ref uint unalignedAlignment, ref bool volatileAccess)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        switch (instruction.OpCode.Code)
        {
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
                    var address = ConvertValue(builder, stack.Pop(), pointerType);
                    var indirectType = getIndirectType(address);
                    var type = instruction.OpCode.Code switch
                    {
                        Code.Ldind_I1 or Code.Ldind_U1 => int8Type,
                        Code.Ldind_I2 or Code.Ldind_U2 => int16Type,
                        Code.Ldind_I4 or Code.Ldind_U4 => int32Type,
                        Code.Ldind_I8 => int64Type,
                        Code.Ldind_R4 => floatType,
                        Code.Ldind_R8 => doubleType,
                        Code.Ldind_I when indirectType is PointerType => GetLLVMTypeRef(indirectType),
                        Code.Ldind_I => sizeType,
                        Code.Ldind_Ref => pointerType,
                        _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
                    };
                    var value = builder.BuildLoad2(type, address);
                    value.Volatile = volatileAccess;
                    if (unalignedAlignment != 0)
                    {
                        value.Alignment = unalignedAlignment;
                        unalignedAlignment = 0;
                    }
                    volatileAccess = false;
                    if (instruction.OpCode.Code is Code.Ldind_I1 or Code.Ldind_U1 or Code.Ldind_I2 or Code.Ldind_U2)
                        value = ConvertValue(builder, value, int32Type,
                            instruction.OpCode.Code is Code.Ldind_I1 or Code.Ldind_I2);
                    stack.Push(value);
                    if (indirectType is not null)
                        trackType(value, indirectType);
                    return true;
                }
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
                    var address = ConvertValue(builder, stack.Pop(), pointerType);
                    var indirectType = getIndirectType(address);
                    var type = instruction.OpCode.Code switch
                    {
                        Code.Stind_I1 => int8Type,
                        Code.Stind_I2 => int16Type,
                        Code.Stind_I4 => int32Type,
                        Code.Stind_I8 => int64Type,
                        Code.Stind_R4 => floatType,
                        Code.Stind_R8 => doubleType,
                        Code.Stind_I when indirectType is PointerType => GetLLVMTypeRef(indirectType),
                        Code.Stind_I => sizeType,
                        Code.Stind_Ref => pointerType,
                        _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
                    };
                    var store = builder.BuildStore(ConvertValue(builder, value, type), address);
                    store.Volatile = volatileAccess;
                    if (unalignedAlignment != 0)
                    {
                        store.Alignment = unalignedAlignment;
                        unalignedAlignment = 0;
                    }
                    volatileAccess = false;
                    return true;
                }
            case Code.Initobj:
                {
                    var type = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var address = stack.Pop();
                    FillMemory(builder, address, LLVMValueRef.CreateConstInt(int8Type, 0, false),
                        LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(type)), false));
                    unalignedAlignment = 0;
                    return true;
                }
            case Code.Ldobj:
                {
                    var type = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var address = stack.Pop();
                    var alignment = unalignedAlignment;
                    unalignedAlignment = 0;
                    if (IsValueType(type) && !IsByReferenceValue(type))
                    {
                        stack.Push(LoadValue(builder, entryBuilder, address, type, alignment, volatileAccess));
                    }
                    else
                    {
                        var value = builder.BuildLoad2(GetLLVMTypeRef(type), address);
                        value.Volatile = volatileAccess;
                        if (alignment != 0)
                            value.Alignment = alignment;
                        stack.Push(value);
                        trackType(value, type);
                    }
                    volatileAccess = false;
                    return true;
                }
            case Code.Stobj:
                {
                    var type = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var value = stack.Pop();
                    var address = stack.Pop();
                    var alignment = unalignedAlignment;
                    unalignedAlignment = 0;
                    StoreValue(builder, address, value, type, alignment, volatileAccess);
                    volatileAccess = false;
                    return true;
                }
            case Code.Cpobj:
                {
                    var type = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var source = stack.Pop();
                    var destination = stack.Pop();
                    CopyValue(builder, destination, source, GetTypeSize(type));
                    unalignedAlignment = 0;
                    return true;
                }
            case Code.Cpblk:
                {
                    var length = ConvertValue(builder, stack.Pop(), sizeType, false);
                    var source = ConvertValue(builder, stack.Pop(), pointerType);
                    var destination = ConvertValue(builder, stack.Pop(), pointerType);
                    CopyMemory(builder, destination, source, length);
                    unalignedAlignment = 0;
                    volatileAccess = false;
                    return true;
                }
            case Code.Initblk:
                {
                    var length = ConvertValue(builder, stack.Pop(), sizeType, false);
                    var value = ConvertValue(builder, stack.Pop(), int8Type, false);
                    var destination = ConvertValue(builder, stack.Pop(), pointerType);
                    FillMemory(builder, destination, value, length);
                    unalignedAlignment = 0;
                    volatileAccess = false;
                    return true;
                }
            case Code.Localloc:
                stack.Push(builder.BuildArrayAlloca(int8Type, ConvertValue(builder, stack.Pop(), sizeType, false)));
                return true;
            case Code.Sizeof:
                {
                    var type = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    stack.Push(LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(type), false));
                    return true;
                }
            default:
                return false;
        }
    }
}
