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

    public FieldDefinition ObjectTypeField => GetInstanceField(Object, "m_pType");
    public FieldDefinition TypeRuntimeTypeIdField => GetInstanceField(Type, "RuntimeTypeId");
    public FieldDefinition TypeGCDescriptorField => GetInstanceField(Type, "GCDescriptor");
    public FieldDefinition EnumValueField => GetInstanceField(Enum, "m_value");
    public FieldDefinition ArrayLengthField => GetInstanceField(Array, "Length");
    public FieldDefinition ArrayLengthsField => GetInstanceField(Array, "_lengths");
    public FieldDefinition ArrayDataField => GetInstanceField(Array, "m_pData");
    public FieldDefinition GCStaticRootsField => GetStaticField(GCHeap, "s_staticRoots");
    public FieldDefinition RuntimeFieldDataField => GetInstanceField(RuntimeFieldHandle, "Data");
    public FieldDefinition RuntimeFieldLengthField => GetInstanceField(RuntimeFieldHandle, "Length");

    public MethodDefinition ActivatorCreateInstanceMethod => Activator.Methods.Single(method =>
        method.Name == "CreateInstance" && method.IsStatic && method.GenericParameters.Count == 1 &&
        method.Parameters.Count == 0);

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
}
