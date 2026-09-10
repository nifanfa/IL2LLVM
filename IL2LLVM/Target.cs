static class Target
{
    internal static void InitializeLLVM()
    {
        lock (Translator.initializationLock)
        {
            if (Translator.llvmInitialized)
                return;
            LLVM.InitializeAllTargetInfos();
            LLVM.InitializeAllTargets();
            LLVM.InitializeAllTargetMCs();
            LLVM.InitializeAllAsmParsers();
            LLVM.InitializeAllAsmPrinters();
            Translator.llvmInitialized = true;
        }
    }

    internal static (string TargetTriple, LLVMCodeModel CodeModel) ParseTargetSpecification(string target)
    {
        var specification = target.Split(';', StringSplitOptions.TrimEntries);
        if (specification.Length is < 1 or > 2 || string.IsNullOrEmpty(specification[0]))
            throw new ArgumentException("Target must be a target triple optionally followed by a code model.");
        var codeModel = specification.Length == 1 ? LLVMCodeModel.LLVMCodeModelDefault : specification[1] switch
        {
            "default" => LLVMCodeModel.LLVMCodeModelDefault,
            "tiny" => LLVMCodeModel.LLVMCodeModelTiny,
            "small" => LLVMCodeModel.LLVMCodeModelSmall,
            "kernel" => LLVMCodeModel.LLVMCodeModelKernel,
            "medium" => LLVMCodeModel.LLVMCodeModelMedium,
            "large" => LLVMCodeModel.LLVMCodeModelLarge,
            _ => throw new ArgumentException($"Unsupported LLVM code model '{specification[1]}'.")
        };
        return (specification[0], codeModel);
    }

    internal static LLVMTargetMachineRef CreateTargetMachine(string targetTriple, LLVMCodeModel codeModel)
    {
        var target = LLVMTargetRef.GetTargetFromTriple(targetTriple);
        return target.CreateTargetMachine(targetTriple, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            codeModel == LLVMCodeModel.LLVMCodeModelKernel ? LLVMRelocMode.LLVMRelocStatic : LLVMRelocMode.LLVMRelocPIC,
            codeModel);
    }

}
