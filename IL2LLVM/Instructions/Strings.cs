sealed class Strings(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateStringInstruction(LLVMBuilderRef builder, Instruction instruction, Stack<LLVMValueRef> stack,
        Action<LLVMValueRef, TypeReference> trackType)
    {
        if (instruction.OpCode.Code != Code.Ldstr)
            return false;
        var value = GetStaticString((string)instruction.Operand);
        stack.Push(value);
        trackType(value, coreLib.String);
        return true;
    }
}
