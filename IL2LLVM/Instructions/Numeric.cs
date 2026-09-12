sealed class Numeric(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateNumericInstruction(LLVMBuilderRef builder, Instruction instruction, Stack<LLVMValueRef> stack,
        Func<Code, LLVMValueRef, LLVMValueRef, LLVMValueRef> buildCheckedIntegerArithmetic,
        Action<LLVMValueRef, TypeReference> emitConditionalException)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ceq:
            case Code.Cgt:
            case Code.Cgt_Un:
            case Code.Clt:
            case Code.Clt_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(builder.BuildZExt(BuildComparison(builder, instruction.OpCode.Code, left, right), int32Type));
                    return true;
                }
            case Code.Sub:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var result = left.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                        ? builder.BuildGEP2(int8Type, left, [builder.BuildNeg(ConvertValue(builder, right, sizeType, true))])
                        : NormalizeBinaryOperands(builder, left, right) is var operands
                            ? IsFloatingValue(operands.Left)
                                ? builder.BuildFSub(operands.Left, operands.Right)
                                : builder.BuildSub(operands.Left, operands.Right)
                            : default;
                    stack.Push(result);
                    return true;
                }
            case Code.Add:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var result = left.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                        ? builder.BuildGEP2(int8Type, left, [ConvertValue(builder, right, sizeType, true)])
                        : right.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
                            ? builder.BuildGEP2(int8Type, right, [ConvertValue(builder, left, sizeType, true)])
                            : NormalizeBinaryOperands(builder, left, right) is var operands
                                ? IsFloatingValue(operands.Left)
                                    ? builder.BuildFAdd(operands.Left, operands.Right)
                                    : builder.BuildAdd(operands.Left, operands.Right)
                                : default;
                    stack.Push(result);
                    return true;
                }
            case Code.Add_Ovf:
            case Code.Add_Ovf_Un:
            case Code.Sub_Ovf:
            case Code.Sub_Ovf_Un:
            case Code.Mul_Ovf:
            case Code.Mul_Ovf_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(buildCheckedIntegerArithmetic(instruction.OpCode.Code, left, right));
                    return true;
                }
            case Code.Mul:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    stack.Push(IsFloatingValue(operands.Left)
                        ? builder.BuildFMul(operands.Left, operands.Right)
                        : builder.BuildMul(operands.Left, operands.Right));
                    return true;
                }
            case Code.Div:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    if (!IsFloatingValue(operands.Left))
                    {
                        emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, operands.Right,
                            LLVMValueRef.CreateConstInt(operands.Right.TypeOf, 0, false)), coreLib.DivideByZeroException);
                        var minimum = LLVMValueRef.CreateConstInt(operands.Left.TypeOf,
                            1UL << ((int)operands.Left.TypeOf.IntWidth - 1), false);
                        var negativeOne = LLVMValueRef.CreateConstInt(operands.Right.TypeOf, ulong.MaxValue, true);
                        emitConditionalException(builder.BuildAnd(
                            builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, operands.Left, minimum),
                            builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, operands.Right, negativeOne)),
                            coreLib.OverflowException);
                    }
                    stack.Push(IsFloatingValue(operands.Left)
                        ? builder.BuildFDiv(operands.Left, operands.Right)
                        : builder.BuildSDiv(operands.Left, operands.Right));
                    return true;
                }
            case Code.Div_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    if (!IsFloatingValue(operands.Left))
                        emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, operands.Right,
                            LLVMValueRef.CreateConstInt(operands.Right.TypeOf, 0, false)), coreLib.DivideByZeroException);
                    stack.Push(IsFloatingValue(operands.Left)
                        ? builder.BuildFDiv(operands.Left, operands.Right)
                        : builder.BuildUDiv(operands.Left, operands.Right));
                    return true;
                }
            case Code.Rem:
            case Code.Rem_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    if (!IsFloatingValue(operands.Left))
                        emitConditionalException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, operands.Right,
                            LLVMValueRef.CreateConstInt(operands.Right.TypeOf, 0, false)), coreLib.DivideByZeroException);
                    stack.Push(IsFloatingValue(operands.Left)
                        ? builder.BuildFRem(operands.Left, operands.Right)
                        : instruction.OpCode.Code == Code.Rem
                            ? builder.BuildSRem(operands.Left, operands.Right)
                            : builder.BuildURem(operands.Left, operands.Right));
                    return true;
                }
            case Code.And:
            case Code.Or:
            case Code.Xor:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    stack.Push(instruction.OpCode.Code switch
                    {
                        Code.And => builder.BuildAnd(operands.Left, operands.Right),
                        Code.Or => builder.BuildOr(operands.Left, operands.Right),
                        _ => builder.BuildXor(operands.Left, operands.Right)
                    });
                    return true;
                }
            case Code.Shl:
            case Code.Shr:
            case Code.Shr_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var operands = NormalizeBinaryOperands(builder, left, right);
                    var shift = builder.BuildAnd(operands.Right, LLVMValueRef.CreateConstInt(operands.Right.TypeOf,
                        (ulong)(operands.Left.TypeOf.IntWidth - 1), false));
                    stack.Push(instruction.OpCode.Code switch
                    {
                        Code.Shl => builder.BuildShl(operands.Left, shift),
                        Code.Shr => builder.BuildAShr(operands.Left, shift),
                        _ => builder.BuildLShr(operands.Left, shift)
                    });
                    return true;
                }
            case Code.Neg:
                {
                    var value = stack.Pop();
                    stack.Push(IsFloatingValue(value) ? builder.BuildFNeg(value) : builder.BuildNeg(value));
                    return true;
                }
            case Code.Not:
                stack.Push(builder.BuildNot(stack.Pop()));
                return true;
            case Code.Conv_I1:
            case Code.Conv_Ovf_I1:
            case Code.Conv_Ovf_I1_Un:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int8Type), int32Type));
                return true;
            case Code.Conv_U1:
            case Code.Conv_Ovf_U1:
            case Code.Conv_Ovf_U1_Un:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int8Type, false), int32Type, false));
                return true;
            case Code.Conv_I2:
            case Code.Conv_Ovf_I2:
            case Code.Conv_Ovf_I2_Un:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int16Type), int32Type));
                return true;
            case Code.Conv_U2:
            case Code.Conv_Ovf_U2:
            case Code.Conv_Ovf_U2_Un:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int16Type, false), int32Type, false));
                return true;
            case Code.Conv_I4:
            case Code.Conv_Ovf_I4:
            case Code.Conv_Ovf_I4_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), int32Type));
                return true;
            case Code.Conv_U4:
            case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_U4_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), int32Type, false));
                return true;
            case Code.Conv_I8:
            case Code.Conv_Ovf_I8:
            case Code.Conv_Ovf_I8_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), int64Type));
                return true;
            case Code.Conv_U8:
            case Code.Conv_Ovf_U8:
            case Code.Conv_Ovf_U8_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), int64Type, false));
                return true;
            case Code.Conv_I:
            case Code.Conv_Ovf_I:
            case Code.Conv_Ovf_I_Un:
            case Code.Conv_U:
            case Code.Conv_Ovf_U:
            case Code.Conv_Ovf_U_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), sizeType,
                    instruction.OpCode.Code is not (Code.Conv_U or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un)));
                return true;
            case Code.Conv_R4:
                stack.Push(ConvertValue(builder, stack.Pop(), floatType));
                return true;
            case Code.Conv_R8:
                stack.Push(ConvertValue(builder, stack.Pop(), doubleType));
                return true;
            case Code.Conv_R_Un:
                stack.Push(ConvertValue(builder, stack.Pop(), doubleType, false));
                return true;
            default:
                return false;
        }
    }
}
