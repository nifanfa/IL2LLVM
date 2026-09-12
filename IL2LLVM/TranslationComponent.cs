abstract class TranslationComponent(Translator translator)
{
    protected Translator Translator { get; } = translator;
    protected LLVMContextRef context { get => Translator.context; set => Translator.context = value; }
    protected LLVMModuleRef module { get => Translator.module; set => Translator.module = value; }
    protected LLVMTargetMachineRef machine { get => Translator.machine; set => Translator.machine = value; }
    protected int pointerSize { get => Translator.pointerSize; set => Translator.pointerSize = value; }
    protected LLVMTypeRef int1Type { get => Translator.int1Type; set => Translator.int1Type = value; }
    protected LLVMTypeRef int8Type { get => Translator.int8Type; set => Translator.int8Type = value; }
    protected LLVMTypeRef int16Type { get => Translator.int16Type; set => Translator.int16Type = value; }
    protected LLVMTypeRef int32Type { get => Translator.int32Type; set => Translator.int32Type = value; }
    protected LLVMTypeRef int64Type { get => Translator.int64Type; set => Translator.int64Type = value; }
    protected LLVMTypeRef floatType { get => Translator.floatType; set => Translator.floatType = value; }
    protected LLVMTypeRef doubleType { get => Translator.doubleType; set => Translator.doubleType = value; }
    protected LLVMTypeRef voidType { get => Translator.voidType; set => Translator.voidType = value; }
    protected LLVMTypeRef sizeType { get => Translator.sizeType; set => Translator.sizeType = value; }
    protected Dictionary<string, LLVMTypeRef> llvmTypeCache { get => Translator.llvmTypeCache; set => Translator.llvmTypeCache = value; }
    protected Dictionary<string, int> typeSizeCache { get => Translator.typeSizeCache; set => Translator.typeSizeCache = value; }
    protected Dictionary<string, int> typeAlignmentCache { get => Translator.typeAlignmentCache; set => Translator.typeAlignmentCache = value; }
    protected Dictionary<string, int> objectSizeCache { get => Translator.objectSizeCache; set => Translator.objectSizeCache = value; }
    protected Dictionary<string, int> typeDefinitionSizeCache { get => Translator.typeDefinitionSizeCache; set => Translator.typeDefinitionSizeCache = value; }
    protected Dictionary<string, int> typeDefinitionAlignmentCache { get => Translator.typeDefinitionAlignmentCache; set => Translator.typeDefinitionAlignmentCache = value; }
    protected Dictionary<string, TypeReference?> enumUnderlyingTypeCache { get => Translator.enumUnderlyingTypeCache; set => Translator.enumUnderlyingTypeCache = value; }
    protected Dictionary<string, bool> valueTypeCache { get => Translator.valueTypeCache; set => Translator.valueTypeCache = value; }
    protected Dictionary<string, bool> byReferenceValueCache { get => Translator.byReferenceValueCache; set => Translator.byReferenceValueCache = value; }
    protected Dictionary<string, bool> managedReferenceTypeCache { get => Translator.managedReferenceTypeCache; set => Translator.managedReferenceTypeCache = value; }
    protected Dictionary<string, MethodDefinition?> methodDefinitionCache { get => Translator.methodDefinitionCache; set => Translator.methodDefinitionCache = value; }
    protected Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>> moduleMethods { get => Translator.moduleMethods; set => Translator.moduleMethods = value; }
    protected Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef>> staticFields { get => Translator.staticFields; set => Translator.staticFields = value; }
    protected Dictionary<string, TypeReference> staticFieldTypes { get => Translator.staticFieldTypes; set => Translator.staticFieldTypes = value; }
    protected Dictionary<string, (LLVMValueRef Function, LLVMValueRef State)> cctorGuards { get => Translator.cctorGuards; set => Translator.cctorGuards = value; }
    protected Dictionary<string, MethodDefinition> localMethods { get => Translator.localMethods; set => Translator.localMethods = value; }
    protected Dictionary<string, TypeDefinition> localTypes { get => Translator.localTypes; set => Translator.localTypes = value; }
    protected CoreLibMetadata coreLib { get => Translator.coreLib; set => Translator.coreLib = value; }
    protected Dictionary<string, int> runtimeTypeIds { get => Translator.runtimeTypeIds; set => Translator.runtimeTypeIds = value; }
    protected Dictionary<string, TypeReference> runtimeTypes { get => Translator.runtimeTypes; set => Translator.runtimeTypes = value; }
    protected Dictionary<string, LLVMValueRef> runtimeTypeObjects { get => Translator.runtimeTypeObjects; set => Translator.runtimeTypeObjects = value; }
    protected Dictionary<string, LLVMValueRef> staticStrings { get => Translator.staticStrings; set => Translator.staticStrings = value; }
    protected Dictionary<string, LLVMValueRef> staticStringArrays { get => Translator.staticStringArrays; set => Translator.staticStringArrays = value; }
    protected Dictionary<string, LLVMValueRef> staticUInt64Arrays { get => Translator.staticUInt64Arrays; set => Translator.staticUInt64Arrays = value; }
    protected Dictionary<string, LLVMValueRef> gcDescriptors { get => Translator.gcDescriptors; set => Translator.gcDescriptors = value; }
    protected Dictionary<string, LLVMValueRef> gcReferenceLists { get => Translator.gcReferenceLists; set => Translator.gcReferenceLists = value; }
    protected Dictionary<string, LLVMValueRef> runtimeFieldData { get => Translator.runtimeFieldData; set => Translator.runtimeFieldData = value; }
    protected Dictionary<string, LLVMValueRef> missingVirtualFunctionPointers { get => Translator.missingVirtualFunctionPointers; set => Translator.missingVirtualFunctionPointers = value; }
    protected Dictionary<string, LLVMValueRef> delegateThunks { get => Translator.delegateThunks; set => Translator.delegateThunks = value; }
    protected Queue<string> pendingMethodTranslations { get => Translator.pendingMethodTranslations; set => Translator.pendingMethodTranslations = value; }
    protected HashSet<string> queuedMethodTranslations { get => Translator.queuedMethodTranslations; set => Translator.queuedMethodTranslations = value; }
    protected bool queueMethodTranslations { get => Translator.queueMethodTranslations; set => Translator.queueMethodTranslations = value; }
    protected void QueueMethodTranslation(string friendlyName) => Translator.Methods.QueueMethodTranslation(friendlyName);
    protected void QueueMethodTranslation(MethodReference method) => Translator.Methods.QueueMethodTranslation(method);
    protected List<TypeDefinition> arrayEnumeratorTypes { get => Translator.arrayEnumeratorTypes; set => Translator.arrayEnumeratorTypes = value; }
    protected MethodDefinition? entryPoint { get => Translator.entryPoint; set => Translator.entryPoint = value; }
    protected MethodDefinition stringConstructor { get => Translator.stringConstructor; set => Translator.stringConstructor = value; }
    protected LLVMTypeRef gcAllocateType { get => Translator.gcAllocateType; set => Translator.gcAllocateType = value; }
    protected LLVMValueRef gcAllocateFunction { get => Translator.gcAllocateFunction; set => Translator.gcAllocateFunction = value; }
    protected int nextRuntimeTypeId { get => Translator.nextRuntimeTypeId; set => Translator.nextRuntimeTypeId = value; }
    protected int nextVirtualDispatchId { get => Translator.nextVirtualDispatchId; set => Translator.nextVirtualDispatchId = value; }

    protected LLVMValueRef ConvertValue(LLVMBuilderRef builder, LLVMValueRef value, LLVMTypeRef target, bool signed = true) => Translator.InstructionHelpers.ConvertValue(builder, value, target, signed);
    protected LLVMValueRef PromoteSmallIntegerLoad(LLVMBuilderRef builder, LLVMValueRef value, TypeReference type) => Translator.InstructionHelpers.PromoteSmallIntegerLoad(builder, value, type);
    protected bool IsFloatingValue(LLVMValueRef value) => Translator.InstructionHelpers.IsFloatingValue(value);
    protected (LLVMValueRef Left, LLVMValueRef Right) NormalizeBinaryOperands(LLVMBuilderRef builder, LLVMValueRef left, LLVMValueRef right) => Translator.InstructionHelpers.NormalizeBinaryOperands(builder, left, right);
    protected LLVMValueRef BuildComparison(LLVMBuilderRef builder, Code code, LLVMValueRef left, LLVMValueRef right) => Translator.InstructionHelpers.BuildComparison(builder, code, left, right);
    protected string GetLabelName(Instruction instruction) => Translator.InstructionHelpers.GetLabelName(instruction);
    protected int GetMetadataTypeSize(MetadataType type) => Translator.InstructionHelpers.GetMetadataTypeSize(type);
    protected LLVMTypeRef GetLLVMTypeRefFromMetadataType(MetadataType type) => Translator.InstructionHelpers.GetLLVMTypeRefFromMetadataType(type);
    protected int GetMethodParameterCount(MethodReference method) => Translator.InstructionHelpers.GetMethodParameterCount(method);

    protected List<(TypeReference RuntimeType, MethodReference Implementation)> GetVirtualImplementations(MethodReference targetMethod, TypeReference contractType) => Translator.TypeSystem.GetVirtualImplementations(targetMethod, contractType);
    protected bool IsKnownRuntimeType(TypeReference type) => Translator.TypeSystem.IsKnownRuntimeType(type);
    protected int GetTypeDepth(TypeReference type) => Translator.TypeSystem.GetTypeDepth(type);
    protected bool ContainsGenericParameter(TypeReference type) => Translator.TypeSystem.ContainsGenericParameter(type);
    protected bool IsDelegateType(TypeReference type) => Translator.TypeSystem.IsDelegateType(type);
    protected LLVMTypeRef GetLLVMTypeRef(TypeReference type) => Translator.TypeSystem.GetLLVMTypeRef(type);
    protected LLVMTypeRef GetUnmanagedCallType(TypeReference type) => Translator.TypeSystem.GetUnmanagedCallType(type);
    protected bool IsVoidType(TypeReference type) => Translator.TypeSystem.IsVoidType(type);
    protected TypeReference? GetEnumUnderlyingType(TypeReference type) => Translator.TypeSystem.GetEnumUnderlyingType(type);
    protected bool IsNoReturnMethod(MethodReference method, Dictionary<string, MethodDefinition> methods) => Translator.TypeSystem.IsNoReturnMethod(method, methods);
    protected MethodDefinition? FindLocalMethod(MethodReference reference, Dictionary<string, MethodDefinition> methods) => Translator.TypeSystem.FindLocalMethod(reference, methods);
    protected MethodReference SpecializeMethodReference(MethodReference reference, MethodReference context) => Translator.TypeSystem.SpecializeMethodReference(reference, context);
    protected TypeReference SubstituteGenericParameter(TypeReference type, MethodReference method) => Translator.TypeSystem.SubstituteGenericParameter(type, method);
    protected TypeReference? GetMethodVariableType(MethodReference method, int index) => Translator.TypeSystem.GetMethodVariableType(method, index);
    protected FieldDefinition GetObjectTypeField() => Translator.TypeSystem.GetObjectTypeField();
    protected FieldDefinition GetTypeRuntimeTypeIdField() => Translator.TypeSystem.GetTypeRuntimeTypeIdField();
    protected FieldDefinition GetTypeGCDescriptorField() => Translator.TypeSystem.GetTypeGCDescriptorField();
    protected FieldDefinition GetEnumValueField() => Translator.TypeSystem.GetEnumValueField();
    protected int GetObjectHeaderSize() => Translator.TypeSystem.GetObjectHeaderSize();
    protected FieldDefinition GetLocalField(FieldReference field) => Translator.TypeSystem.GetLocalField(field);
    protected TypeReference SubstituteFieldType(FieldReference field, MethodReference? context = null) => Translator.TypeSystem.SubstituteFieldType(field, context);
    protected TypeReference ResolveGenericType(TypeReference type, MethodReference context) => Translator.TypeSystem.ResolveGenericType(type, context);
    protected TypeReference SubstituteGenericTypeArguments(TypeReference type, GenericInstanceType declaringType) => Translator.TypeSystem.SubstituteGenericTypeArguments(type, declaringType);
    protected int GetFieldOffset(FieldDefinition field) => Translator.TypeSystem.GetFieldOffset(field);
    protected int GetFieldOffsetForType(FieldDefinition field, TypeReference declaringType) => Translator.TypeSystem.GetFieldOffsetForType(field, declaringType);
    protected int GetBaseTypeSize(TypeReference? type) => Translator.TypeSystem.GetBaseTypeSize(type);
    protected int GetObjectSize(TypeReference type) => Translator.TypeSystem.GetObjectSize(type);
    protected int GetTypeSize(TypeReference type) => Translator.TypeSystem.GetTypeSize(type);
    protected int GetTypeDefinitionSize(TypeDefinition type) => Translator.TypeSystem.GetTypeDefinitionSize(type);
    protected int GetTypeDefinitionAlignment(TypeDefinition type) => Translator.TypeSystem.GetTypeDefinitionAlignment(type);
    protected bool IsValueType(TypeReference type) => Translator.TypeSystem.IsValueType(type);
    protected bool IsByReferenceValue(TypeReference type) => Translator.TypeSystem.IsByReferenceValue(type);
    protected bool IsManagedReferenceType(TypeReference type) => Translator.TypeSystem.IsManagedReferenceType(type);
    protected int GetTypeAlignment(TypeReference type) => Translator.TypeSystem.GetTypeAlignment(type);
    protected int AlignUp(int value, int alignment) => Translator.TypeSystem.AlignUp(value, alignment);
    protected ulong GetLLVMTypeSize(LLVMTypeRef type) => Translator.TypeSystem.GetLLVMTypeSize(type);

    protected MethodReference ResolveCallTarget(MethodReference targetMethod) => Translator.Methods.ResolveCallTarget(targetMethod);
    protected MethodReference ResolveVirtualTarget(MethodReference targetMethod, TypeReference receiverType) => Translator.Methods.ResolveVirtualTarget(targetMethod, receiverType);
    protected bool ImplementsInterface(TypeDefinition type, TypeReference interfaceType) => Translator.Methods.ImplementsInterface(type, interfaceType);
    protected bool TryCloseRuntimeType(TypeDefinition type, TypeReference contractType, out TypeReference runtimeType) => Translator.Methods.TryCloseRuntimeType(type, contractType, out runtimeType);
    protected IEnumerable<TypeReference> GetImplementedInterfaces(TypeReference type) => Translator.Methods.GetImplementedInterfaces(type);
    protected bool TryBindTypePattern(TypeReference pattern, TypeReference actual, TypeDefinition owner, Dictionary<int, TypeReference> bindings) => Translator.Methods.TryBindTypePattern(pattern, actual, owner, bindings);
    protected bool SameType(TypeReference left, TypeReference right) => Translator.Methods.SameType(left, right);
    protected bool IsOpenSelfInstantiation(GenericInstanceType type) => Translator.Methods.IsOpenSelfInstantiation(type);
    protected bool SameTypeDefinition(TypeReference left, TypeReference right) => Translator.Methods.SameTypeDefinition(left, right);
    protected bool SameMethodDefinition(MethodReference left, MethodReference right) => Translator.Methods.SameMethodDefinition(left, right);
    protected MethodDefinition? FindMethodDefinition(MethodReference method) => Translator.Methods.FindMethodDefinition(method);
    protected bool SameMethodDeclarationSignature(MethodReference left, MethodReference right) => Translator.Methods.SameMethodDeclarationSignature(left, right);
    protected bool SameMethodSignature(MethodReference left, MethodReference right) => Translator.Methods.SameMethodSignature(left, right);
    protected bool SameMethodInstantiation(MethodReference left, MethodReference right) => Translator.Methods.SameMethodInstantiation(left, right);
    protected ArrayIntrinsicKind GetArrayIntrinsicKind(MethodReference method) => Translator.Methods.GetArrayIntrinsicKind(method);
    protected bool IsDelegateConstructor(MethodReference method) => Translator.Methods.IsDelegateConstructor(method);
    protected bool IsDelegateInvoke(MethodReference method) => Translator.Methods.IsDelegateInvoke(method);
    protected MethodDefinition GetDelegateInvokeMethod(TypeReference type) => Translator.Methods.GetDelegateInvokeMethod(type);
    protected bool IsTypeInitializer(MethodReference method) => Translator.Methods.IsTypeInitializer(method);
    protected Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>? GetRegisteredMethod(MethodReference method) => Translator.Methods.GetRegisteredMethod(method);
    protected int GetGenericMethodArity(MethodReference method) => Translator.Methods.GetGenericMethodArity(method);
    protected MethodReference BindMethodToDeclaringType(MethodDefinition method, TypeReference declaringType, MethodReference? requestedMethod = null) => Translator.Methods.BindMethodToDeclaringType(method, declaringType, requestedMethod);
    protected MethodReference? FindMethodImplementation(TypeReference type, MethodReference targetMethod) => Translator.Methods.FindMethodImplementation(type, targetMethod);
    protected bool UsesValueReturnBuffer(MethodReference method) => Translator.Methods.UsesValueReturnBuffer(method);
    protected bool UsesUnmanagedSignature(MethodReference method) => Translator.Methods.UsesUnmanagedSignature(method);
    protected LLVMTypeRef CreateLLVMFunction(LLVMModuleRef llvmModule, MethodReference method) => Translator.Methods.CreateLLVMFunction(llvmModule, method);
    protected string GetFriendlyMethodName(MethodReference method, TypeReference? methodDeclareType = null) => Translator.Methods.GetFriendlyMethodName(method, methodDeclareType);
    protected string GetFriendlyTypeName(TypeReference type, bool includeGenericMarker = true) => Translator.Methods.GetFriendlyTypeName(type, includeGenericMarker);
    protected static string RemoveGenericArity(string value) => Methods.RemoveGenericArity(value);
    protected string GetFriendlyParameterTypeName(TypeReference type) => Translator.Methods.GetFriendlyParameterTypeName(type);
    protected string SanitizeSymbolPart(string value) => Translator.Methods.SanitizeSymbolPart(value);
    protected string GetStableSymbolSuffix(string value) => Translator.Methods.GetStableSymbolSuffix(value);
    protected Tuple<LLVMValueRef, LLVMTypeRef> GetStaticField(FieldReference field, MethodReference? context = null) => Translator.Methods.GetStaticField(field, context);
    protected LLVMValueRef GetRuntimeFieldHandle(LLVMBuilderRef builder, LLVMBuilderRef allocationBuilder, FieldReference field) => Translator.Methods.GetRuntimeFieldHandle(builder, allocationBuilder, field);
    protected void RegisterMethodFunction(LLVMModuleRef llvmModule, MethodReference method, Collection<Instruction>? instructions, string? symbolName = null) => Translator.Methods.RegisterMethodFunction(llvmModule, method, instructions, symbolName);
    protected LLVMValueRef AddInternalGlobal(LLVMTypeRef type, string name) => Translator.Methods.AddInternalGlobal(type, name);
    protected unsafe LLVMTypeRef GetFunctionType(LLVMValueRef function) => Translator.Methods.GetFunctionType(function);

    protected IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types) => Translator.Runtime.GetAllTypes(types);
    protected bool TryGetArrayEnumerator(MethodReference targetMethod, out TypeReference elementType, out TypeDefinition definition, out MethodDefinition constructor) => Translator.Runtime.TryGetArrayEnumerator(targetMethod, out elementType, out definition, out constructor);
    protected LLVMValueRef GetArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef index, LLVMTypeRef elementType, int? elementSize = null) => Translator.Runtime.GetArrayElementAddress(builder, array, index, elementType, elementSize);
    protected LLVMValueRef GetMultiArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef[] indices, LLVMTypeRef elementType, int? elementSize = null) => Translator.Runtime.GetMultiArrayElementAddress(builder, array, indices, elementType, elementSize);
    protected LLVMValueRef BuildStringValue(LLVMBuilderRef builder, string value, Action<LLVMValueRef, TypeReference>? storeTemporaryRoot = null) => Translator.Runtime.BuildStringValue(builder, value, storeTemporaryRoot);
    protected LLVMValueRef BuildAllocation(LLVMBuilderRef builder, int size) => Translator.Runtime.BuildAllocation(builder, size);
    protected LLVMValueRef BuildAllocationSize(LLVMBuilderRef builder, LLVMValueRef size) => Translator.Runtime.BuildAllocationSize(builder, size);
    protected LLVMValueRef BuildBoxedValue(LLVMBuilderRef builder, LLVMValueRef value, TypeReference valueType) => Translator.Runtime.BuildBoxedValue(builder, value, valueType);
    protected bool TryGetNullableElementType(TypeReference type, out TypeReference elementType) => Translator.Runtime.TryGetNullableElementType(type, out elementType);
    protected FieldDefinition GetArrayDataField() => Translator.Runtime.GetArrayDataField();
    protected FieldDefinition GetArrayLengthsField() => Translator.Runtime.GetArrayLengthsField();
    protected LLVMValueRef BuildArrayLengthTable(LLVMBuilderRef builder, LLVMValueRef[] dimensions) => Translator.Runtime.BuildArrayLengthTable(builder, dimensions);
    protected LLVMValueRef GetArrayDataPointer(LLVMBuilderRef builder, LLVMValueRef array) => Translator.Runtime.GetArrayDataPointer(builder, array);
    protected LLVMValueRef GetFieldAddress(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, TypeReference? declaringType = null) => Translator.Runtime.GetFieldAddress(builder, obj, field, declaringType);
    protected Tuple<LLVMValueRef, LLVMTypeRef> CreateLocalStorage(LLVMBuilderRef builder, TypeReference type) => Translator.Runtime.CreateLocalStorage(builder, type);
    protected void CopyValue(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef source, int size) => Translator.Runtime.CopyValue(builder, destination, source, size);
    protected void StoreField(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, LLVMValueRef value) => Translator.Runtime.StoreField(builder, obj, field, value);
    protected FieldDefinition GetArrayLengthField() => Translator.Runtime.GetArrayLengthField();
    protected string GetRuntimeTypeKey(TypeReference type) => Translator.Runtime.GetRuntimeTypeKey(type);
    protected int GetRuntimeTypeId(TypeReference type) => Translator.Runtime.GetRuntimeTypeId(type);
    protected void InitializeRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj, TypeReference type) => Translator.Runtime.InitializeRuntimeType(builder, obj, type);
    protected int GetBoxedObjectHeaderSize() => Translator.Runtime.GetBoxedObjectHeaderSize();
    protected LLVMValueRef GetBoxedValueAddress(LLVMBuilderRef builder, LLVMValueRef box, TypeReference? valueType = null) => Translator.Runtime.GetBoxedValueAddress(builder, box, valueType);
    protected void InitializeBoxedRuntimeType(LLVMBuilderRef builder, LLVMValueRef box, TypeReference type) => Translator.Runtime.InitializeBoxedRuntimeType(builder, box, type);
    protected LLVMValueRef GetRuntimeTypeObject(TypeReference type) => Translator.Runtime.GetRuntimeTypeObject(type);
    protected ulong GetEnumConstantValue(object? value, TypeReference? underlyingType) => Translator.Runtime.GetEnumConstantValue(value, underlyingType);
    protected LLVMValueRef GetStaticString(string value) => Translator.Runtime.GetStaticString(value);
    protected LLVMValueRef GetStaticStringArray(string[] values) => Translator.Runtime.GetStaticStringArray(values);
    protected LLVMValueRef GetStaticUInt64Array(ulong[] values) => Translator.Runtime.GetStaticUInt64Array(values);
    protected LLVMValueRef GetObjectRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj) => Translator.Runtime.GetObjectRuntimeType(builder, obj);
    protected LLVMValueRef GetObjectRuntimeTypeId(LLVMBuilderRef builder, LLVMValueRef obj) => Translator.Runtime.GetObjectRuntimeTypeId(builder, obj);
    protected (LLVMValueRef Function, LLVMValueRef State)? GetCctorGuard(TypeReference type) => Translator.Runtime.GetCctorGuard(type);
    protected LLVMValueRef GetGCDescriptor(TypeReference type) => Translator.Runtime.GetGCDescriptor(type);
    protected void ValidateGCReferenceOffsets(TypeReference type, IEnumerable<int> offsets, string region) => Translator.Runtime.ValidateGCReferenceOffsets(type, offsets, region);
    protected IEnumerable<int> GetGCReferenceOffsets(TypeReference type) => Translator.Runtime.GetGCReferenceOffsets(type);
    protected TypeReference? GetClosedBaseType(TypeReference type) => Translator.Runtime.GetClosedBaseType(type);
    protected bool IsRuntimeTypeCompatible(TypeReference runtimeType, TypeReference targetType) => Translator.Runtime.IsRuntimeTypeCompatible(runtimeType, targetType);
}
