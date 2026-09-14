sealed class Arguments(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateArgumentInstruction(LLVMBuilderRef builder, LLVMValueRef function,
        MethodReference method, Instruction instruction, Stack<LLVMValueRef> stack,
        Action<LLVMValueRef, TypeReference> trackType)
    {
        if (instruction.OpCode.Code != Code.Arglist)
            return false;

        var handleStorage = CreateLocalStorage(builder, coreLib.RuntimeArgumentHandle);
        var handle = builder.BuildLoad2(handleStorage.Item2, handleStorage.Item1);
        var hiddenParameterIndex = GetMethodParameterCount(method);
        StoreField(builder, handle, coreLib.RuntimeArgumentHandleArgumentsField,
            function.GetParam((uint)hiddenParameterIndex));
        StoreField(builder, handle, coreLib.RuntimeArgumentHandleCountField,
            function.GetParam((uint)(hiddenParameterIndex + 1)));
        stack.Push(handle);
        trackType(handle, coreLib.RuntimeArgumentHandle);
        return true;
    }
}
