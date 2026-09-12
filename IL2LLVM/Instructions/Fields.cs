sealed class Fields(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateFieldInstruction(LLVMBuilderRef builder, Instruction instruction, MethodReference method,
        Stack<LLVMValueRef> stack, Action<LLVMValueRef, TypeReference> trackType)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Stfld:
                {
                    var fieldReference = (FieldReference)instruction.Operand;
                    var field = GetLocalField(fieldReference);
                    var fieldType = SubstituteFieldType(fieldReference, method);
                    var fieldDeclaringType = ResolveGenericType(fieldReference.DeclaringType, method);
                    var value = stack.Pop();
                    var instance = stack.Pop();
                    if (IsValueType(fieldType))
                        CopyValue(builder, GetFieldAddress(builder, instance, field, fieldDeclaringType), value, GetTypeSize(fieldType));
                    else
                        builder.BuildStore(ConvertValue(builder, value, GetLLVMTypeRef(fieldType)),
                            GetFieldAddress(builder, instance, field, fieldDeclaringType));
                    return true;
                }
            case Code.Ldfld:
            case Code.Ldflda:
                {
                    var fieldReference = (FieldReference)instruction.Operand;
                    var field = GetLocalField(fieldReference);
                    var fieldType = SubstituteFieldType(fieldReference, method);
                    var fieldDeclaringType = ResolveGenericType(fieldReference.DeclaringType, method);
                    var instance = stack.Pop();
                    var address = GetFieldAddress(builder, instance, field, fieldDeclaringType);
                    if (instruction.OpCode.Code == Code.Ldflda)
                    {
                        stack.Push(address);
                        trackType(address, new ByReferenceType(fieldType));
                    }
                    else if (IsValueType(fieldType))
                    {
                        stack.Push(address);
                        trackType(address, fieldType);
                    }
                    else
                    {
                        var value = PromoteSmallIntegerLoad(builder,
                            builder.BuildLoad2(GetLLVMTypeRef(fieldType), address), fieldType);
                        stack.Push(value);
                        trackType(value, fieldType);
                    }
                    return true;
                }
            case Code.Stsfld:
            case Code.Ldsflda:
                {
                    var field = (FieldReference)instruction.Operand;
                    EmitStaticConstructorGuard(builder, method, field.DeclaringType);
                    var storage = GetStaticField(field, method);
                    if (instruction.OpCode.Code == Code.Ldsflda)
                        stack.Push(storage.Item1);
                    else
                    {
                        var value = stack.Count == 0 ? LLVMValueRef.CreateConstNull(storage.Item2) : stack.Pop();
                        var fieldType = SubstituteFieldType(field, method);
                        if (IsValueType(fieldType))
                            CopyValue(builder, storage.Item1, value, GetTypeSize(fieldType));
                        else
                            builder.BuildStore(ConvertValue(builder, value, storage.Item2), storage.Item1);
                    }
                    return true;
                }
            case Code.Ldsfld:
                {
                    var field = (FieldReference)instruction.Operand;
                    EmitStaticConstructorGuard(builder, method, field.DeclaringType);
                    var storage = GetStaticField(field, method);
                    var fieldType = SubstituteFieldType(field, method);
                    var value = IsValueType(fieldType)
                        ? storage.Item1
                        : PromoteSmallIntegerLoad(builder, builder.BuildLoad2(storage.Item2, storage.Item1), fieldType);
                    stack.Push(value);
                    trackType(value, fieldType);
                    return true;
                }
            default:
                return false;
        }
    }

    void EmitStaticConstructorGuard(LLVMBuilderRef builder, MethodReference method, TypeReference type)
    {
        if (IsTypeInitializer(method))
            return;
        var guard = GetCctorGuard(ResolveGenericType(type, method));
        if (guard is not null)
            builder.BuildCall2(LLVMTypeRef.CreateFunction(voidType, []), guard.Value.Function, []);
    }
}
