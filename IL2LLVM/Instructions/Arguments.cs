sealed class Arguments(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateArgIteratorConstructorCall(MethodContext methodContext, MethodReference targetMethod)
    {
        if (!SameMethodDefinition(targetMethod, coreLib.ArgIteratorConstructor))
            return false;

        var builder = methodContext.Builder;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var handle = ConvertValue(builder, methodContext.Stack.Pop(), pointerType);
        var iterator = ConvertValue(builder, methodContext.Stack.Pop(), pointerType);
        var vaList = builder.BuildLoad2(GetLLVMTypeRef(coreLib.RuntimeArgumentHandleValueField.FieldType),
            GetFieldAddress(builder, handle, coreLib.RuntimeArgumentHandleValueField));
        StoreField(builder, iterator, coreLib.ArgIteratorHandleField, vaList);
        return true;
    }

    internal bool TryTranslateArgIteratorCall(MethodContext methodContext, MethodReference targetMethod)
    {
        if (!SameMethodDefinition(targetMethod, coreLib.ArgIteratorGetNextArgMethod))
            return false;

        var builder = methodContext.Builder;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var iterator = ConvertValue(builder, methodContext.Stack.Pop(), pointerType);
        var handle = builder.BuildLoad2(GetLLVMTypeRef(coreLib.ArgIteratorHandleField.FieldType),
            GetFieldAddress(builder, iterator, coreLib.ArgIteratorHandleField));
        var typedReferenceStorage = methodContext.BuildEntryAlloca(
            LLVMTypeRef.CreateArray(int8Type, (uint)Math.Max(1, GetTypeSize(coreLib.TypedReference))));
        var typedReference = ConvertValue(builder, typedReferenceStorage, pointerType);
        unsafe
        {
            LLVM.BuildMemSet(builder, typedReference, LLVMValueRef.CreateConstNull(int8Type),
                LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(coreLib.TypedReference)), false),
                (uint)pointerSize);
        }
        StoreField(builder, typedReference, coreLib.TypedReferenceValueField, handle);
        // Native va_list does not carry a managed RuntimeTypeHandle.  Leave the
        // handle unset; refanyval's vararg path consumes the native va_list and
        // deliberately does not inspect this field.
        StoreField(builder, typedReference, coreLib.TypedReferenceKindField,
            LLVMValueRef.CreateConstInt(int32Type, 1, false));
        methodContext.Stack.Push(typedReference);
        methodContext.TrackType(typedReference, coreLib.TypedReference);
        return true;
    }

    internal bool TryTranslateArgumentInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder,
        MethodReference method, Instruction instruction, Stack<LLVMValueRef> stack,
        Action<LLVMValueRef, TypeReference> trackType)
    {
        if (instruction.OpCode.Code != Code.Arglist)
            return false;

        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var handleStorage = CreateLocalStorage(entryBuilder, coreLib.RuntimeArgumentHandle);
        var handle = builder.BuildLoad2(handleStorage.Item2, handleStorage.Item1);
        var vaListStorage = entryBuilder.BuildAlloca(LLVMTypeRef.CreateArray(int8Type,
            (uint)(pointerSize * 4)));
        var vaList = builder.BuildBitCast(vaListStorage, pointerType);
        var vaStartType = LLVMTypeRef.CreateFunction(voidType, [pointerType]);
        var vaStart = module.GetNamedFunction("llvm.va_start.p0");
        if (vaStart == default)
            vaStart = module.AddFunction("llvm.va_start.p0", vaStartType);
        builder.BuildCall2(vaStartType, vaStart, [vaList]);
        StoreField(builder, handle, coreLib.RuntimeArgumentHandleValueField, vaList);
        stack.Push(handle);
        trackType(handle, coreLib.RuntimeArgumentHandle);
        return true;
    }
}
