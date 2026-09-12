sealed class MethodContext
{
    public required LLVMBuilderRef Builder { get; init; }
    public required LLVMBuilderRef EntryBuilder { get; init; }
    public required LLVMValueRef Function { get; init; }
    public required MethodReference Method { get; init; }
    public required Stack<LLVMValueRef> Stack { get; init; }
    public required Dictionary<LLVMValueRef, TypeReference> TrackedTypes { get; init; }
    public required Dictionary<LLVMValueRef, MethodReference> TrackedFunctionTargets { get; init; }
    public required HashSet<LLVMBasicBlockRef> TerminatedBlocks { get; init; }
    public required MethodReference TypeGetTypeFromHandleMethod { get; init; }
    public required Action<int, LLVMValueRef, TypeReference> StoreTemporaryRoot { get; init; }
    public required Action SynchronizeEvaluationStackRoots { get; init; }
    public required Action<IEnumerable<(LLVMValueRef Value, TypeReference? Type)>> SynchronizeRoots { get; init; }
    public required Action<LLVMValueRef, TypeReference> TrackType { get; init; }
    public required Func<LLVMTypeRef, LLVMValueRef> BuildEntryAlloca { get; init; }
    public required Func<Code, LLVMValueRef, LLVMValueRef, LLVMValueRef> BuildCheckedIntegerArithmetic { get; init; }
    public required Action<LLVMValueRef, TypeReference> EmitConditionalException { get; init; }
    public required Action<LLVMValueRef, LLVMValueRef[]> CheckMultiArrayAccess { get; init; }
    public required Func<TypeReference, MethodReference, LLVMValueRef, LLVMValueRef> GetDelegateFunctionPointer { get; init; }
    public required Func<MethodReference, LLVMValueRef[], LLVMTypeRef, LLVMValueRef,
        List<(TypeReference RuntimeType, MethodReference Implementation)>, bool, LLVMValueRef, LLVMValueRef> BuildVirtualDispatch
    { get; init; }
    public required Func<MethodReference, LLVMValueRef, LLVMValueRef> BuildVirtualFunctionPointer { get; init; }
    public TypeReference? ConstrainedType { get; set; }
}
