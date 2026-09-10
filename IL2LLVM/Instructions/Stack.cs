sealed class Stack(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateStackInstruction(Instruction instruction, System.Collections.Generic.Stack<LLVMValueRef> stack)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Pop:
                if (stack.Count != 0)
                    stack.Pop();
                return true;
            case Code.Dup:
                var value = stack.Pop();
                stack.Push(value);
                stack.Push(value);
                return true;
            default:
                return false;
        }
    }
}
