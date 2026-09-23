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
        var constructor = EnsureMethodRegistered(stringConstructor);
        builder.BuildCall2(constructor.Item2, constructor.Item1, [stringObject, array]);
        return stringObject;
    }

    internal new LLVMValueRef BuildAllocation(LLVMBuilderRef builder, int size)
    {
        return BuildAllocationSize(builder, LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
    }

    internal new LLVMValueRef BuildAllocationSize(LLVMBuilderRef builder, LLVMValueRef size)
    {
        var gcAllocate = EnsureMethodRegistered(coreLib.GCAllocateMethod);
        return builder.BuildCall2(gcAllocate.Item2, gcAllocate.Item1,
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
            LLVMValueRef.CreateConstInt(sizeType, (ulong)((dimensions.Length + 1) * GetTypeSize(elementType)), false)));
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
        CopyMemory(builder, destinationPointer, sourcePointer,
            LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, size), false));
    }

    internal new LLVMValueRef LoadValue(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder,
        LLVMValueRef address, TypeReference type, uint alignment = 0, bool isVolatile = false)
    {
        var storage = CreateLocalStorage(entryBuilder, type);
        var destination = builder.BuildLoad2(storage.Item2, storage.Item1);
        if (GetTypeSize(type) <= pointerSize || isVolatile)
        {
            var llvmType = GetCallType(type);
            var load = builder.BuildLoad2(llvmType,
                ConvertValue(builder, address, LLVMTypeRef.CreatePointer(llvmType, 0)));
            if (alignment != 0)
                load.Alignment = alignment;
            load.Volatile = isVolatile;
            builder.BuildStore(load,
                ConvertValue(builder, destination, LLVMTypeRef.CreatePointer(llvmType, 0)));
        }
        else
        {
            CopyValue(builder, destination, address, GetTypeSize(type));
        }
        return destination;
    }

    internal new void StoreValue(LLVMBuilderRef builder, LLVMValueRef address,
        LLVMValueRef value, TypeReference type, uint alignment = 0, bool isVolatile = false)
    {
        if (!IsValueType(type) || IsByReferenceValue(type))
        {
            var llvmType = GetLLVMTypeRef(type);
            var store = builder.BuildStore(ConvertValue(builder, value, llvmType),
                ConvertValue(builder, address, LLVMTypeRef.CreatePointer(llvmType, 0)));
            if (alignment != 0)
                store.Alignment = alignment;
            store.Volatile = isVolatile;
            return;
        }

        if (GetTypeSize(type) <= pointerSize)
        {
            var llvmType = GetCallType(type);
            if (value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind &&
                llvmType.Kind != LLVMTypeKind.LLVMPointerTypeKind)
                value = builder.BuildLoad2(llvmType,
                    ConvertValue(builder, value, LLVMTypeRef.CreatePointer(llvmType, 0)));
            var store = builder.BuildStore(ConvertValue(builder, value, llvmType),
                ConvertValue(builder, address, LLVMTypeRef.CreatePointer(llvmType, 0)));
            if (alignment != 0)
                store.Alignment = alignment;
            store.Volatile = isVolatile;
            return;
        }

        if (isVolatile)
        {
            var llvmType = GetCallType(type);
            if (value.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                value = builder.BuildLoad2(llvmType,
                    ConvertValue(builder, value, LLVMTypeRef.CreatePointer(llvmType, 0)));
            var store = builder.BuildStore(value,
                ConvertValue(builder, address, LLVMTypeRef.CreatePointer(llvmType, 0)));
            if (alignment != 0)
                store.Alignment = alignment;
            store.Volatile = true;
        }
        else
        {
            CopyValue(builder, address, value, GetTypeSize(type));
        }
    }

    internal new void CopyMemory(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef source,
        LLVMValueRef length)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        builder.BuildMemCpy(
            ConvertValue(builder, destination, pointerType), 1,
            ConvertValue(builder, source, pointerType), 1,
            ConvertValue(builder, length, sizeType, false));
    }

    internal new void FillMemory(LLVMBuilderRef builder, LLVMValueRef destination, LLVMValueRef value,
        LLVMValueRef length)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        builder.BuildMemSet(
            ConvertValue(builder, destination, pointerType),
            ConvertValue(builder, value, int8Type, false),
            ConvertValue(builder, length, sizeType, false), 1);
    }

    internal new void StoreField(LLVMBuilderRef builder, LLVMValueRef obj, FieldDefinition field, LLVMValueRef value)
    {
        StoreValue(builder, GetFieldAddress(builder, obj, field), value, field.FieldType);
    }

    internal new FieldDefinition GetArrayLengthField()
    {
        return coreLib.ArrayLengthField;
    }

    internal new string GetRuntimeTypeKey(TypeReference type)
    {
        if (type is GenericInstanceType generic)
        {
            if (generic.GenericArguments.Count > 0 &&
                generic.GenericArguments.All(argument => argument is GenericParameter))
                return generic.ElementType.FullName;
            return generic.ElementType.FullName + "<" + string.Join(",", generic.GenericArguments.Select(GetRuntimeTypeKey)) + ">";
        }
        return type.FullName;
    }

    internal new int GetRuntimeTypeId(TypeReference type, TypeReference? baseType = null)
    {
        var key = GetRuntimeTypeKey(type);
        if (!runtimeTypeIds.TryGetValue(key, out var id))
        {
            id = nextRuntimeTypeId++;
            runtimeTypeIds.Add(key, id);
        }
        runtimeTypes.TryAdd(key, type);
        if (baseType is not null)
            runtimeBaseTypes.TryAdd(key, baseType);
        return id;
    }

    internal new TypeDefinition? GetRuntimeTypeDefinition(TypeReference type)
    {
        return runtimeBaseTypes.TryGetValue(GetRuntimeTypeKey(type), out var runtimeBaseType)
            ? runtimeBaseType.Resolve()
            : type.Resolve();
    }

    internal new void InitializeRuntimeType(LLVMBuilderRef builder, LLVMValueRef obj, TypeReference type)
    {
        if (obj == default || IsValueType(type))
            return;
        StoreField(builder, obj, GetObjectTypeField(), GetRuntimeTypeObject(type));
        if (type is ArrayType arrayType)
        {
            StoreField(builder, obj, coreLib.ArrayElementSizeField,
                LLVMValueRef.CreateConstInt(int32Type, (ulong)GetTypeSize(arrayType.ElementType), false));
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
        var suffix = GetFriendlyTypeName(type);
        typeObject = AddInternalGlobal(storageType,
            RegisterGeneratedSymbol($"__runtime_type_{suffix}", key));
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
        var objectReferenceOffsets = GetGCReferenceOffsets(type).Distinct().Order().ToArray();
        var arrayElementReferenceOffsets = type is ArrayType arrayType
            ? IsManagedReferenceType(arrayType.ElementType)
                ? [0]
                : IsValueType(arrayType.ElementType)
                    ? GetGCReferenceOffsets(arrayType.ElementType).Distinct().Order().ToArray()
                    : []
            : [];

        var values = new List<LLVMValueRef>
        {
            LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(typeDefinition), pointerType)
        };
        foreach (var field in fields)
        {
            LLVMValueRef value;
            if (ReferenceEquals(field, coreLib.TypeNameField))
                value = LLVMValueRef.CreateConstPointerCast(GetStaticString(type.Name), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeNamespaceField))
                value = string.IsNullOrEmpty(type.Namespace)
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticString(type.Namespace), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeFullNameField))
                value = LLVMValueRef.CreateConstPointerCast(GetStaticString(type.FullName.Replace('/', '+')), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeRuntimeTypeIdField))
                value = LLVMValueRef.CreateConstInt(int32Type, (ulong)GetRuntimeTypeId(type), false);
            else if (ReferenceEquals(field, coreLib.TypeObjectReferenceOffsetsField))
                value = LLVMValueRef.CreateConstPointerCast(GetStaticInt32Array(objectReferenceOffsets), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeArrayElementReferenceOffsetsField))
                value = LLVMValueRef.CreateConstPointerCast(GetStaticInt32Array(arrayElementReferenceOffsets), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeEnumNamesField))
                value = underlyingType is null
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticStringArray(enumNames), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeEnumValuesField))
                value = underlyingType is null
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(GetStaticUInt64Array(enumValues), pointerType);
            else if (ReferenceEquals(field, coreLib.TypeIsFlagsEnumField))
                value = LLVMValueRef.CreateConstInt(GetLLVMTypeRef(field.FieldType), isFlags ? 1ul : 0ul, false);
            else if (ReferenceEquals(field, coreLib.TypeIsSignedEnumField))
                value = LLVMValueRef.CreateConstInt(GetLLVMTypeRef(field.FieldType), isSigned ? 1ul : 0ul, false);
            else if (ReferenceEquals(field, coreLib.TypeFactoryField))
            {
                var factory = GetRuntimeTypeFactory(type);
                value = factory == default
                    ? LLVMValueRef.CreateConstNull(pointerType)
                    : LLVMValueRef.CreateConstPointerCast(factory, pointerType);
            }
            else
                value = LLVMValueRef.CreateConstNull(GetLLVMTypeRef(field.FieldType));
            values.Add(value);
        }
        typeObject.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType, values.ToArray());
        return typeObject;
    }

    private LLVMValueRef GetRuntimeTypeFactory(TypeReference type)
    {
        var key = GetRuntimeTypeKey(type);
        if (runtimeTypeFactories.TryGetValue(key, out var existing))
            return existing;

        if (type is ArrayType)
        {
            runtimeTypeFactories.Add(key, default);
            return default;
        }

        var definition = type.Resolve();
        var isScalar = !IsValueType(type) && !IsManagedReferenceType(type);
        var constructorDefinition = definition?.Methods.FirstOrDefault(candidate =>
            candidate.IsConstructor && !candidate.IsStatic && candidate.Parameters.Count == 0 && candidate.IsPublic);
        if (!isScalar && !IsValueType(type) &&
            (definition is null || definition.IsInterface || definition.IsAbstract || constructorDefinition is null))
        {
            runtimeTypeFactories.Add(key, default);
            return default;
        }

        Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>? registeredClassConstructor = null;
        if (!isScalar && !IsValueType(type))
        {
            var constructor = BindMethodToDeclaringType(constructorDefinition!, type);
            registeredClassConstructor = GetRegisteredMethod(constructor);
            if (registeredClassConstructor is null)
            {
                runtimeTypeFactories.Add(key, default);
                return default;
            }
        }

        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var returnType = GetCallType(type);
        var factoryType = LLVMTypeRef.CreateFunction(returnType, [pointerType]);
        var suffix = GetFriendlyTypeName(type);
        var factory = module.AddFunction(
            RegisterGeneratedSymbol($"__type_factory_{suffix}", key), factoryType);
        factory.FunctionCallConv = (uint)LLVMCallConv.LLVMCCallConv;
        factory.Linkage = LLVMLinkage.LLVMInternalLinkage;
        var builder = context.CreateBuilder();
        builder.PositionAtEnd(factory.AppendBasicBlock("entry"));

        if (isScalar)
        {
            builder.BuildRet(LLVMValueRef.CreateConstNull(returnType));
            builder.Dispose();
            return CreateFactoryDelegate();
        }

        if (IsValueType(type))
        {
            var storageType = LLVMTypeRef.CreateArray(int8Type, (uint)Math.Max(1, GetTypeSize(type)));
            var storage = builder.BuildAlloca(storageType);
            FillMemory(builder, storage, LLVMValueRef.CreateConstNull(int8Type),
                LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(type)), false));
            if (constructorDefinition is not null)
            {
                var constructor = BindMethodToDeclaringType(constructorDefinition, type);
                if (GetRegisteredMethod(constructor) is { } registered)
                    builder.BuildCall2(registered.Item2, registered.Item1,
                        [builder.BuildBitCast(storage, LLVMTypeRef.CreatePointer(int8Type, 0))]);
            }
            builder.BuildRet(builder.BuildLoad2(returnType,
                builder.BuildBitCast(storage, LLVMTypeRef.CreatePointer(returnType, 0))));
            builder.Dispose();
            return CreateFactoryDelegate();
        }

        var instance = BuildAllocation(builder, GetObjectSize(type));
        InitializeRuntimeType(builder, instance, type);
        builder.BuildCall2(registeredClassConstructor!.Item2, registeredClassConstructor.Item1, [instance]);
        builder.BuildRet(instance);
        builder.Dispose();
        return CreateFactoryDelegate();

        LLVMValueRef CreateFactoryDelegate()
        {
            var delegateType = new GenericInstanceType(coreLib.Func);
            delegateType.GenericArguments.Add(type);
            var fields = GetObjectLayoutFields(coreLib.Func);
            var storageType = context.GetStructType(
                fields.Select(field => GetLLVMTypeRef(field.FieldType)).ToArray(), false);
            var storage = AddInternalGlobal(storageType,
                RegisterGeneratedSymbol($"__type_factory_delegate_{suffix}", key));
            runtimeTypeFactories.Add(key, storage);
            storage.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType,
                fields.Select(field =>
                {
                    if (ReferenceEquals(field, coreLib.ObjectTypeField))
                        return LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(delegateType), pointerType);
                    if (ReferenceEquals(field, coreLib.DelegateFunctionField))
                        return LLVMValueRef.CreateConstPointerCast(factory, GetLLVMTypeRef(field.FieldType));
                    return LLVMValueRef.CreateConstNull(GetLLVMTypeRef(field.FieldType));
                }).ToArray());
            return storage;
        }
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
        var arrayFields = GetObjectLayoutFields(coreLib.Array);
        var arrayStorageType = context.GetStructType(
            [.. arrayFields.Select(field => GetLLVMTypeRef(field.FieldType)), charDataType], false);
        var index = staticStrings.Count;
        var arrayStorage = AddInternalGlobal(arrayStorageType, $"__static_chars_{index}");
        var stringFields = GetObjectLayoutFields(coreLib.String);
        var stringStorageType = context.GetStructType(
            stringFields.Select(field => GetLLVMTypeRef(field.FieldType)).ToArray(), false);
        var stringStorage = AddInternalGlobal(stringStorageType, $"__static_string_{index}");
        staticStrings.Add(value, stringStorage);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataIndex = LLVMValueRef.CreateConstInt(int32Type, (ulong)arrayFields.Length, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(arrayStorageType, arrayStorage, [zero, dataIndex, zero]);
        var charArrayType = new ArrayType(coreLib.Char);
        arrayStorage.Initializer = LLVMValueRef.CreateConstNamedStruct(arrayStorageType,
            [.. GetStaticArrayHeaderValues(arrayFields, charArrayType, value.Length, dataPointer),
             LLVMValueRef.CreateConstArray(int16Type, charValues)]);

        stringStorage.Initializer = LLVMValueRef.CreateConstNamedStruct(stringStorageType,
            stringFields.Select(field =>
            {
                if (ReferenceEquals(field, coreLib.ObjectTypeField))
                    return LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(coreLib.String), pointerType);
                if (ReferenceEquals(field, coreLib.StringLengthField))
                    return LLVMValueRef.CreateConstInt(GetLLVMTypeRef(field.FieldType), (ulong)value.Length, false);
                if (ReferenceEquals(field, coreLib.StringCharsField))
                    return LLVMValueRef.CreateConstPointerCast(arrayStorage, GetLLVMTypeRef(field.FieldType));
                return LLVMValueRef.CreateConstNull(GetLLVMTypeRef(field.FieldType));
            }).ToArray());
        return stringStorage;
    }

    internal new LLVMValueRef GetStaticStringArray(string[] values)
    {
        var key = string.Join("\0", values);
        if (staticStringArrays.TryGetValue(key, out var result))
            return result;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var elementValues = values.Select(value => LLVMValueRef.CreateConstPointerCast(GetStaticString(value), pointerType))
            .Append(LLVMValueRef.CreateConstNull(pointerType)).ToArray();
        var dataType = LLVMTypeRef.CreateArray(pointerType, (uint)elementValues.Length);
        var arrayFields = GetObjectLayoutFields(coreLib.Array);
        var storageType = context.GetStructType(
            [.. arrayFields.Select(field => GetLLVMTypeRef(field.FieldType)), dataType], false);
        result = AddInternalGlobal(storageType, $"__static_string_array_{staticStringArrays.Count}");
        staticStringArrays.Add(key, result);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(storageType, result,
            [zero, LLVMValueRef.CreateConstInt(int32Type, (ulong)arrayFields.Length, false), zero]);
        result.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType,
            [.. GetStaticArrayHeaderValues(arrayFields, new ArrayType(coreLib.String), values.Length, dataPointer),
             LLVMValueRef.CreateConstArray(pointerType, elementValues)]);
        return result;
    }

    internal new LLVMValueRef GetStaticUInt64Array(ulong[] values)
    {
        var key = string.Join(",", values);
        if (staticUInt64Arrays.TryGetValue(key, out var result))
            return result;
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var elementValues = values.Select(value => LLVMValueRef.CreateConstInt(int64Type, value, false))
            .Append(LLVMValueRef.CreateConstNull(int64Type)).ToArray();
        var dataType = LLVMTypeRef.CreateArray(int64Type, (uint)elementValues.Length);
        var arrayFields = GetObjectLayoutFields(coreLib.Array);
        var storageType = context.GetStructType(
            [.. arrayFields.Select(field => GetLLVMTypeRef(field.FieldType)), dataType], false);
        result = AddInternalGlobal(storageType, $"__static_uint64_array_{staticUInt64Arrays.Count}");
        staticUInt64Arrays.Add(key, result);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(storageType, result,
            [zero, LLVMValueRef.CreateConstInt(int32Type, (ulong)arrayFields.Length, false), zero]);
        result.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType,
            [.. GetStaticArrayHeaderValues(arrayFields, new ArrayType(coreLib.UInt64), values.Length, dataPointer),
             LLVMValueRef.CreateConstArray(int64Type, elementValues)]);
        return result;
    }

    internal new LLVMValueRef GetStaticInt32Array(int[] values)
    {
        var key = string.Join(",", values);
        if (staticInt32Arrays.TryGetValue(key, out var result))
            return result;
        var elementValues = values.Select(value => LLVMValueRef.CreateConstInt(int32Type, (ulong)value, true))
            .Append(LLVMValueRef.CreateConstNull(int32Type)).ToArray();
        var dataType = LLVMTypeRef.CreateArray(int32Type, (uint)elementValues.Length);
        var arrayFields = GetObjectLayoutFields(coreLib.Array);
        var storageType = context.GetStructType(
            [.. arrayFields.Select(field => GetLLVMTypeRef(field.FieldType)), dataType], false);
        result = AddInternalGlobal(storageType, $"__static_int32_array_{staticInt32Arrays.Count}");
        staticInt32Arrays.Add(key, result);
        var zero = LLVMValueRef.CreateConstInt(int32Type, 0, false);
        var dataPointer = LLVMValueRef.CreateConstGEP2(storageType, result,
            [zero, LLVMValueRef.CreateConstInt(int32Type, (ulong)arrayFields.Length, false), zero]);
        result.Initializer = LLVMValueRef.CreateConstNamedStruct(storageType,
            [.. GetStaticArrayHeaderValues(arrayFields, new ArrayType(coreLib.Int32), values.Length, dataPointer),
             LLVMValueRef.CreateConstArray(int32Type, elementValues)]);
        return result;
    }

    private FieldDefinition[] GetObjectLayoutFields(TypeDefinition type)
    {
        var fields = new List<FieldDefinition>();
        AddFields(type);
        return fields.ToArray();

        void AddFields(TypeDefinition current)
        {
            if (current.BaseType?.Resolve() is { } baseType)
                AddFields(baseType);
            fields.AddRange(current.Fields.Where(field => !field.IsStatic));
        }
    }

    private LLVMValueRef[] GetStaticArrayHeaderValues(FieldDefinition[] fields, ArrayType arrayType, int length,
        LLVMValueRef dataPointer)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        return fields.Select(field =>
        {
            if (ReferenceEquals(field, coreLib.ObjectTypeField))
                return LLVMValueRef.CreateConstPointerCast(GetRuntimeTypeObject(arrayType), pointerType);
            if (ReferenceEquals(field, coreLib.ArrayLengthField))
                return LLVMValueRef.CreateConstInt(GetLLVMTypeRef(field.FieldType), (ulong)length, false);
            if (ReferenceEquals(field, coreLib.ArrayElementSizeField))
                return LLVMValueRef.CreateConstInt(GetLLVMTypeRef(field.FieldType),
                    (ulong)GetTypeSize(arrayType.ElementType), false);
            if (ReferenceEquals(field, coreLib.ArrayDataField))
                return LLVMValueRef.CreateConstPointerCast(dataPointer, GetLLVMTypeRef(field.FieldType));
            return LLVMValueRef.CreateConstNull(GetLLVMTypeRef(field.FieldType));
        }).ToArray();
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
        var key = GetRuntimeTypeKey(type);
        if (cctorGuards.TryGetValue(key, out var existing))
            return existing;

        var cctorDefinition = definition.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic);
        if (cctorDefinition?.HasBody != true)
            return null;
        MethodReference cctorReference = type is GenericInstanceType
            ? BindMethodToDeclaringType(cctorDefinition, type)
            : cctorDefinition;
        var cctor = GetRegisteredMethod(cctorReference);
        if (cctor is null)
        {
            RegisterMethodFunction(module, cctorReference, cctorDefinition.Body.Instructions);
            cctor = GetRegisteredMethod(cctorReference);
        }
        if (cctor is null || cctor.Item1 == default)
            return null;

        var suffix = GetFriendlyTypeName(type);
        var state = AddInternalGlobal(int8Type, RegisterGeneratedSymbol($"__cctor_state_{suffix}", key));
        state.Initializer = LLVMValueRef.CreateConstNull(int8Type);
        var guardType = LLVMTypeRef.CreateFunction(voidType, []);
        var guard = module.AddFunction(RegisterGeneratedSymbol($"__cctor_guard_{suffix}", key), guardType);
        guard.FunctionCallConv = (uint)LLVMCallConv.LLVMCCallConv;
        guard.Linkage = LLVMLinkage.LLVMInternalLinkage;
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

    internal new IEnumerable<int> GetGCReferenceOffsets(TypeReference type)
    {
        var references = new List<int>();
        var objectHeaderSize = IsManagedReferenceType(type) ? GetObjectHeaderSize() : 0;
        if (type is ArrayType)
            type = coreLib.Array;
        Collect(type, 0, true);
        return references.Where(offset => offset >= objectHeaderSize)
            .Select(offset => offset - objectHeaderSize);

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
                if (fieldType is ByReferenceType)
                    references.Add(fieldOffset);
                else if (IsManagedReferenceType(fieldType))
                    references.Add(fieldOffset);
                else if (IsValueType(fieldType))
                    Collect(fieldType, fieldOffset, false);
            }
        }
    }

    internal new TypeReference? GetClosedBaseType(TypeReference type)
    {
        if (runtimeBaseTypes.TryGetValue(GetRuntimeTypeKey(type), out var runtimeBaseType))
            return runtimeBaseType;
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
