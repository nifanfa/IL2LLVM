sealed class CoreLibMetadata
{
    private readonly IReadOnlyDictionary<string, TypeDefinition> types;

    public CoreLibMetadata(IReadOnlyDictionary<string, TypeDefinition> types)
    {
        this.types = types;
    }

    public TypeDefinition Object => GetType("System.Object");
    public TypeDefinition ValueType => GetType("System.ValueType");
    public TypeDefinition Array => GetType("System.Array");
    public TypeDefinition String => GetType("System.String");
    public TypeDefinition Type => GetType("System.Type");
    public TypeDefinition Enum => GetType("System.Enum");
    public TypeDefinition Delegate => GetType("System.Delegate");
    public TypeDefinition MulticastDelegate => GetType("System.MulticastDelegate");
    public TypeDefinition Nullable => GetType("System.Nullable`1");
    public TypeDefinition Activator => GetType("System.Activator");
    public TypeDefinition ArrayEnumerator => GetType("System.ArrayEnumerator`1");
    public TypeDefinition IntPtr => GetType("System.IntPtr");
    public TypeDefinition Exception => GetType("System.Exception");
    public TypeDefinition InvalidCastException => GetType("System.InvalidCastException");
    public TypeDefinition NullReferenceException => GetType("System.NullReferenceException");
    public TypeDefinition IndexOutOfRangeException => GetType("System.IndexOutOfRangeException");
    public TypeDefinition OverflowException => GetType("System.OverflowException");
    public TypeDefinition DivideByZeroException => GetType("System.DivideByZeroException");
    public TypeDefinition Char => GetType("System.Char");
    public TypeDefinition Int32 => GetType("System.Int32");
    public TypeDefinition UInt64 => GetType("System.UInt64");
    public TypeDefinition UIntPtr => GetType("System.UIntPtr");
    public TypeDefinition Void => GetType("System.Void");
    public TypeDefinition RuntimeTypeHandle => GetType("System.RuntimeTypeHandle");
    public TypeDefinition RuntimeFieldHandle => GetType("System.RuntimeFieldHandle");
    public TypeDefinition GCDesc => GetType("System.GCDesc");
    public TypeDefinition FlagsAttribute => GetType("System.FlagsAttribute");
    public TypeDefinition RuntimeExportAttribute => GetType("System.Runtime.RuntimeExportAttribute");
    public TypeDefinition RuntimeNoGCFrameAttribute => GetType("System.Runtime.RuntimeNoGCFrameAttribute");
    public TypeDefinition UnmanagedCallersOnlyAttribute => GetType("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute");
    public TypeDefinition ExceptionRuntime => GetType("System.Runtime.ExceptionRuntime");
    public TypeDefinition ExceptionFrame => GetType("System.Runtime.ExceptionFrame");
    public TypeDefinition JumpBuffer => GetType("System.Runtime.JumpBuffer");
    public TypeDefinition StackPointer => GetType("System.Runtime.StackPointer");
    public TypeDefinition GCHeap => GetType("System.Runtime.GCHeap");
    public TypeDefinition GCFrame => GetType("System.Runtime.GCFrame");
    public TypeDefinition GCRoot => GetType("System.Runtime.GCRoot");
    public TypeDefinition GCStaticRoot => GetType("System.Runtime.GCStaticRoot");

    public FieldDefinition ObjectTypeField => GetInstanceField(Object, "m_pType");
    public FieldDefinition StringLengthField => GetInstanceField(String, "Length");
    public FieldDefinition StringCharsField => GetInstanceField(String, "_chars");
    public FieldDefinition DelegateFunctionField => GetInstanceField(Delegate, "_function");
    public FieldDefinition DelegateTargetField => GetInstanceField(Delegate, "_target");
    public FieldDefinition DelegateNextField => GetInstanceField(Delegate, "_next");
    public FieldDefinition TypeNameField => GetInstanceField(Type, "Name");
    public FieldDefinition TypeNamespaceField => GetInstanceField(Type, "Namespace");
    public FieldDefinition TypeFullNameField => GetInstanceField(Type, "FullName");
    public FieldDefinition TypeRuntimeTypeIdField => GetInstanceField(Type, "RuntimeTypeId");
    public FieldDefinition TypeGCDescriptorField => GetInstanceField(Type, "GCDescriptor");
    public FieldDefinition TypeEnumNamesField => GetInstanceField(Type, "EnumNames");
    public FieldDefinition TypeEnumValuesField => GetInstanceField(Type, "EnumValues");
    public FieldDefinition TypeIsFlagsEnumField => GetInstanceField(Type, "IsFlagsEnum");
    public FieldDefinition TypeIsSignedEnumField => GetInstanceField(Type, "IsSignedEnum");
    public FieldDefinition EnumValueField => GetInstanceField(Enum, "m_value");
    public FieldDefinition ArrayLengthField => GetInstanceField(Array, "Length");
    public FieldDefinition ArrayLengthsField => GetInstanceField(Array, "_lengths");
    public FieldDefinition ArrayDataField => GetInstanceField(Array, "m_pData");
    public FieldDefinition GCDescTotalSlotCountField => GetInstanceField(GCDesc, "TotalSlotCount");
    public FieldDefinition GCDescBaseSizeField => GetInstanceField(GCDesc, "BaseSize");
    public FieldDefinition GCDescFixedReferenceCountField => GetInstanceField(GCDesc, "FixedReferenceCount");
    public FieldDefinition GCDescArrayLengthOffsetField => GetInstanceField(GCDesc, "ArrayLengthOffset");
    public FieldDefinition GCDescArrayElementSizeField => GetInstanceField(GCDesc, "ArrayElementSize");
    public FieldDefinition GCDescArrayElementReferenceCountField => GetInstanceField(GCDesc, "ArrayElementReferenceCount");
    public FieldDefinition GCDescReferenceOffsetsField => GetInstanceField(GCDesc, "ReferenceOffsets");
    public FieldDefinition GCStaticRootsField => GetStaticField(GCHeap, "s_staticRoots");
    public FieldDefinition GCStaticRootNextField => GetInstanceField(GCStaticRoot, "Next");
    public FieldDefinition GCStaticRootAddressField => GetInstanceField(GCStaticRoot, "Address");
    public FieldDefinition GCStaticRootDescriptorField => GetInstanceField(GCStaticRoot, "Descriptor");
    public FieldDefinition RuntimeFieldDataField => GetInstanceField(RuntimeFieldHandle, "Data");
    public FieldDefinition RuntimeFieldLengthField => GetInstanceField(RuntimeFieldHandle, "Length");

    public MethodDefinition ActivatorCreateInstanceMethod => Activator.Methods.Single(method =>
        method.Name == "CreateInstance" && method.IsStatic && method.GenericParameters.Count == 1 &&
        method.Parameters.Count == 0);
    public MethodDefinition StringCharArrayConstructor => GetRequiredConstructor(String, new ArrayType(Char));
    public MethodDefinition TypeGetTypeFromHandleMethod => GetRequiredMethod(Type, "GetTypeFromHandle", false,
        Type, RuntimeTypeHandle);
    public MethodDefinition ExceptionPushMethod => GetRequiredMethod(ExceptionRuntime, "Push", false, Void,
        new PointerType(ExceptionFrame), new PointerType(JumpBuffer));
    public MethodDefinition ExceptionPopMethod => GetRequiredMethod(ExceptionRuntime, "Pop", false, Void,
        new PointerType(ExceptionFrame));
    public MethodDefinition ExceptionGetBufferMethod => GetRequiredMethod(ExceptionRuntime, "GetBuffer", false,
        new PointerType(JumpBuffer), new PointerType(ExceptionFrame));
    public MethodDefinition ExceptionGetTopMethod => GetRequiredMethod(ExceptionRuntime, "GetTop", false,
        new PointerType(ExceptionFrame));
    public MethodDefinition ExceptionGetCurrentMethod => GetRequiredMethod(ExceptionRuntime, "GetCurrent", false,
        Exception);
    public MethodDefinition ExceptionSetJumpMethod => GetRequiredMethod(ExceptionRuntime, "SetJump", false, Int32,
        new PointerType(JumpBuffer), new PointerType(StackPointer));
    public MethodDefinition ExceptionLongJumpMethod => GetRequiredMethod(ExceptionRuntime, "LongJump", false, Void,
        new PointerType(JumpBuffer), Int32);
    public MethodDefinition ExceptionAbortMethod => GetRequiredMethod(ExceptionRuntime, "Abort", false, Void);
    public MethodDefinition ExceptionThrowMethod => GetRequiredMethod(ExceptionRuntime, "Throw", false, Void,
        Exception);
    public MethodDefinition GCAllocateMethod => GetRequiredMethod(GCHeap, "Allocate", false,
        new PointerType(Object), UIntPtr);
    public MethodDefinition GCPushMethod => GetRequiredMethod(GCHeap, "Push", false, Void,
        new PointerType(GCFrame), new PointerType(GCRoot), Int32);
    public MethodDefinition GCPopMethod => GetRequiredMethod(GCHeap, "Pop", false, Void,
        new PointerType(GCFrame));

    public bool IsObject(TypeReference type) => IsType(type, Object);
    public bool IsValueType(TypeReference type) => IsType(type, ValueType);
    public bool IsEnum(TypeReference type) => IsType(type, Enum);
    public bool IsDelegate(TypeReference type) => IsType(type, Delegate) || IsType(type, MulticastDelegate);
    public bool IsNullable(TypeReference type) => IsType(type, Nullable);
    public bool IsNativeInteger(TypeReference type) => IsType(type, IntPtr) || IsType(type, UIntPtr);
    public bool IsFlagsAttribute(TypeReference type) => IsType(type, FlagsAttribute);
    public bool IsRuntimeExportAttribute(TypeReference type) => IsType(type, RuntimeExportAttribute);
    public bool IsRuntimeNoGCFrameAttribute(TypeReference type) => IsType(type, RuntimeNoGCFrameAttribute);
    public bool IsUnmanagedCallersOnlyAttribute(TypeReference type) => IsType(type, UnmanagedCallersOnlyAttribute);

    public FieldDefinition GetNullableHasValueField(TypeReference type) => GetInstanceField(Resolve(type), "_hasValue");
    public FieldDefinition GetNullableValueField(TypeReference type) => GetInstanceField(Resolve(type), "_value");

    public TypeDefinition GetType(string fullName)
    {
        return types.TryGetValue(fullName, out var type)
            ? type
            : throw new NotSupportedException($"CoreLib type is not defined in the input module: {fullName}");
    }

    private static bool IsType(TypeReference type, TypeDefinition definition)
    {
        var elementType = type is GenericInstanceType generic ? generic.ElementType : type;
        return elementType.FullName == definition.FullName;
    }

    private static TypeDefinition Resolve(TypeReference type)
    {
        return type.Resolve() ?? throw new NotSupportedException($"CoreLib type is not defined: {type.FullName}");
    }

    private static FieldDefinition GetInstanceField(TypeDefinition type, string name)
    {
        return type.Fields.FirstOrDefault(field => !field.IsStatic && field.Name == name) ??
            throw new NotSupportedException($"CoreLib field is not defined: {type.FullName}.{name}");
    }

    private static FieldDefinition GetStaticField(TypeDefinition type, string name)
    {
        return type.Fields.FirstOrDefault(field => field.IsStatic && field.Name == name) ??
            throw new NotSupportedException($"CoreLib field is not defined: {type.FullName}.{name}");
    }

    private static MethodDefinition GetRequiredConstructor(TypeDefinition type,
        params TypeReference[] parameterTypes)
    {
        var matches = type.Methods.Where(method => method.IsConstructor && !method.IsStatic &&
            HasSignature(method, parameterTypes)).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one matching CoreLib constructor on {type.FullName}, found {matches.Count}.");
    }

    private static MethodDefinition GetRequiredMethod(TypeDefinition type, string name, bool hasThis,
        TypeReference returnType, params TypeReference[] parameterTypes)
    {
        var matches = type.Methods.Where(method => method.Name == name && method.HasThis == hasThis &&
            method.GenericParameters.Count == 0 && SameType(method.ReturnType, returnType) &&
            HasSignature(method, parameterTypes)).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one matching CoreLib method named {name} on {type.FullName}, found {matches.Count}.");
    }

    private static bool HasSignature(MethodReference method, IReadOnlyList<TypeReference> parameterTypes)
    {
        return method.Parameters.Count == parameterTypes.Count &&
            method.Parameters.Select(parameter => parameter.ParameterType).Zip(parameterTypes)
                .All(pair => SameType(pair.First, pair.Second));
    }

    private static bool SameType(TypeReference left, TypeReference right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.FullName == right.FullName)
            return true;
        if (left is ArrayType leftArray && right is ArrayType rightArray)
            return leftArray.Rank == rightArray.Rank && SameType(leftArray.ElementType, rightArray.ElementType);
        if (left is PointerType leftPointer && right is PointerType rightPointer)
            return SameType(leftPointer.ElementType, rightPointer.ElementType);
        if (left is ByReferenceType leftByReference && right is ByReferenceType rightByReference)
            return SameType(leftByReference.ElementType, rightByReference.ElementType);
        var leftDefinition = left.Resolve();
        var rightDefinition = right.Resolve();
        if (leftDefinition is not null && rightDefinition is not null)
            return leftDefinition.MetadataToken == rightDefinition.MetadataToken &&
                leftDefinition.Module.Mvid == rightDefinition.Module.Mvid;
        return left.FullName == right.FullName;
    }
}
