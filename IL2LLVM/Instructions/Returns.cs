sealed class Returns(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateReturnInstruction(LLVMBuilderRef builder, Instruction instruction, MethodReference method,
        System.Collections.Generic.Stack<LLVMValueRef> stack, bool usesValueReturnBuffer, LLVMValueRef valueReturnBuffer,
        Action popGCFrame, HashSet<LLVMBasicBlockRef> terminatedBlocks)
    {
        if (instruction.OpCode.Code != Code.Ret)
            return false;
        var returnType = SubstituteGenericParameter(method.ReturnType, method);
        if (usesValueReturnBuffer)
        {
            if (stack.Count != 0)
                CopyValue(builder, valueReturnBuffer, stack.Pop(), GetTypeSize(returnType));
            else
            {
                unsafe
                {
                    LLVM.BuildMemSet(builder, valueReturnBuffer, LLVMValueRef.CreateConstNull(int8Type),
                        LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(returnType), false), 1);
                }
            }
            popGCFrame();
            builder.BuildRetVoid();
        }
        else if (!IsVoidType(returnType))
        {
            var value = ConvertValue(builder, stack.Count == 0
                ? LLVMValueRef.CreateConstNull(GetLLVMTypeRef(returnType))
                : stack.Pop(), GetLLVMTypeRef(returnType));
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
