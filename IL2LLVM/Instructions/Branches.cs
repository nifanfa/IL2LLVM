sealed class Branches(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateBranchInstruction(LLVMBuilderRef builder, Instruction instruction, System.Collections.Generic.Stack<LLVMValueRef> stack,
        SortedDictionary<int, LLVMBasicBlockRef> labels, HashSet<LLVMBasicBlockRef> terminatedBlocks,
        Action<LLVMBasicBlockRef> saveStack, Action<LLVMBasicBlockRef> restoreStack)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Switch:
                {
                    var value = stack.Pop();
                    var targets = (Instruction[])instruction.Operand;
                    var defaultBlock = instruction.Next is null ? labels[targets[0].Offset] : labels[instruction.Next.Offset];
                    var source = builder.InsertBlock;
                    foreach (var target in targets)
                        saveStack(labels[target.Offset]);
                    saveStack(defaultBlock);
                    var switchValue = builder.BuildSwitch(value, defaultBlock, (uint)targets.Length);
                    for (uint index = 0; index < targets.Length; index++)
                        switchValue.AddCase(LLVMValueRef.CreateConstInt(value.TypeOf, index, false), labels[targets[index].Offset]);
                    if (instruction.Next is not null)
                    {
                        builder.PositionAtEnd(defaultBlock);
                        restoreStack(defaultBlock);
                    }
                    terminatedBlocks.Add(source);
                    return true;
                }
            case Code.Beq:
            case Code.Beq_S:
            case Code.Bge:
            case Code.Bge_S:
            case Code.Bge_Un:
            case Code.Bge_Un_S:
            case Code.Bgt:
            case Code.Bgt_S:
            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
            case Code.Ble:
            case Code.Ble_S:
            case Code.Ble_Un:
            case Code.Ble_Un_S:
            case Code.Blt:
            case Code.Blt_S:
            case Code.Blt_Un:
            case Code.Blt_Un_S:
            case Code.Bne_Un:
            case Code.Bne_Un_S:
            case Code.Br:
            case Code.Br_S:
            case Code.Brfalse:
            case Code.Brfalse_S:
            case Code.Brtrue:
            case Code.Brtrue_S:
                {
                    var target = (Instruction)instruction.Operand;
                    if (instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S)
                    {
                        var value = stack.Pop();
                        var condition = builder.BuildICmp(
                            instruction.OpCode.Code is Code.Brtrue or Code.Brtrue_S
                                ? LLVMIntPredicate.LLVMIntNE
                                : LLVMIntPredicate.LLVMIntEQ,
                            value, LLVMValueRef.CreateConstNull(value.TypeOf));
                        var fallthrough = instruction.Next is null ? labels[target.Offset] : labels[instruction.Next.Offset];
                        var branch = labels[target.Offset];
                        saveStack(branch);
                        saveStack(fallthrough);
                        builder.BuildCondBr(condition, branch, fallthrough);
                        terminatedBlocks.Add(builder.InsertBlock);
                        if (instruction.Next is not null)
                        {
                            builder.PositionAtEnd(fallthrough);
                            restoreStack(fallthrough);
                        }
                        return true;
                    }
                    if (instruction.OpCode.Code is Code.Br or Code.Br_S)
                    {
                        var branch = labels[target.Offset];
                        saveStack(branch);
                        builder.BuildBr(branch);
                        terminatedBlocks.Add(builder.InsertBlock);
                        return true;
                    }
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var comparison = BuildComparison(builder, instruction.OpCode.Code, left, right);
                    var comparisonFallthrough = instruction.Next is null ? labels[target.Offset] : labels[instruction.Next.Offset];
                    saveStack(labels[target.Offset]);
                    saveStack(comparisonFallthrough);
                    builder.BuildCondBr(comparison, labels[target.Offset], comparisonFallthrough);
                    terminatedBlocks.Add(builder.InsertBlock);
                    if (instruction.Next is not null)
                    {
                        builder.PositionAtEnd(comparisonFallthrough);
                        restoreStack(comparisonFallthrough);
                    }
                    return true;
                }
            default:
                return false;
        }
    }
}
