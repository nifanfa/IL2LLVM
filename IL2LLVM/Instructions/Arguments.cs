sealed class Arguments(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateArgumentInstruction(LLVMBuilderRef builder, LLVMValueRef function,
        MethodReference method, Instruction instruction, Stack<LLVMValueRef> stack,
        Action<LLVMValueRef, TypeReference> trackType)
    {
        if (instruction.OpCode.Code != Code.Arglist)
            return false;

        throw new NotSupportedException(
            $"Reading a native variable argument list requires platform-specific vararg access: {method.FullName}.");
    }
}
