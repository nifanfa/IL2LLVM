sealed class Returns(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateReturnInstruction(LLVMBuilderRef builder, Instruction instruction, MethodReference method,
        Stack<LLVMValueRef> stack, Action popGCFrame, HashSet<LLVMBasicBlockRef> terminatedBlocks)
    {
        if (instruction.OpCode.Code != Code.Ret)
            return false;
        var returnType = SubstituteGenericParameter(method.ReturnType, method);
        if (!IsVoidType(returnType))
        {
            var value = stack.Count == 0
                ? LLVMValueRef.CreateConstNull(GetCallType(returnType))
                : stack.Pop();
            value = IsValueType(returnType) && !IsByReferenceValue(returnType)
                ? builder.BuildLoad2(GetCallType(returnType), value)
                : ConvertValue(builder, value, GetLLVMTypeRef(returnType));
            popGCFrame();
            builder.BuildRet(value);
        }
        else
        {
            popGCFrame();
            builder.BuildRetVoid();
        }
        terminatedBlocks.Add(builder.InsertBlock);
        return true;
    }
}
