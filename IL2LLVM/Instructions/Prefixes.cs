sealed class Prefixes(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslatePrefixInstruction(MethodContext methodContext, Instruction instruction, ref uint unalignedAlignment)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Nop:
            case Code.Break:
            case Code.No:
            case Code.Volatile:
            case Code.Readonly:
            case Code.Tail:
                return true;
            case Code.Unaligned:
                unalignedAlignment = Convert.ToUInt32(instruction.Operand);
                if (unalignedAlignment is not (1 or 2 or 4))
                    throw new InvalidOperationException($"Unsupported unaligned prefix value: {unalignedAlignment}.");
                return true;
            case Code.Constrained:
                methodContext.ConstrainedType = SubstituteGenericParameter((TypeReference)instruction.Operand,
                    methodContext.Method);
                return true;
            default:
                return false;
        }
    }
}
