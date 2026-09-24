sealed class Fields(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateFieldInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder,
        Instruction instruction, MethodReference method, Stack<LLVMValueRef> stack,
        Action<LLVMValueRef, TypeReference> trackType, ref uint unalignedAlignment, ref bool volatileAccess)
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
                    StoreValue(builder, GetFieldAddress(builder, instance, field, fieldDeclaringType), value, fieldType,
                        unalignedAlignment, volatileAccess);
                    unalignedAlignment = 0;
                    volatileAccess = false;
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
                    else if (IsValueType(fieldType) && !IsByReferenceValue(fieldType))
                    {
                        var value = LoadValue(builder, entryBuilder, address, fieldType,
                            unalignedAlignment, volatileAccess);
                        stack.Push(value);
                        trackType(value, fieldType);
                    }
                    else
                    {
                        var load = builder.BuildLoad2(GetLLVMTypeRef(fieldType), address);
                        load.Volatile = volatileAccess;
                        if (unalignedAlignment != 0)
                            load.Alignment = unalignedAlignment;
                        var value = PromoteSmallIntegerLoad(builder, load, fieldType);
                        stack.Push(value);
                        trackType(value, fieldType);
                    }
                    unalignedAlignment = 0;
                    volatileAccess = false;
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
                        StoreValue(builder, storage.Item1, value, fieldType, isVolatile: volatileAccess);
                    }
                    volatileAccess = false;
                    unalignedAlignment = 0;
                    return true;
                }
            case Code.Ldsfld:
                {
                    var field = (FieldReference)instruction.Operand;
                    EmitStaticConstructorGuard(builder, method, field.DeclaringType);
                    var storage = GetStaticField(field, method);
                    var fieldType = SubstituteFieldType(field, method);
                    LLVMValueRef value;
                    if (IsValueType(fieldType) && !IsByReferenceValue(fieldType))
                        value = LoadValue(builder, entryBuilder, storage.Item1, fieldType,
                            isVolatile: volatileAccess);
                    else
                    {
                        var load = builder.BuildLoad2(storage.Item2, storage.Item1);
                        load.Volatile = volatileAccess;
                        value = PromoteSmallIntegerLoad(builder, load, fieldType);
                    }
                    volatileAccess = false;
                    unalignedAlignment = 0;
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
        var declaringType = ResolveGenericType(type, method);
        if (IsTypeInitializer(method) &&
            GetRuntimeTypeKey(ResolveGenericType(method.DeclaringType, method)) == GetRuntimeTypeKey(declaringType))
            return;
        var guard = GetCctorGuard(declaringType);
        if (guard is not null)
            builder.BuildCall2(LLVMTypeRef.CreateFunction(voidType, []), guard.Value.Function, []);
    }
}
