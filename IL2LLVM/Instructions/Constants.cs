sealed class Constants(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateConstantInstruction(LLVMBuilderRef builder, Instruction instruction, System.Collections.Generic.Stack<LLVMValueRef> stack)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_I4_M1:
                stack.Push(LLVMValueRef.CreateConstInt(int32Type, unchecked((ulong)-1), true));
                return true;
            case Code.Ldc_I4_0:
            case Code.Ldc_I4_1:
            case Code.Ldc_I4_2:
            case Code.Ldc_I4_3:
            case Code.Ldc_I4_4:
            case Code.Ldc_I4_5:
            case Code.Ldc_I4_6:
            case Code.Ldc_I4_7:
            case Code.Ldc_I4_8:
            case Code.Ldc_I4:
            case Code.Ldc_I4_S:
                stack.Push(LLVMValueRef.CreateConstInt(int32Type, (ulong)(instruction.OpCode.Code switch
                {
                    Code.Ldc_I4_0 => 0,
                    Code.Ldc_I4_1 => 1,
                    Code.Ldc_I4_2 => 2,
                    Code.Ldc_I4_3 => 3,
                    Code.Ldc_I4_4 => 4,
                    Code.Ldc_I4_5 => 5,
                    Code.Ldc_I4_6 => 6,
                    Code.Ldc_I4_7 => 7,
                    Code.Ldc_I4_8 => 8,
                    Code.Ldc_I4 => (int)instruction.Operand,
                    Code.Ldc_I4_S => (sbyte)instruction.Operand,
                    _ => throw new InvalidOperationException(instruction.OpCode.Code.ToString())
                })));
                return true;
            case Code.Ldc_I8:
                stack.Push(LLVMValueRef.CreateConstInt(int64Type, unchecked((ulong)(long)instruction.Operand), true));
                return true;
            case Code.Ldc_R4:
                stack.Push(LLVMValueRef.CreateConstReal(floatType, (float)instruction.Operand));
                return true;
            case Code.Ldc_R8:
                stack.Push(LLVMValueRef.CreateConstReal(doubleType, (double)instruction.Operand));
                return true;
            case Code.Ldnull:
                stack.Push(LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(int8Type, 0)));
                return true;
            default:
                return false;
        }
    }
}
