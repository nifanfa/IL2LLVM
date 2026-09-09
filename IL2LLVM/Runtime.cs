sealed partial class Translator
{
    IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in GetAllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    bool IsArrayEnumeratorDefinition(TypeDefinition type)
    {
        if (!type.HasGenericParameters || type.Interfaces.Count == 0)
            return false;
        return type.Methods.Any(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType is ArrayType array &&
            array.ElementType is GenericParameter parameter && parameter.Type == GenericParameterType.Type &&
            parameter.Owner is TypeReference owner && SameTypeDefinition(owner, type) &&
            type.Interfaces.Any(@interface => @interface.InterfaceType is GenericInstanceType genericInterface &&
                genericInterface.GenericArguments.Count == 1 && SameType(genericInterface.GenericArguments[0], parameter) &&
                genericInterface.Resolve()?.Properties.Any(property => property.Name == "Current" &&
                    property.PropertyType is GenericParameter current && current.Type == GenericParameterType.Type &&
                    current.Position == 0) == true));
    }

    bool TryGetArrayEnumerator(MethodReference targetMethod, out TypeReference elementType,
        out TypeDefinition definition, out MethodDefinition constructor)
    {
        elementType = null!;
        definition = null!;
        constructor = null!;
        if (!targetMethod.HasThis || targetMethod.Parameters.Count != 0 ||
            targetMethod.DeclaringType.Resolve()?.IsInterface != true)
            return false;
        var returnType = SubstituteGenericParameter(targetMethod.ReturnType, targetMethod);
        if (returnType.Resolve()?.IsInterface != true)
            return false;
        foreach (var candidate in arrayEnumeratorTypes)
        {
            if (!TryCloseRuntimeType(candidate, returnType, out var closedType) ||
                closedType is not GenericInstanceType genericType)
                continue;
            var candidateConstructor = candidate.Methods.FirstOrDefault(method =>
                method.IsConstructor && !method.IsStatic && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType is ArrayType array &&
                array.ElementType is GenericParameter parameter && parameter.Type == GenericParameterType.Type &&
                parameter.Owner is TypeReference owner && SameTypeDefinition(owner, candidate));
            if (candidateConstructor?.Parameters[0].ParameterType is not ArrayType constructorArray ||
                constructorArray.ElementType is not GenericParameter elementParameter ||
                elementParameter.Position >= genericType.GenericArguments.Count)
                continue;
            elementType = genericType.GenericArguments[elementParameter.Position];
            definition = candidate;
            constructor = candidateConstructor;
            return true;
        }
        return false;
    }

    LLVMValueRef GetArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef index,
        LLVMTypeRef elementType, int? elementSize = null)
    {
        var nativeIndex = ConvertValue(builder, index, sizeType, false);
        var elementOffset = builder.BuildMul(nativeIndex,
            LLVMValueRef.CreateConstInt(sizeType, elementSize.HasValue ? (ulong)elementSize.Value : GetLLVMTypeSize(elementType), false));
        return builder.BuildGEP2(int8Type, GetArrayDataPointer(builder, array), [elementOffset]);
    }

    LLVMValueRef GetMultiArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef[] indices,
        LLVMTypeRef elementType, int? elementSize = null)
    {
        var elementIndex = LLVMValueRef.CreateConstInt(sizeType, 0, false);
        var lengths = builder.BuildLoad2(GetLLVMTypeRef(GetArrayLengthsField().FieldType),
            GetFieldAddress(builder, array, GetArrayLengthsField()));
        for (int i = 0; i < indices.Length; i++)
        {
            var length = ConvertValue(builder, builder.BuildLoad2(int32Type,
                GetArrayElementAddress(builder, lengths, LLVMValueRef.CreateConstInt(sizeType, (ulong)i, false), int32Type)), sizeType, false);
            elementIndex = builder.BuildAdd(builder.BuildMul(elementIndex, length), ConvertValue(builder, indices[i], sizeType, false));
        }
        var elementOffset = builder.BuildMul(elementIndex,
            LLVMValueRef.CreateConstInt(sizeType, elementSize.HasValue ? (ulong)elementSize.Value : GetLLVMTypeSize(elementType), false));
        return builder.BuildGEP2(int8Type, GetArrayDataPointer(builder, array), [elementOffset]);
    }

    LLVMValueRef BuildStringValue(LLVMBuilderRef builder, string value, Action<LLVMValueRef, TypeReference>? storeTemporaryRoot = null)
    {
        var arrayBaseSize = GetTypeDefinitionSize(localTypes["System.Array"]);
        var charArrayType = new ArrayType(localTypes["System.Char"]);
        var array = BuildAllocationSize(builder,
            LLVMValueRef.CreateConstInt(sizeType, (ulong)(arrayBaseSize + (value.Length + 1) * GetMetadataTypeSize(MetadataType.Char)), false));
        StoreField(builder, array, GetArrayLengthField(), LLVMValueRef.CreateConstInt(sizeType, (ulong)value.Length, false));
        InitializeRuntimeType(builder, array, charArrayType);
        storeTemporaryRoot?.Invoke(array, charArrayType);
        for (int i = 0; i < value.Length; i++)
        {
            var address = GetArrayElementAddress(builder, array, LLVMValueRef.CreateConstInt(sizeType, (ulong)i, false), int16Type);
            builder.BuildStore(LLVMValueRef.CreateConstInt(int16Type, value[i], false), address);
        }
        var stringType = localTypes["System.String"];
        var stringObject = BuildAllocation(builder, GetTypeDefinitionSize(stringType));
        InitializeRuntimeType(builder, stringObject, stringType);
        var constructor = GetRegisteredMethod(stringConstructor) ??
            throw new NotSupportedException($"Method is not defined in the input module: {stringConstructor.FullName}");
        builder.BuildCall2(constructor.Item2, constructor.Item1, [stringObject, array]);
        return stringObject;
    }

    LLVMValueRef BuildAllocation(LLVMBuilderRef builder, int size)
    {
        return BuildAllocationSize(builder, LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
    }

    LLVMValueRef BuildAllocationSize(LLVMBuilderRef builder, LLVMValueRef size)
    {
        return builder.BuildCall2(gcAllocateType, gcAllocateFunction,
            [ConvertValue(builder, size, sizeType, false)]);
    }

    LLVMValueRef BuildBoxedValue(LLVMBuilderRef builder, LLVMValueRef value, TypeReference valueType)
    {
        var box = BuildAllocation(builder, GetBoxedObjectHeaderSize() + Math.Max(1, GetTypeSize(valueType)));
        InitializeBoxedRuntimeType(builder, box, valueType);
        var boxedValue = GetBoxedValueAddress(builder, box);
        if (IsValueType(valueType) && value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
            CopyValue(builder, boxedValue, value, GetTypeSize(valueType));
        else
        {
            var storage = builder.BuildAlloca(value.TypeOf);
            builder.BuildStore(value, storage);
            CopyValue(builder, boxedValue, storage, GetTypeSize(valueType));
        }
        return box;
    }

    bool TryGetNullableElementType(TypeReference type, out TypeReference elementType)
    {
        if (type is GenericInstanceType generic && generic.ElementType.Namespace == "System" &&
            generic.ElementType.Name == "Nullable`1" && generic.GenericArguments.Count == 1)
        {
            elementType = generic.GenericArguments[0];
            return true;
        }
        elementType = type;
        return false;
    }

    bool IsExternalMethod(MethodReference method)
    {
        return (FindLocalMethod(method, localMethods) ?? method.Resolve())?.PInvokeInfo is not null;
    }

    LLVMValueRef GetSpanDataPointer(LLVMBuilderRef builder, LLVMValueRef value, TypeReference type)
    {
        if (type is not GenericInstanceType span || span.ElementType.FullName is not "System.Span`1" and not "System.ReadOnlySpan`1" ||
            span.GenericArguments.Count != 1)
            throw new NotSupportedException($"Expected Span<T> or ReadOnlySpan<T>, not {type.FullName}.");
        var definition = span.Resolve() ?? throw new NotSupportedException($"Type is not defined in the input module: {span.FullName}");
        var arrayField = definition.Fields.First(field => field.Name == "_array");
        var startField = definition.Fields.First(field => field.Name == "_start");
        var array = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, value, arrayField, span));
        var start = builder.BuildLoad2(int32Type, GetFieldAddress(builder, value, startField, span));
        return GetArrayElementAddress(builder, array, start, GetLLVMTypeRef(span.GenericArguments[0]), GetTypeSize(span.GenericArguments[0]));
    }

    LLVMValueRef GetExternalArgumentPointer(LLVMBuilderRef builder, LLVMValueRef value, TypeReference type)
    {
        if (type.MetadataType == MetadataType.String)
            return GetStringDataPointer(builder, value);
        if (type is ArrayType)
            return GetArrayDataPointer(builder, value);
        if (type is GenericInstanceType span && span.ElementType.FullName is "System.Span`1" or "System.ReadOnlySpan`1")
            return GetSpanDataPointer(builder, value, span);
        return value;
    }

    FieldDefinition GetStringCharsField()
    {
        return localTypes["System.String"].Fields.First(field => field.Name == "_chars");
    }

    LLVMValueRef GetStringDataPointer(LLVMBuilderRef builder, LLVMValueRef value)
    {
        var chars = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, value, GetStringCharsField()));
        return GetArrayDataPointer(builder, chars);
    }

    FieldDefinition GetArrayDataField()
    {
        return localTypes["System.Array"].Fields.First(field => field.Name == "m_pData");
    }

    FieldDefinition GetArrayLengthsField()
    {
        return localTypes["System.Array"].Fields.First(field => field.Name == "_lengths");
    }

    LLVMValueRef BuildArrayLengthTable(LLVMBuilderRef builder, LLVMValueRef[] dimensions)
    {
        var elementType = localTypes["System.Int32"];
        var arrayType = new ArrayType(elementType);
        var array = BuildAllocationSize(builder, builder.BuildAdd(
            LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false),
            LLVMValueRef.CreateConstInt(sizeType, (ulong)(dimensions.Length * GetTypeSize(elementType)), false)));
        StoreField(builder, array, GetArrayLengthField(), LLVMValueRef.CreateConstInt(int32Type, (ulong)dimensions.Length, false));
        InitializeRuntimeType(builder, array, arrayType);
        for (int index = 0; index < dimensions.Length; index++)
        {
            var address = GetArrayElementAddress(builder, array,
                LLVMValueRef.CreateConstInt(sizeType, (ulong)index, false), int32Type);
            builder.BuildStore(ConvertValue(builder, dimensions[index], int32Type, false), address);
        }
        return array;
    }

    LLVMValueRef GetArrayDataPointer(LLVMBuilderRef builder, LLVMValueRef array)
    {
        return builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, array, GetArrayDataField()));
    }

    LLVMValueRef GetFieldAddress(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field,
        TypeReference? declaringType = null)
    {
        var offset = LLVMValueRef.CreateConstInt(sizeType, (ulong)(declaringType is null
            ? GetFieldOffset(field)
            : GetFieldOffsetForType(field, declaringType)), false);
        return builder.BuildGEP2(int8Type, obj, [offset]);
    }

    Tuple<LLVMValueRef, LLVMTypeRef> CreateLocalStorage(LLVMBuilderRef builder, TypeReference type)
    {
        if (IsValueType(type))
        {
            var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
            var storageType = LLVMTypeRef.CreateArray(int8Type, (uint)Math.Max(1, GetTypeSize(type)));
            var storage = builder.BuildBitCast(builder.BuildAlloca(storageType), pointerType);
            var local = builder.BuildAlloca(pointerType);
            builder.BuildStore(storage, local);
            return new(local, pointerType);
        }

        var llvmType = GetLLVMTypeRef(type);
        return new(builder.BuildAlloca(llvmType), llvmType);
    }

    void CopyValue(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef source, int size)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var destinationPointer = destination.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            ? (destination.TypeOf.Equals(pointerType) ? destination : builder.BuildBitCast(destination, pointerType))
            : builder.BuildAlloca(destination.TypeOf);
        var sourcePointer = source.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            ? (source.TypeOf.Equals(pointerType) ? source : builder.BuildBitCast(source, pointerType))
            : builder.BuildAlloca(source.TypeOf);
        if (destination.TypeOf.Kind != LLVMTypeKind.LLVMPointerTypeKind)
            builder.BuildStore(source, destinationPointer);
        if (source.TypeOf.Kind != LLVMTypeKind.LLVMPointerTypeKind)
            builder.BuildStore(source, sourcePointer);
        unsafe
        {
            LLVM.BuildMemCpy(builder, destinationPointer, 1, sourcePointer, 1,
                LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
        }
    }

    void StoreField(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, LLVMValueRef value)
    {
        if (IsValueType(field.FieldType))
        {
            CopyValue(builder, GetFieldAddress(builder, obj, field), value, GetTypeSize(field.FieldType));
            return;
        }
        var fieldType = GetLLVMTypeRef(field.FieldType);
        builder.BuildStore(ConvertValue(builder, value, fieldType), GetFieldAddress(builder, obj, field));
    }

    FieldDefinition GetArrayLengthField()
    {
        return localTypes["System.Array"].Fields.First(field => field.Name == "Length");
    }

    string GetRuntimeTypeKey(TypeReference type)
    {
        if (type is GenericInstanceType generic)
            return generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(GetRuntimeTypeKey)) + ">";
        return type.FullName;
    }

    int GetRuntimeTypeId(TypeReference type)
    {
        var key = GetRuntimeTypeKey(type);
        if (!runtimeTypeIds.TryGetValue(key, out var id))
        {
            id = nextRuntimeTypeId++;
            runtimeTypeIds.Add(key, id);
        }
        runtimeTypes.TryAdd(key, type);
        return id;
    }

    void InitializeRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj, TypeReference type)
    {
        if (obj == default || IsValueType(type))
            return;
        StoreField(builder, obj, GetObjectTypeDescriptorField(), GetTypeDescriptor(type));
        if (type is ArrayType)
        {
            StoreField(builder, obj, GetArrayDataField(), builder.BuildGEP2(int8Type, obj,
                [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(localTypes["System.Array"]), false)]));
        }
    }

    int GetBoxedObjectHeaderSize()
    {
        return GetTypeDefinitionSize(localTypes["System.Object"]);
    }

    LLVMValueRef GetBoxedValueAddress(LLVMBuilderRef builder, LLVMValueRef box)
    {
        return builder.BuildGEP2(int8Type, box,
            [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetBoxedObjectHeaderSize(), false)]);
    }

    void InitializeBoxedRuntimeType(LLVMBuilderRef builder, LLVMValueRef box, TypeReference type)
    {
        StoreField(builder, box, GetObjectTypeDescriptorField(), GetTypeDescriptor(type));
    }

    LLVMValueRef GetTypeDescriptor(TypeReference type)
    {
        var key = GetRuntimeTypeKey(type);
        if (typeDescriptors.TryGetValue(key, out var descriptor))
            return descriptor;
        var descriptorType = context.GetStructType([
            int32Type,
            LLVMTypeRef.CreatePointer(int8Type, 0),
            LLVMTypeRef.CreatePointer(int8Type, 0)
        ], false);
        descriptor = AddInternalGlobal(descriptorType, $"__type_descriptor_{GetStableSymbolSuffix(key)}");
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        descriptor.Initializer = LLVMValueRef.CreateConstNamedStruct(descriptorType, [
            LLVMValueRef.CreateConstInt(int32Type, (ulong)GetRuntimeTypeId(type), false),
            LLVMValueRef.CreateConstPointerCast(GetGCDescriptor(type), pointerType),
            LLVMValueRef.CreateConstNull(pointerType)
        ]);
        typeDescriptors.Add(key, descriptor);
        return descriptor;
    }

    LLVMValueRef GetObjectTypeDescriptor(LLVMBuilderRef builder, LLVMValueRef obj)
    {
        return builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, obj, GetObjectTypeDescriptorField()));
    }

    LLVMValueRef GetObjectRuntimeTypeId(LLVMBuilderRef builder, LLVMValueRef obj)
    {
        return ConvertValue(builder, builder.BuildLoad2(int32Type,
            GetFieldAddress(builder, GetObjectTypeDescriptor(builder, obj), GetTypeDescriptorRuntimeTypeIdField())), sizeType, false);
    }

    (LLVMValueRef Function, LLVMValueRef State)? GetCctorGuard(TypeReference type)
    {
        var definition = type.Resolve();
        if (definition is null)
            return null;
        var key = definition.FullName;
        if (cctorGuards.TryGetValue(key, out var existing))
            return existing;
        var cctor = moduleMethods.Values.FirstOrDefault(candidate =>
            candidate.Item3.Name == ".cctor" && SameTypeDefinition(candidate.Item3.DeclaringType, definition));
        if (cctor is null || cctor.Item1 == default)
            return null;

        var suffix = GetStableSymbolSuffix(GetRuntimeTypeKey(type));
        var state = AddInternalGlobal(int8Type, $"__cctor_state_{suffix}");
        state.Initializer = LLVMValueRef.CreateConstNull(int8Type);
        var guardType = LLVMTypeRef.CreateFunction(voidType, []);
        var guard = module.AddFunction($"__cctor_guard_{suffix}", guardType);
        guard.Linkage = LLVMLinkage.LLVMExternalLinkage;
        cctorGuards.Add(key, (guard, state));

        var guardBuilder = context.CreateBuilder();
        var entry = context.AppendBasicBlock(guard, "entry");
        var initialize = context.AppendBasicBlock(guard, "initialize");
        var done = context.AppendBasicBlock(guard, "done");
        guardBuilder.PositionAtEnd(entry);
        var currentState = guardBuilder.BuildLoad2(int8Type, state);
        var alreadyInitialized = guardBuilder.BuildICmp(LLVMIntPredicate.LLVMIntNE, currentState,
            LLVMValueRef.CreateConstInt(int8Type, 0, false));
        guardBuilder.BuildCondBr(alreadyInitialized, done, initialize);
        guardBuilder.PositionAtEnd(initialize);
        guardBuilder.BuildStore(LLVMValueRef.CreateConstInt(int8Type, 1, false), state);
        guardBuilder.BuildCall2(cctor.Item2, cctor.Item1, []);
        guardBuilder.BuildStore(LLVMValueRef.CreateConstInt(int8Type, 2, false), state);
        guardBuilder.BuildBr(done);
        guardBuilder.PositionAtEnd(done);
        guardBuilder.BuildRetVoid();
        return (guard, state);
    }

    LLVMValueRef GetGCDescriptor(TypeReference type)
    {
        var key = GetRuntimeTypeKey(type);
        if (gcDescriptors.TryGetValue(key, out var descriptor))
            return descriptor;

        var fixedReferences = GetGCReferenceOffsets(type).Distinct().Order().ToArray();
        var elementReferences = Array.Empty<int>();
        var elementSize = 0;
        var arrayLengthOffset = 0;
        var baseSize = type is ArrayType array
            ? GetTypeDefinitionSize(GetArrayLengthField().DeclaringType)
            : type.Resolve() is { } definition && localTypes.ContainsKey(definition.FullName)
                ? GetObjectSize(type)
                : GetTypeSize(type);
        if (type is ArrayType arrayType)
        {
            elementSize = GetTypeSize(arrayType.ElementType);
            arrayLengthOffset = GetFieldOffset(GetArrayLengthField());
            elementReferences = IsManagedReferenceType(arrayType.ElementType)
                ? [0]
                : IsValueType(arrayType.ElementType)
                    ? GetGCReferenceOffsets(arrayType.ElementType).Distinct().Order().ToArray()
                    : [];
        }

        ValidateGCReferenceOffsets(type, fixedReferences, "object");
        ValidateGCReferenceOffsets(type, elementReferences, "array element");

        var gcDescType = localTypes.TryGetValue("System.GCDesc", out var localGCDescType)
            ? localGCDescType
            : throw new NotSupportedException("System.GCDesc is not defined in the input module.");
        var headerValues = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            ["TotalSlotCount"] = 0,
            ["BaseSize"] = (ulong)baseSize,
            ["FixedReferenceCount"] = (ulong)fixedReferences.Length,
            ["ArrayLengthOffset"] = (ulong)arrayLengthOffset,
            ["ArrayElementSize"] = (ulong)elementSize,
            ["ArrayElementReferenceCount"] = (ulong)elementReferences.Length
        };
        var allGCDescFields = gcDescType.Fields.Where(field => !field.IsStatic).ToArray();
        var gcDescFields = allGCDescFields.Where(field => headerValues.ContainsKey(field.Name)).ToArray();
        headerValues["TotalSlotCount"] = (ulong)(gcDescFields.Length + fixedReferences.Length + elementReferences.Length);
        if (gcDescFields.Length != headerValues.Count ||
            !allGCDescFields.Any(field => field.Name == "ReferenceOffsets") ||
            allGCDescFields.Any(field => !headerValues.ContainsKey(field.Name) && field.Name != "ReferenceOffsets") ||
            gcDescFields.Any(field => GetTypeSize(field.FieldType) != pointerSize) ||
            gcDescFields.Any(field => !headerValues.ContainsKey(field.Name)))
            throw new InvalidOperationException("System.GCDesc must contain exactly six pointer-sized instance fields: TotalSlotCount, BaseSize, FixedReferenceCount, ArrayLengthOffset, ArrayElementSize, ArrayElementReferenceCount.");
        var values = new List<LLVMValueRef>();
        var fieldTypes = new List<LLVMTypeRef>();
        foreach (var field in gcDescFields)
        {
            fieldTypes.Add(sizeType);
            values.Add(LLVMValueRef.CreateConstInt(sizeType, headerValues[field.Name], false));
        }
        if (fixedReferences.Length != 0)
        {
            var offsetType = LLVMTypeRef.CreateArray(int16Type, (uint)fixedReferences.Length);
            fieldTypes.Add(offsetType);
            values.Add(LLVMValueRef.CreateConstArray(int16Type,
                fixedReferences.Select(offset => LLVMValueRef.CreateConstInt(int16Type, (ulong)offset, false)).ToArray()));
        }
        if (elementReferences.Length != 0)
        {
            var offsetType = LLVMTypeRef.CreateArray(int16Type, (uint)elementReferences.Length);
            fieldTypes.Add(offsetType);
            values.Add(LLVMValueRef.CreateConstArray(int16Type,
                elementReferences.Select(offset => LLVMValueRef.CreateConstInt(int16Type, (ulong)offset, false)).ToArray()));
        }
        var descriptorType = context.GetStructType(fieldTypes.ToArray(), false);
        descriptor = AddInternalGlobal(descriptorType, $"__gc_desc_{GetStableSymbolSuffix(key)}");
        descriptor.Initializer = LLVMValueRef.CreateConstNamedStruct(descriptorType, values.ToArray());
        gcDescriptors.Add(key, descriptor);
        return descriptor;
    }

    void ValidateGCReferenceOffsets(TypeReference type, IEnumerable<int> offsets, string region)
    {
        foreach (var offset in offsets)
        {
            if ((uint)offset > ushort.MaxValue)
                throw new InvalidOperationException($"GC {region} reference offset for {type.FullName} does not fit in ushort: {offset}.");
        }
    }

    IEnumerable<int> GetGCReferenceOffsets(TypeReference type)
    {
        var references = new List<int>();
        if (type is ArrayType)
            type = localTypes["System.Array"];
        Collect(type, 0, true);
        return references;

        void Collect(TypeReference currentType, int baseOffset, bool includeBaseType)
        {
            var definition = currentType.Resolve();
            if (definition is null)
                return;
            if (includeBaseType && !IsValueType(currentType))
            {
                var baseType = GetClosedBaseType(currentType);
                if (baseType is not null)
                    Collect(baseType, baseOffset, true);
            }
            foreach (var field in definition.Fields.Where(field => !field.IsStatic))
            {
                var fieldType = currentType is GenericInstanceType genericType
                    ? SubstituteGenericTypeArguments(field.FieldType, genericType)
                    : field.FieldType;
                var fieldOffset = baseOffset + GetFieldOffsetForType(field, currentType);
                if (IsManagedReferenceType(fieldType))
                    references.Add(fieldOffset);
                else if (IsValueType(fieldType))
                    Collect(fieldType, fieldOffset, false);
            }
        }
    }

    TypeReference? GetClosedBaseType(TypeReference type)
    {
        var definition = type.Resolve();
        if (definition?.BaseType is null)
            return null;
        return type is GenericInstanceType genericType
            ? SubstituteGenericTypeArguments(definition.BaseType, genericType)
            : definition.BaseType;
    }

    bool IsRuntimeTypeCompatible(TypeReference runtimeType, TypeReference targetType)
    {
        var targetDefinition = targetType.Resolve();
        var runtimeDefinition = runtimeType.Resolve();
        if (targetDefinition is null || runtimeDefinition is null)
            return false;
        if (runtimeDefinition.IsValueType && targetDefinition.FullName is "System.Object" or "System.ValueType")
            return true;
        if (targetDefinition.IsInterface)
            return GetImplementedInterfaces(runtimeType).Any(interfaceType => SameType(interfaceType, targetType));
        for (TypeReference? current = runtimeType; current is not null; current = GetClosedBaseType(current))
        {
            if (SameType(current, targetType))
                return true;
        }
        return false;
    }

}
