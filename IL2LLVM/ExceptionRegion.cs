sealed class ExceptionRegion
{
    public required int Start;
    public required int End;
    public required List<ExceptionHandler> Handlers;
    public required LLVMValueRef Frame;
    public required LLVMValueRef Buffer;
}
