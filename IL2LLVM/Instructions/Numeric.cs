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
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int8Type), int32Type));
                return true;
            case Code.Conv_U1:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int8Type, false), int32Type, false));
                return true;
            case Code.Conv_I2:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int16Type), int32Type));
                return true;
            case Code.Conv_U2:
                stack.Push(ConvertValue(builder, ConvertValue(builder, stack.Pop(), int16Type, false), int32Type, false));
                return true;
            case Code.Conv_I4:
                stack.Push(ConvertValue(builder, stack.Pop(), int32Type));
                return true;
            case Code.Conv_U4:
                stack.Push(ConvertValue(builder, stack.Pop(), int32Type, false));
                return true;
            case Code.Conv_I8:
                stack.Push(ConvertValue(builder, stack.Pop(), int64Type));
                return true;
            case Code.Conv_U8:
                stack.Push(ConvertValue(builder, stack.Pop(), int64Type, false));
                return true;
            case Code.Conv_I:
            case Code.Conv_U:
                stack.Push(ConvertValue(builder, stack.Pop(), sizeType,
                    instruction.OpCode.Code != Code.Conv_U));
                return true;
            case Code.Conv_Ovf_I1:
            case Code.Conv_Ovf_I1_Un:
            case Code.Conv_Ovf_U1:
            case Code.Conv_Ovf_U1_Un:
            case Code.Conv_Ovf_I2:
            case Code.Conv_Ovf_I2_Un:
            case Code.Conv_Ovf_U2:
            case Code.Conv_Ovf_U2_Un:
            case Code.Conv_Ovf_I4:
            case Code.Conv_Ovf_I4_Un:
            case Code.Conv_Ovf_U4:
            case Code.Conv_Ovf_U4_Un:
            case Code.Conv_Ovf_I8:
            case Code.Conv_Ovf_I8_Un:
            case Code.Conv_Ovf_U8:
            case Code.Conv_Ovf_U8_Un:
            case Code.Conv_Ovf_I:
            case Code.Conv_Ovf_I_Un:
            case Code.Conv_Ovf_U:
            case Code.Conv_Ovf_U_Un:
                stack.Push(BuildCheckedConversion(builder, stack.Pop(), instruction.OpCode.Code,
                    emitConditionalException));
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

    LLVMValueRef BuildCheckedConversion(LLVMBuilderRef builder, LLVMValueRef value, Code code,
        Action<LLVMValueRef, TypeReference> emitConditionalException)
    {
        var targetSigned = code is Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un or
            Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un or
            Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un or Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un;
        var sourceUnsigned = code is Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_U1_Un or
            Code.Conv_Ovf_I2_Un or Code.Conv_Ovf_U2_Un or Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_U4_Un or
            Code.Conv_Ovf_I8_Un or Code.Conv_Ovf_U8_Un or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U_Un;
        var targetType = code switch
        {
            Code.Conv_Ovf_I1 or Code.Conv_Ovf_I1_Un or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U1_Un => int8Type,
            Code.Conv_Ovf_I2 or Code.Conv_Ovf_I2_Un or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U2_Un => int16Type,
            Code.Conv_Ovf_I4 or Code.Conv_Ovf_I4_Un or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U4_Un => int32Type,
            Code.Conv_Ovf_I8 or Code.Conv_Ovf_I8_Un or Code.Conv_Ovf_U8 or Code.Conv_Ovf_U8_Un => int64Type,
            _ => sizeType
        };
        var targetBits = (int)targetType.IntWidth;
        LLVMValueRef overflow;

        if (IsFloatingValue(value))
        {
            var upper = targetSigned ? Math.Pow(2, targetBits - 1) : Math.Pow(2, targetBits);
            var useInclusiveMinimum = targetSigned &&
                (targetBits == 64 || targetBits == 32 && value.TypeOf.Kind == LLVMTypeKind.LLVMFloatTypeKind);
            var lower = targetSigned
                ? useInclusiveMinimum
                    ? -Math.Pow(2, targetBits - 1)
                    : -Math.Pow(2, targetBits - 1) - 1
                : -1;
            var belowMinimum = builder.BuildFCmp(
                useInclusiveMinimum ? LLVMRealPredicate.LLVMRealOLT : LLVMRealPredicate.LLVMRealOLE,
                value, LLVMValueRef.CreateConstReal(value.TypeOf, lower));
            overflow = builder.BuildOr(
                builder.BuildFCmp(LLVMRealPredicate.LLVMRealUNO, value, value),
                builder.BuildOr(
                    belowMinimum,
                    builder.BuildFCmp(LLVMRealPredicate.LLVMRealOGE, value,
                        LLVMValueRef.CreateConstReal(value.TypeOf, upper))));
        }
        else
        {
            if (value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                value = builder.BuildPtrToInt(value, sizeType);
            var sourceBits = (int)value.TypeOf.IntWidth;
            overflow = LLVMValueRef.CreateConstInt(int1Type, 0, false);
            if (targetSigned)
            {
                if (sourceUnsigned && sourceBits >= targetBits)
                {
                    var maximum = (1UL << (targetBits - 1)) - 1;
                    overflow = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, value,
                        LLVMValueRef.CreateConstInt(value.TypeOf, maximum, false));
                }
                else if (!sourceUnsigned && sourceBits > targetBits)
                {
                    var minimum = unchecked((ulong)(-(1L << (targetBits - 1))));
                    var maximum = (1UL << (targetBits - 1)) - 1;
                    overflow = builder.BuildOr(
                        builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, value,
                            LLVMValueRef.CreateConstInt(value.TypeOf, minimum, true)),
                        builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, value,
                            LLVMValueRef.CreateConstInt(value.TypeOf, maximum, true)));
                }
            }
            else if (sourceUnsigned)
            {
                if (sourceBits > targetBits)
                {
                    var maximum = targetBits == 64 ? ulong.MaxValue : (1UL << targetBits) - 1;
                    overflow = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, value,
                        LLVMValueRef.CreateConstInt(value.TypeOf, maximum, false));
                }
            }
            else
            {
                overflow = builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, value,
                    LLVMValueRef.CreateConstInt(value.TypeOf, 0, false));
                if (sourceBits > targetBits)
                {
                    var maximum = targetBits == 64 ? ulong.MaxValue : (1UL << targetBits) - 1;
                    overflow = builder.BuildOr(overflow,
                        builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, value,
                            LLVMValueRef.CreateConstInt(value.TypeOf, maximum, false)));
                }
            }
        }

        emitConditionalException(overflow, coreLib.OverflowException);
        var converted = IsFloatingValue(value)
            ? targetSigned ? builder.BuildFPToSI(value, targetType) : builder.BuildFPToUI(value, targetType)
            : ConvertValue(builder, value, targetType, !sourceUnsigned);
        return targetBits < 32
            ? ConvertValue(builder, converted, int32Type, targetSigned)
            : converted;
    }
}
