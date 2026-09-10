sealed class Translator
{
    internal static readonly object initializationLock = new();
    internal static bool llvmInitialized;
    internal LLVMContextRef context;
    internal LLVMModuleRef module;
    internal LLVMTargetMachineRef machine;
    internal int pointerSize;
    internal LLVMTypeRef int1Type;
    internal LLVMTypeRef int8Type;
    internal LLVMTypeRef int16Type;
    internal LLVMTypeRef int32Type;
    internal LLVMTypeRef int64Type;
    internal LLVMTypeRef floatType;
    internal LLVMTypeRef doubleType;
    internal LLVMTypeRef voidType;
    internal LLVMTypeRef sizeType;
    internal Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>> moduleMethods = new();
    internal Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef>> staticFields = new();
    internal Dictionary<string, TypeReference> staticFieldTypes = new(StringComparer.Ordinal);
    internal Dictionary<string, (LLVMValueRef Function, LLVMValueRef State)> cctorGuards = new(StringComparer.Ordinal);
    internal Dictionary<string, MethodDefinition> localMethods = new(StringComparer.Ordinal);
    internal Dictionary<string, TypeDefinition> localTypes = new(StringComparer.Ordinal);
    internal CoreLibMetadata coreLib = null!;
    internal Dictionary<string, int> runtimeTypeIds = new(StringComparer.Ordinal);
    internal Dictionary<string, TypeReference> runtimeTypes = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> runtimeTypeObjects = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> staticStrings = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> staticStringArrays = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> staticUInt64Arrays = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> gcDescriptors = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> runtimeFieldData = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> missingVirtualFunctionPointers = new(StringComparer.Ordinal);
    internal Dictionary<string, LLVMValueRef> delegateThunks = new(StringComparer.Ordinal);
    internal List<TypeDefinition> arrayEnumeratorTypes = [];
    internal MethodDefinition? entryPoint;
    internal MethodDefinition stringConstructor = null!;
    internal LLVMTypeRef gcAllocateType;
    internal LLVMValueRef gcAllocateFunction;
    internal int nextRuntimeTypeId;
    internal int nextVirtualDispatchId;

    internal InstructionHelpers InstructionHelpers { get; }
    internal TypeSystem TypeSystem { get; }
    internal Methods Methods { get; }
    internal Runtime Runtime { get; }
    readonly Compilation compilation;

    public Translator()
    {
        InstructionHelpers = new(this);
        TypeSystem = new(this);
        Methods = new(this);
        Runtime = new(this);
        compilation = new(this);
    }

    public void Translate(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Expected an input file, output file, and target triple.");

        Target.InitializeLLVM();
        compilation.TranslateModule(args);
    }
}
