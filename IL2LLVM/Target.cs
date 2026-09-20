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
            ConfigureCodeGeneration();
            Translator.llvmInitialized = true;
        }
    }

    internal static (string TargetTriple, LLVMCodeModel CodeModel, string Cpu, string Features) ParseTargetSpecification(string target)
    {
        var specification = target.Split(';', StringSplitOptions.TrimEntries);
        if (specification.Length is < 1 or > 4 || string.IsNullOrEmpty(specification[0]))
            throw new ArgumentException("Target must be a target triple optionally followed by a code model, CPU, and target features.");
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
        var cpu = specification.Length >= 3 && !string.IsNullOrEmpty(specification[2])
            ? specification[2]
            : "generic";
        var features = specification.Length >= 4 ? specification[3] : "";
        return (specification[0], codeModel, cpu, features);
    }

    internal static LLVMTargetMachineRef CreateTargetMachine(string targetTriple, LLVMCodeModel codeModel,
                                                              string cpu, string features)
    {
        var target = LLVMTargetRef.GetTargetFromTriple(targetTriple);
        return target.CreateTargetMachine(targetTriple, cpu, features,
            LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            codeModel == LLVMCodeModel.LLVMCodeModelKernel ? LLVMRelocMode.LLVMRelocStatic : LLVMRelocMode.LLVMRelocPIC,
            codeModel);
    }

    static unsafe void ConfigureCodeGeneration()
    {
        ReadOnlySpan<byte> programName = "IL2LLVM\0"u8;
        ReadOnlySpan<byte> registerAllocator = "-regalloc=basic\0"u8;
        fixed (byte* programNamePointer = programName)
        fixed (byte* registerAllocatorPointer = registerAllocator)
        {
            sbyte** arguments = stackalloc sbyte*[2];
            arguments[0] = (sbyte*)programNamePointer;
            arguments[1] = (sbyte*)registerAllocatorPointer;
            LLVM.ParseCommandLineOptions(2, arguments, null);
        }
    }

}
