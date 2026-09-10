sealed class Strings(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateStringInstruction(LLVMBuilderRef builder, Instruction instruction, System.Collections.Generic.Stack<LLVMValueRef> stack,
        Action synchronizeEvaluationStackRoots, Action<int, LLVMValueRef, TypeReference> storeTemporaryRoot,
        Action<LLVMValueRef, TypeReference> trackType)
    {
        if (instruction.OpCode.Code != Code.Ldstr)
            return false;
        synchronizeEvaluationStackRoots();
        var value = BuildStringValue(builder, (string)instruction.Operand,
            (temporary, type) => storeTemporaryRoot(0, temporary, type));
        stack.Push(value);
        trackType(value, coreLib.String);
        return true;
    }
}
