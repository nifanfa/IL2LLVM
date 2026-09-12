sealed class Exceptions(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateExceptionInstruction(LLVMBuilderRef builder, LLVMValueRef function, Instruction instruction,
        MethodDefinition? method, Stack<LLVMValueRef> stack, HashSet<LLVMBasicBlockRef> terminatedBlocks,
        Dictionary<ExceptionHandler, LLVMValueRef> caughtExceptions,
        Dictionary<ExceptionHandler, (LLVMValueRef Slot, Dictionary<int, LLVMBasicBlockRef> Targets)> finallyStates,
        Dictionary<int, (LLVMBasicBlockRef Handler, LLVMBasicBlockRef Next)> filterStates,
        SortedDictionary<int, LLVMBasicBlockRef> labels, List<ExceptionRegion> exceptionRegions,
        Action<ExceptionHandler, LLVMBasicBlockRef> registerFinallyContinuation,
        Action<LLVMBasicBlockRef> saveStack, LLVMTypeRef exceptionThrowType, LLVMValueRef exceptionThrowFunction,
        LLVMTypeRef exceptionCurrentType, LLVMValueRef exceptionCurrentFunction,
        LLVMTypeRef exceptionPopType, LLVMValueRef exceptionPopFunction)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        switch (instruction.OpCode.Code)
        {
            case Code.Throw:
                builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                    [stack.Count == 0 ? LLVMValueRef.CreateConstNull(pointerType) : ConvertValue(builder, stack.Pop(), pointerType)]);
                builder.BuildUnreachable();
                terminatedBlocks.Add(builder.InsertBlock);
                return true;
            case Code.Rethrow:
                {
                    var activeCatch = method?.Body.ExceptionHandlers
                        .Where(handler => handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter &&
                            handler.HandlerStart.Offset <= instruction.Offset &&
                            (handler.HandlerEnd is null || instruction.Offset < handler.HandlerEnd.Offset))
                        .OrderByDescending(handler => handler.HandlerStart.Offset)
                        .FirstOrDefault();
                    builder.BuildCall2(exceptionThrowType, exceptionThrowFunction,
                        [activeCatch is not null && caughtExceptions.TryGetValue(activeCatch, out var caughtException)
                            ? builder.BuildLoad2(pointerType, caughtException)
                            : builder.BuildCall2(exceptionCurrentType, exceptionCurrentFunction, [])]);
                    builder.BuildUnreachable();
                    terminatedBlocks.Add(builder.InsertBlock);
                    return true;
                }
            case Code.Endfinally:
                {
                    var handler = method?.Body.ExceptionHandlers.FirstOrDefault(candidate =>
                        candidate.HandlerType is ExceptionHandlerType.Finally or ExceptionHandlerType.Fault &&
                        candidate.HandlerStart.Offset <= instruction.Offset &&
                        (candidate.HandlerEnd is null || instruction.Offset < candidate.HandlerEnd.Offset));
                    if (handler is not null && finallyStates.TryGetValue(handler, out var state))
                    {
                        var defaultBlock = context.AppendBasicBlock(function, $"finally.invalid.{nextVirtualDispatchId++}");
                        var switchValue = builder.BuildSwitch(builder.BuildLoad2(int32Type, state.Slot),
                            defaultBlock, (uint)state.Targets.Count);
                        foreach (var continuation in state.Targets)
                            switchValue.AddCase(LLVMValueRef.CreateConstInt(int32Type, (uint)continuation.Key, false), continuation.Value);
                        terminatedBlocks.Add(builder.InsertBlock);
                        builder.PositionAtEnd(defaultBlock);
                        builder.BuildUnreachable();
                        terminatedBlocks.Add(defaultBlock);
                    }
                    else
                    {
                        builder.BuildUnreachable();
                        terminatedBlocks.Add(builder.InsertBlock);
                    }
                    return true;
                }
            case Code.Endfilter:
                if (filterStates.TryGetValue(instruction.Offset, out var filterState))
                {
                    var result = stack.Count == 0
                        ? LLVMValueRef.CreateConstInt(int32Type, 0, false)
                        : ConvertValue(builder, stack.Pop(), int32Type);
                    builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, result,
                        LLVMValueRef.CreateConstInt(int32Type, 0, false)), filterState.Handler, filterState.Next);
                }
                else
                    builder.BuildUnreachable();
                terminatedBlocks.Add(builder.InsertBlock);
                return true;
            case Code.Leave:
            case Code.Leave_S:
                {
                    var target = (Instruction)instruction.Operand;
                    var branch = labels[target.Offset];
                    var exitedRegions = exceptionRegions
                        .Where(region => region.Start <= instruction.Offset && instruction.Offset < region.End &&
                            target.Offset != region.Start && !(target.Offset >= region.Start && target.Offset < region.End))
                        .OrderBy(region => region.End - region.Start)
                        .ToList();
                    foreach (var region in exitedRegions)
                        builder.BuildCall2(exceptionPopType, exceptionPopFunction, [region.Frame]);
                    var handlers = method?.Body.ExceptionHandlers.Where(handler =>
                            handler.HandlerType == ExceptionHandlerType.Finally &&
                            handler.TryStart.Offset <= instruction.Offset && instruction.Offset < handler.TryEnd.Offset &&
                            !(target.Offset >= handler.TryStart.Offset && target.Offset < handler.TryEnd.Offset))
                        .OrderBy(handler => handler.TryEnd.Offset - handler.TryStart.Offset).ToList() ?? [];
                    if (handlers.Count != 0)
                    {
                        var continuation = branch;
                        for (var index = handlers.Count - 1; index >= 0; index--)
                        {
                            registerFinallyContinuation(handlers[index], continuation);
                            continuation = labels[handlers[index].HandlerStart.Offset];
                        }
                        builder.BuildBr(continuation);
                    }
                    else
                    {
                        saveStack(branch);
                        builder.BuildBr(branch);
                    }
                    terminatedBlocks.Add(builder.InsertBlock);
                    return true;
                }
            default:
                return false;
        }
    }
}
