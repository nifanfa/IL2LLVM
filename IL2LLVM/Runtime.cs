sealed class Runtime(Translator translator) : TranslationComponent(translator)
{
    internal new IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in GetAllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    internal new bool IsArrayEnumeratorDefinition(TypeDefinition type)
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

    internal new bool TryGetArrayEnumerator(MethodReference targetMethod, out TypeReference elementType,
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

    internal new LLVMValueRef GetArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef index,
        LLVMTypeRef elementType, int? elementSize = null)
    {
        var nativeIndex = ConvertValue(builder, index, sizeType, false);
        var elementOffset = builder.BuildMul(nativeIndex,
            LLVMValueRef.CreateConstInt(sizeType, elementSize.HasValue ? (ulong)elementSize.Value : GetLLVMTypeSize(elementType), false));
        return builder.BuildGEP2(int8Type, GetArrayDataPointer(builder, array), [elementOffset]);
    }

    internal new LLVMValueRef GetMultiArrayElementAddress(LLVMBuilderRef builder, LLVMValueRef array, LLVMValueRef[] indices,
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

    internal new LLVMValueRef BuildStringValue(LLVMBuilderRef builder, string value, Action<LLVMValueRef, TypeReference>? storeTemporaryRoot = null)
    {
        var arrayBaseSize = GetTypeDefinitionSize(coreLib.Array);
        var charArrayType = new ArrayType(coreLib.Char);
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
        var stringType = coreLib.String;
        var stringObject = BuildAllocation(builder, GetTypeDefinitionSize(stringType));
        InitializeRuntimeType(builder, stringObject, stringType);
        var constructor = GetRegisteredMethod(stringConstructor) ??
            throw new NotSupportedException($"Method is not defined in the input module: {stringConstructor.FullName}");
        builder.BuildCall2(constructor.Item2, constructor.Item1, [stringObject, array]);
        return stringObject;
    }

    internal new LLVMValueRef BuildAllocation(LLVMBuilderRef builder, int size)
    {
        return BuildAllocationSize(builder, LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
    }

    internal new LLVMValueRef BuildAllocationSize(LLVMBuilderRef builder, LLVMValueRef size)
    {
        return builder.BuildCall2(gcAllocateType, gcAllocateFunction,
            [ConvertValue(builder, size, sizeType, false)]);
    }

    internal new LLVMValueRef BuildBoxedValue(LLVMBuilderRef builder, LLVMValueRef value, TypeReference valueType)
    {
        var boxSize = GetEnumUnderlyingType(valueType) is null
            ? GetBoxedObjectHeaderSize() + Math.Max(1, GetTypeSize(valueType))
            : GetObjectSize(coreLib.Enum);
        var box = BuildAllocation(builder, boxSize);
        InitializeBoxedRuntimeType(builder, box, valueType);
        var boxedValue = GetBoxedValueAddress(builder, box, valueType);
        if (GetEnumUnderlyingType(valueType) is { } enumUnderlyingType)
        {
            var isSigned = enumUnderlyingType.MetadataType is MetadataType.SByte or MetadataType.Int16 or
                MetadataType.Int32 or MetadataType.Int64;
            builder.BuildStore(ConvertValue(builder, value, int64Type, isSigned), boxedValue);
            return box;
        }
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

    internal new bool TryGetNullableElementType(TypeReference type, out TypeReference elementType)
    {
        if (type is GenericInstanceType generic && coreLib.IsNullable(generic) && generic.GenericArguments.Count == 1)
        {
            elementType = generic.GenericArguments[0];
            return true;
        }
        elementType = type;
        return false;
    }

    internal new bool IsExternalMethod(MethodReference method)
    {
        return (FindLocalMethod(method, localMethods) ?? method.Resolve())?.PInvokeInfo is not null;
    }

    internal new FieldDefinition GetArrayDataField()
    {
        return coreLib.ArrayDataField;
    }

    internal new FieldDefinition GetArrayLengthsField()
    {
        return coreLib.ArrayLengthsField;
    }

    internal new LLVMValueRef BuildArrayLengthTable(LLVMBuilderRef builder, LLVMValueRef[] dimensions)
    {
        var elementType = coreLib.Int32;
        var arrayType = new ArrayType(elementType);
        var array = BuildAllocationSize(builder, builder.BuildAdd(
            LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(coreLib.Array), false),
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

    internal new LLVMValueRef GetArrayDataPointer(LLVMBuilderRef builder, LLVMValueRef array)
    {
        return builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, array, GetArrayDataField()));
    }

    internal new LLVMValueRef GetFieldAddress(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field,
        TypeReference? declaringType = null)
    {
        var offset = LLVMValueRef.CreateConstInt(sizeType, (ulong)(declaringType is null
            ? GetFieldOffset(field)
            : GetFieldOffsetForType(field, declaringType)), false);
        return builder.BuildGEP2(int8Type, obj, [offset]);
    }

    internal new Tuple<LLVMValueRef, LLVMTypeRef> CreateLocalStorage(LLVMBuilderRef builder, TypeReference type)
    {
        if (IsByReferenceValue(type))
        {
            var referenceType = GetLLVMTypeRef(type);
            var local = builder.BuildAlloca(referenceType);
            builder.BuildStore(LLVMValueRef.CreateConstNull(referenceType), local);
            return new(local, referenceType);
        }
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

    internal new void CopyValue(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef source, int size)
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

    internal new void StoreField(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, LLVMValueRef value)
    {
        if (IsValueType(field.FieldType))
        {
            CopyValue(builder, GetFieldAddress(builder, obj, field), value, GetTypeSize(field.FieldType));
            return;
        }
        var fieldType = GetLLVMTypeRef(field.FieldType);
        builder.BuildStore(ConvertValue(builder, value, fieldType), GetFieldAddress(builder, obj, field));
    }

    internal new FieldDefinition GetArrayLengthField()
    {
        return coreLib.ArrayLengthField;
    }

    internal new string GetRuntimeTypeKey(TypeReference type)
    {
        if (type is GenericInstanceType generic)
            return generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(GetRuntimeTypeKey)) + ">";
        return type.FullName;
    }

    internal new int GetRuntimeTypeId(TypeReference type)
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

    internal new void InitializeRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj, TypeReference type)
    {
        if (obj == default || IsValueType(type))
            return;
        StoreField(builder, obj, GetObjectTypeField(), GetRuntimeTypeObject(type));
        if (type is ArrayType)
        {
            StoreField(builder, obj, GetArrayDataField(), builder.BuildGEP2(int8Type, obj,
                [LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeDefinitionSize(coreLib.Array), false)]));
        }
    }

    internal new int GetBoxedObjectHeaderSize()
    {
        return GetTypeDefinitionSize(coreLib.Object);
    }

    internal new LLVMValueRef GetBoxedValueAddress(LLVMBuilderRef builder, LLVMValueRef box, TypeReference? valueType = null)
    {
        var offset = valueType is not null && GetEnumUnderlyingType(valueType) is not null
            ? GetFieldOffset(GetEnumValueField())
            : GetBoxedObjectHeaderSize();
        return builder.BuildGEP2(int8Type, box,
            [LLVMValueRef.CreateConstInt(sizeType, (ulong)offset, false)]);
    }

    internal new void InitializeBoxedRuntimeType(LLVMBuilderRef builder, LLVMValueRef box, TypeReference type)
    {
        StoreField(builder, box, GetObjectTypeField(), GetRuntimeTypeObject(type));
    }

    internal new LLVMValueRef GetRuntimeTypeObject(TypeReference type)
    {
        var key = GetRuntimeTypeKey(type);
        if (runtimeTypeObjects.TryGetValue(key, out var typeObject))
            return typeObject;

        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var typeDefinition = coreLib.Type;
        var fields = typeDefinition.Fields.Where(field => !field.IsStatic).ToArray();
        var storageType = context.GetStructType([
            pointerType,
            .. fields.Select(field => GetLLVMTypeRef(field.FieldType))
        ], false);
        typeObject = AddInternalGlobal(storageType, $"__runtime_type_{GetStableSymbolSuffix(key)}");
        runtimeTypeObjects.Add(key, typeObject);

        var definition = type.Resolve();
        var underlyingType = GetEnumUnderlyingType(type);
        var enumFields = definition?.Fields.Where(field => field.IsStatic && field.HasConstant)
            .Select(field => (Field: field, Value: GetEnumConstantValue(field.Constant, underlyingType)))
            .OrderBy(field => field.Value).ToArray() ?? [];
        var enumNames = enumFields.Select(field => field.Field.Name).ToArray();
        var enumValues = enumFields.Select(field => field.Value).ToArray();
        var isFlags = definition?.CustomAttributes.Any(attribute =>
            coreLib.IsFlagsAttribute(attribute.AttributeType)) == true;
        var isSigned = underlyingType?.MetadataType is MetadataType.SByte or MetadataType.Int16 or
            MetadataType.Int32 or MetadataType.Int64;

        var values = new List<LLVMValueRef>
        {
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(typeDefinition), pointerType)
        };
        foreach (var field in fields)
        {
            values.Add(field.Name switch
            {
                "Name" => LLVMValueRef.CreateConstPointerCast(GetStaticString(type.Name), pointerType),
                "Namespace" => string.IsNullOrEmpty(type.Namespace)
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticString(type.Namespace), pointerType),
                "FullName" => LLVMValueRef.CreateConstPointerCast(GetStaticString(type.FullName.Replace('/', '+')), pointerType),
                "RuntimeTypeId" => LLVMValueRef.CreateConstInt(int32Type, (ulong)GetRuntimeTypeId(type), false),
                "GCDescriptor" => LLVMValueRef.CreateConstPointerCast(GetGCDescriptor(type), pointerType),
                "EnumNames" => underlyingType is null
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticStringArray(enumNames), pointerType),
                "EnumValues" => underlyingType is null
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticUInt64Array(enumValues), pointerType),
                "IsFlagsEnum" => LLVMValueRef.CreateConstInt(int1Type, isFlags ? 1ul : 0ul, false),
                "IsSignedEnum" => LLVMValueRef.CreateConstInt(int1Type, isSigned ? 1ul : 0ul, false),
                _ => LLVMValueRef.CreateConstNull(GetLLVMTypeRef(field.FieldType))
            });
        }
        typeObject.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType, values.ToArray());
        return typeObject;
    }

    internal new ulong GetEnumConstantValue(object? value, TypeReference? underlyingType)
    {
        return underlyingType?.MetadataType switch
        {
            MetadataType.SByte => unchecked((ulong)Convert.ToSByte(value)),
            MetadataType.Int16 => unchecked((ulong)Convert.ToInt16(value)),
            MetadataType.Int32 => unchecked((ulong)Convert.ToInt32(value)),
            MetadataType.Int64 => unchecked((ulong)Convert.ToInt64(value)),
            MetadataType.Byte => Convert.ToByte(value),
            MetadataType.UInt16 => Convert.ToUInt16(value),
            MetadataType.UInt32 => Convert.ToUInt32(value),
            MetadataType.UInt64 => Convert.ToUInt64(value),
            _ => 0
        };
    }

    internal new LLVMValueRef GetStaticString(string value)
    {
        if (staticStrings.TryGetValue(value, out var result))
            return result;

        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var charValues = value.Select(character => LLVMValueRef.CreateConstInt(int16Type, character, false))
            .Append(LLVMValueRef.CreateConstNull(int16Type)).ToArray();
        var charDataType = LLVMTypeRef.CreateArray(int16Type, (uint)charValues.Length);
        var arrayStorageType = context.GetStructType([pointerType, int32Type, pointerType, pointerType, charDataType], false);
        var arrayStorage = AddInternalGlobal(arrayStorageType, $"__static_chars_{GetStableSymbolSuffix(value)}");
        var stringStorageType = context.GetStructType([pointerType, int32Type, pointerType], false);
        var stringStorage = AddInternalGlobal(stringStorageType, $"__static_string_{GetStableSymbolSuffix(value)}");
        staticStrings.Add(value, stringStorage);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataIndex = LLVMValueRef.CreateConstInt(int32Type, 4, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(arrayStorageType, arrayStorage, [zero, dataIndex, zero]);
        arrayStorage.Initializer = LLVMValueRef.CreateConstNamedStruct(arrayStorageType, [
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(new ArrayType(coreLib.Char)), pointerType),
            LLVMValueRef.CreateConstInt(int32Type, (ulong)value.Length, false),
            LLVMValueRef.CreateConstNull(pointerType),
            LLVMValueRef.CreateConstPointerCast(dataPointer, pointerType),
            LLVMValueRef.CreateConstArray(int16Type, charValues)
        ]);

        stringStorage.Initializer = LLVMValueRef.CreateConstNamedStruct(stringStorageType, [
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(coreLib.String), pointerType),
            LLVMValueRef.CreateConstInt(int32Type, (ulong)value.Length, false),
            LLVMValueRef.CreateConstPointerCast(arrayStorage, pointerType)
        ]);
        return stringStorage;
    }

    internal new LLVMValueRef GetStaticStringArray(string[] values)
    {
        var key = string.Join("\0", values);
        if (staticStringArrays.TryGetValue(key, out var result))
            return result;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var elementValues = values.Select(value => LLVMValueRef.CreateConstPointerCast(GetStaticString(value), pointerType)).ToArray();
        if (elementValues.Length == 0)
            elementValues = [LLVMValueRef.CreateConstNull(pointerType)];
        var dataType = LLVMTypeRef.CreateArray(pointerType, (uint)elementValues.Length);
        var storageType = context.GetStructType([pointerType, int32Type, pointerType, pointerType, dataType], false);
        result = AddInternalGlobal(storageType, $"__static_string_array_{GetStableSymbolSuffix(key)}");
        staticStringArrays.Add(key, result);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(storageType, result,
            [zero, LLVMValueRef.CreateConstInt(int32Type, 4, false), zero]);
        result.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType, [
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(new ArrayType(coreLib.String)), pointerType),
            LLVMValueRef.CreateConstInt(int32Type, (ulong)values.Length, false),
            LLVMValueRef.CreateConstNull(pointerType),
            LLVMValueRef.CreateConstPointerCast(dataPointer, pointerType),
            LLVMValueRef.CreateConstArray(pointerType, elementValues)
        ]);
        return result;
    }

    internal new LLVMValueRef GetStaticUInt64Array(ulong[] values)
    {
        var key = string.Join(",", values);
        if (staticUInt64Arrays.TryGetValue(key, out var result))
            return result;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var elementValues = values.Select(value => LLVMValueRef.CreateConstInt(int64Type, value, false)).ToArray();
        if (elementValues.Length == 0)
            elementValues = [LLVMValueRef.CreateConstNull(int64Type)];
        var dataType = LLVMTypeRef.CreateArray(int64Type, (uint)elementValues.Length);
        var storageType = context.GetStructType([pointerType, int32Type, pointerType, pointerType, dataType], false);
        result = AddInternalGlobal(storageType, $"__static_uint64_array_{GetStableSymbolSuffix(key)}");
        staticUInt64Arrays.Add(key, result);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(storageType, result,
            [zero, LLVMValueRef.CreateConstInt(int32Type, 4, false), zero]);
        result.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType, [
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(new ArrayType(coreLib.UInt64)), pointerType),
            LLVMValueRef.CreateConstInt(int32Type, (ulong)values.Length, false),
            LLVMValueRef.CreateConstNull(pointerType),
            LLVMValueRef.CreateConstPointerCast(dataPointer, pointerType),
            LLVMValueRef.CreateConstArray(int64Type, elementValues)
        ]);
        return result;
    }

    internal new LLVMValueRef GetObjectRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj)
    {
        return builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, obj, GetObjectTypeField()));
    }

    internal new LLVMValueRef GetObjectRuntimeTypeId(LLVMBuilderRef builder, LLVMValueRef obj)
    {
        return ConvertValue(builder, builder.BuildLoad2(int32Type,
            GetFieldAddress(builder, GetObjectRuntimeType(builder, obj), GetTypeRuntimeTypeIdField())), sizeType, false);
    }

    internal new (LLVMValueRef Function, LLVMValueRef State)? GetCctorGuard(TypeReference type)
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

    internal new LLVMValueRef GetGCDescriptor(TypeReference type)
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

        var gcDescType = coreLib.GCDesc;
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
            throw new InvalidOperationException($"{coreLib.GCDesc.FullName} must contain exactly six pointer-sized instance fields: TotalSlotCount, BaseSize, FixedReferenceCount, ArrayLengthOffset, ArrayElementSize, ArrayElementReferenceCount.");
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

    internal new void ValidateGCReferenceOffsets(TypeReference type, IEnumerable<int> offsets, string region)
    {
        foreach (var offset in offsets)
        {
            if ((uint)offset > ushort.MaxValue)
                throw new InvalidOperationException($"GC {region} reference offset for {type.FullName} does not fit in ushort: {offset}.");
        }
    }

    internal new IEnumerable<int> GetGCReferenceOffsets(TypeReference type)
    {
        var references = new List<int>();
        if (type is ArrayType)
            type = coreLib.Array;
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

    internal new TypeReference? GetClosedBaseType(TypeReference type)
    {
        var definition = type.Resolve();
        if (definition?.BaseType is null)
            return null;
        return type is GenericInstanceType genericType
            ? SubstituteGenericTypeArguments(definition.BaseType, genericType)
            : definition.BaseType;
    }

    internal new bool IsRuntimeTypeCompatible(TypeReference runtimeType, TypeReference targetType)
    {
        var targetDefinition = targetType.Resolve();
        var runtimeDefinition = runtimeType.Resolve();
        if (targetDefinition is null || runtimeDefinition is null)
            return false;
        if (runtimeDefinition.IsValueType && (coreLib.IsObject(targetType) || coreLib.IsValueType(targetType)))
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
