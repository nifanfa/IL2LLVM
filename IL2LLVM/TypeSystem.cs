sealed partial class Translator
{
    List<(TypeReference RuntimeType, MethodReference Implementation)> GetVirtualImplementations(
        MethodReference targetMethod, TypeReference contractType)
    {
        var implementations = new List<(TypeReference RuntimeType, MethodReference Implementation)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in localTypes.Values.Where(candidate => !candidate.IsInterface)
                     .OrderByDescending(GetTypeDepth))
        {
            TypeReference runtimeType = type;
            if (type.HasGenericParameters &&
                TryCloseRuntimeType(type, contractType, out var closedType))
                runtimeType = closedType;
            var implementation = FindMethodImplementation(runtimeType, targetMethod);
            if (implementation is null || FindLocalMethod(implementation, localMethods)?.HasBody != true ||
                !seen.Add(GetRuntimeTypeKey(runtimeType)))
                continue;
            implementations.Add((runtimeType, implementation));
        }
        return implementations;
    }

    bool IsKnownRuntimeType(TypeReference type)
    {
        return type.Resolve() is { } definition && localTypes.ContainsKey(definition.FullName);
    }

    int GetTypeDepth(TypeDefinition type)
    {
        int depth = 0;
        for (TypeReference? current = type; GetClosedBaseType(current) is not null; current = GetClosedBaseType(current)!)
            depth++;
        return depth;
    }

    FieldDefinition GetDelegateField(TypeReference type, string name)
    {
        var current = type.Resolve();
        while (current is not null)
        {
            var field = current.Fields.FirstOrDefault(candidate => candidate.Name == name && !candidate.IsStatic);
            if (field is not null)
                return field;
            current = current.BaseType?.Resolve();
        }
        throw new NotSupportedException($"Delegate field is not defined: {type.FullName}.{name}");
    }

    bool IsDelegateType(TypeReference type)
    {
        for (var current = type.Resolve(); current is not null;)
        {
            if (current.FullName == "System.Delegate" || current.FullName == "System.MulticastDelegate")
                return true;
            if (current.BaseType is null)
                break;
            current = current.BaseType.Resolve();
        }
        return false;
    }

    LLVMTypeRef GetLLVMTypeRef(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetLLVMTypeRef(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetLLVMTypeRef(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetLLVMTypeRef(pinned.ElementType);
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetLLVMTypeRef(enumUnderlyingType);
        if (type is ByReferenceType byReference)
            return LLVMTypeRef.CreatePointer(GetLLVMTypeRef(byReference.ElementType), 0);
        if (type is PointerType)
            return LLVMTypeRef.CreatePointer(int8Type, 0);
        if (type is ArrayType)
            return LLVMTypeRef.CreatePointer(int8Type, 0);
        if (type is GenericParameter)
            return sizeType;
        return GetLLVMTypeRefFromMetadataType(type.MetadataType);
    }

    bool IsVoidType(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return IsVoidType(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return IsVoidType(optionalModifier.ElementType);
        return type.MetadataType == MetadataType.Void;
    }

    TypeReference? GetEnumUnderlyingType(TypeReference type)
    {
        var resolved = type.Resolve();
        return resolved is { IsEnum: true } definition
            ? definition.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType
            : null;
    }

    bool IsNoReturnMethod(MethodReference method, Dictionary<string, MethodDefinition> methods)
    {
        var definition = FindLocalMethod(method, methods);
        if (definition is null || !definition.HasBody || definition.Body.Instructions.Count == 0)
            return false;
        var last = definition.Body.Instructions.LastOrDefault(instruction => instruction.OpCode.Code is not Code.Nop);
        return last?.OpCode.Code is Code.Throw or Code.Rethrow;
    }

    MethodDefinition GetRequiredConstructor(TypeDefinition type, params TypeReference[] parameterTypes)
    {
        var matches = type.Methods.Where(method => method.IsConstructor && !method.IsStatic &&
            method.Parameters.Count == parameterTypes.Length &&
            method.Parameters.Select(parameter => parameter.ParameterType).Zip(parameterTypes)
                .All(pair => SameType(pair.First, pair.Second))).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one matching constructor on {type.FullName}, found {matches.Count}.");
    }

    MethodDefinition GetRequiredMethod(TypeDefinition type, string name, bool hasThis,
        TypeReference returnType, params TypeReference[] parameterTypes)
    {
        var matches = type.Methods.Where(method => method.Name == name && method.HasThis == hasThis &&
            SameType(method.ReturnType, returnType) && method.Parameters.Count == parameterTypes.Length &&
            method.Parameters.Select(parameter => parameter.ParameterType).Zip(parameterTypes)
                .All(pair => SameType(pair.First, pair.Second))).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one matching method named {name} on {type.FullName}, found {matches.Count}.");
    }

    MethodDefinition? FindLocalMethod(MethodReference reference, Dictionary<string, MethodDefinition> methods)
    {
        if (methods.TryGetValue(reference.FullName, out var exact))
            return exact;
        var resolved = reference.Resolve();
        if (resolved is not null && methods.TryGetValue(resolved.FullName, out exact))
            return exact;
        return methods.Values.FirstOrDefault(candidate =>
        {
            if (!SameTypeDefinition(candidate.DeclaringType, reference.DeclaringType))
                return false;
            var bound = reference.DeclaringType is GenericInstanceType
                ? BindMethodToDeclaringType(candidate, reference.DeclaringType, reference)
                : candidate;
            return SameMethodSignature(bound, reference);
        });
    }

    MethodReference SpecializeMethodReference(MethodReference reference, MethodReference context)
    {
        var elementMethod = reference is GenericInstanceMethod genericReference
            ? genericReference.ElementMethod
            : reference;
        var declaringType = ResolveGenericType(elementMethod.DeclaringType, context);
        var specialized = new MethodReference(elementMethod.Name,
            ResolveGenericType(elementMethod.ReturnType, context), declaringType)
        {
            HasThis = elementMethod.HasThis,
            ExplicitThis = elementMethod.ExplicitThis,
            CallingConvention = elementMethod.CallingConvention
        };
        foreach (var parameter in elementMethod.Parameters)
            specialized.Parameters.Add(new ParameterDefinition(ResolveGenericType(parameter.ParameterType, context)));
        foreach (var parameter in elementMethod.GenericParameters)
            specialized.GenericParameters.Add(new GenericParameter(parameter.Name, specialized));
        if (reference is not GenericInstanceMethod genericMethod)
            return specialized;
        var genericInstance = new GenericInstanceMethod(specialized);
        foreach (var argument in genericMethod.GenericArguments)
            genericInstance.GenericArguments.Add(ResolveGenericType(argument, context));
        return genericInstance;
    }

    TypeReference SubstituteGenericParameter(TypeReference type, MethodReference method)
    {
        if (type is not GenericParameter parameter)
            return ResolveGenericType(type, method);
        if (parameter.Type == GenericParameterType.Type && method.DeclaringType is GenericInstanceType declaringType && parameter.Position < declaringType.GenericArguments.Count)
            return declaringType.GenericArguments[parameter.Position];
        if (parameter.Type == GenericParameterType.Method && method is GenericInstanceMethod genericMethod && parameter.Position < genericMethod.GenericArguments.Count)
            return genericMethod.GenericArguments[parameter.Position];
        return type;
    }

    TypeReference? GetMethodVariableType(MethodReference method, int index)
    {
        var definition = method.Resolve();
        if (definition?.HasBody != true || index < 0 || index >= definition.Body.Variables.Count)
            return null;
        return SubstituteGenericParameter(definition.Body.Variables[index].VariableType, method);
    }

    FieldDefinition GetObjectTypeDescriptorField()
    {
        return localTypes["System.Object"].Fields.First(field => field.Name == "m_pTypeDescriptor");
    }

    FieldDefinition GetTypeDescriptorRuntimeTypeIdField()
    {
        return localTypes["System.TypeDescriptor"].Fields.First(field => field.Name == "RuntimeTypeId");
    }

    FieldDefinition GetTypeDescriptorGCDescriptorField()
    {
        return localTypes["System.TypeDescriptor"].Fields.First(field => field.Name == "GCDescriptor");
    }

    FieldDefinition GetTypeDescriptorTypeField()
    {
        return localTypes["System.TypeDescriptor"].Fields.First(field => field.Name == "Type");
    }

    int GetObjectHeaderSize()
    {
        return localTypes["System.Object"].Fields.Where(field => !field.IsStatic)
            .Max(field => GetFieldOffset(field) + GetTypeSize(field.FieldType));
    }

    FieldDefinition GetLocalField(FieldReference field)
    {
        var resolvedField = field.Resolve();
        if (resolvedField is not null && localTypes.ContainsKey(resolvedField.DeclaringType.FullName))
            return resolvedField;
        var declaringType = field.DeclaringType is GenericInstanceType genericType
            ? genericType.ElementType
            : field.DeclaringType;
        if (!localTypes.TryGetValue(declaringType.FullName, out var type))
            throw new NotSupportedException($"Field is not defined in the input module: {field.FullName}");
        while (true)
        {
            var definition = type.Fields.FirstOrDefault(candidate => candidate.Name == field.Name);
            if (definition is not null)
                return definition;
            if (type.BaseType is null || !localTypes.TryGetValue(type.BaseType.FullName, out type))
                break;
        }
        throw new NotSupportedException($"Field is not defined in the input module: {field.FullName}");
    }

    TypeReference SubstituteFieldType(FieldReference field, MethodReference? context = null)
    {
        var resolved = field.Resolve();
        var declaringType = field.DeclaringType as GenericInstanceType;
        var contextDeclaringType = context?.DeclaringType as GenericInstanceType;
        if (contextDeclaringType is not null && (declaringType is null || declaringType.GenericArguments.Any(argument => argument is GenericParameter)))
            declaringType = contextDeclaringType;
        var fieldType = resolved?.FieldType ?? field.FieldType;
        if (declaringType is not null)
        {
            if (fieldType is GenericParameter parameter && parameter.Position < declaringType.GenericArguments.Count)
                fieldType = declaringType.GenericArguments[parameter.Position];
            else if (fieldType is GenericInstanceType fieldGeneric && fieldGeneric.GenericArguments.Any(argument => argument is GenericParameter))
            {
                var arguments = fieldGeneric.GenericArguments.Select(argument => argument is GenericParameter genericParameter &&
                    genericParameter.Position < declaringType.GenericArguments.Count
                        ? declaringType.GenericArguments[genericParameter.Position]
                        : argument).ToArray();
                fieldType = new GenericInstanceType(fieldGeneric.ElementType);
                for (int i = 0; i < arguments.Length; i++)
                    ((GenericInstanceType)fieldType).GenericArguments.Add(arguments[i]);
            }
        }
        return context is null ? fieldType : SubstituteGenericParameter(fieldType, context);
    }

    TypeReference ResolveGenericType(TypeReference type, MethodReference context)
    {
        if (type is GenericParameter parameter)
        {
            if (parameter.Type == GenericParameterType.Type && parameter.Owner is TypeReference parameterType &&
                context.DeclaringType is GenericInstanceType declaring &&
                SameTypeDefinition(parameterType, declaring.ElementType) && parameter.Position < declaring.GenericArguments.Count)
                return declaring.GenericArguments[parameter.Position];
            if (parameter.Type == GenericParameterType.Method && parameter.Owner is MethodReference parameterMethod &&
                context is GenericInstanceMethod method && SameMethodDefinition(parameterMethod, method.ElementMethod) &&
                parameter.Position < method.GenericArguments.Count)
                return method.GenericArguments[parameter.Position];
        }
        if (type is GenericInstanceType generic)
        {
            var result = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                result.GenericArguments.Add(ResolveGenericType(argument, context));
            return result;
        }
        if (type is ArrayType array)
            return new ArrayType(ResolveGenericType(array.ElementType, context), array.Rank);
        if (type is ByReferenceType byReference)
            return new ByReferenceType(ResolveGenericType(byReference.ElementType, context));
        if (type is PointerType pointer)
            return new PointerType(ResolveGenericType(pointer.ElementType, context));
        if (type is RequiredModifierType requiredModifier)
            return new RequiredModifierType(requiredModifier.ModifierType, ResolveGenericType(requiredModifier.ElementType, context));
        if (type is OptionalModifierType optionalModifier)
            return new OptionalModifierType(optionalModifier.ModifierType, ResolveGenericType(optionalModifier.ElementType, context));
        if (type is PinnedType pinned)
            return new PinnedType(ResolveGenericType(pinned.ElementType, context));
        return type;
    }

    TypeReference SubstituteGenericTypeArguments(TypeReference type, GenericInstanceType declaringType)
    {
        if (type is GenericParameter parameter && parameter.Type == GenericParameterType.Type &&
            parameter.Position < declaringType.GenericArguments.Count)
            return declaringType.GenericArguments[parameter.Position];
        if (type is GenericInstanceType generic)
        {
            var result = new GenericInstanceType(generic.ElementType);
            foreach (var argument in generic.GenericArguments)
                result.GenericArguments.Add(SubstituteGenericTypeArguments(argument, declaringType));
            return result;
        }
        if (type is ArrayType array)
            return new ArrayType(SubstituteGenericTypeArguments(array.ElementType, declaringType), array.Rank);
        if (type is ByReferenceType byReference)
            return new ByReferenceType(SubstituteGenericTypeArguments(byReference.ElementType, declaringType));
        if (type is PointerType pointer)
            return new PointerType(SubstituteGenericTypeArguments(pointer.ElementType, declaringType));
        return type;
    }

    int GetFieldOffset(FieldDefinition field)
    {
        return GetFieldOffsetForType(field, field.DeclaringType);
    }

    int GetFieldOffsetForType(FieldDefinition field, TypeReference declaringType)
    {
        var definition = declaringType.Resolve() ?? field.DeclaringType;
        var baseType = GetClosedBaseType(declaringType);
        var offset = IsValueType(definition) || baseType is null
            ? 0
            : GetObjectSize(baseType);
        foreach (var candidate in definition.Fields.TakeWhile(candidate => candidate.Name != field.Name).Where(candidate => !candidate.IsStatic))
        {
            var candidateType = declaringType is GenericInstanceType genericType
                ? SubstituteGenericTypeArguments(candidate.FieldType, genericType)
                : candidate.FieldType;
            offset = AlignUp(offset, GetTypeAlignment(candidateType));
            offset += GetTypeSize(candidateType);
        }
        var fieldType = declaringType is GenericInstanceType genericDeclaringType
            ? SubstituteGenericTypeArguments(field.FieldType, genericDeclaringType)
            : field.FieldType;
        return AlignUp(offset, GetTypeAlignment(fieldType));
    }

    int GetBaseTypeSize(TypeReference? type)
    {
        if (type is null)
            return 0;
        var definition = type.Resolve();
        if (definition is not null && localTypes.ContainsKey(definition.FullName) && !IsValueType(type))
            return GetObjectSize(type);
        return definition is not null && localTypes.ContainsKey(definition.FullName)
            ? GetTypeDefinitionSize(definition)
            : GetMetadataTypeSize(type.MetadataType);
    }

    int GetObjectSize(TypeReference type)
    {
        var definition = type.Resolve();
        if (definition is null || definition.IsInterface || IsValueType(type))
            return GetTypeSize(type);

        var baseType = GetClosedBaseType(type);
        var offset = baseType is null ? 0 : GetObjectSize(baseType);
        var alignment = baseType is null ? 1 : GetTypeAlignment(baseType);
        foreach (var field in definition.Fields.Where(field => !field.IsStatic))
        {
            var fieldType = type is GenericInstanceType genericType
                ? SubstituteGenericTypeArguments(field.FieldType, genericType)
                : field.FieldType;
            offset = AlignUp(offset, GetTypeAlignment(fieldType));
            offset += GetTypeSize(fieldType);
            alignment = Math.Max(alignment, GetTypeAlignment(fieldType));
        }
        if (SameTypeDefinition(definition, GetObjectTypeDescriptorField().DeclaringType))
            offset = Math.Max(offset, GetObjectHeaderSize());
        return Math.Max(AlignUp(offset, alignment), definition.ClassSize);
    }

    int GetTypeSize(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetTypeSize(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetTypeSize(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetTypeSize(pinned.ElementType);
        if (type is GenericInstanceType genericInstance && localTypes.TryGetValue(genericInstance.ElementType.FullName, out var genericDefinition))
        {
            if (!IsValueType(genericDefinition))
                return pointerSize;
            var offset = 0;
            var alignment = 1;
            foreach (var field in genericDefinition.Fields.Where(field => !field.IsStatic))
            {
                var fieldType = SubstituteGenericTypeArguments(field.FieldType, genericInstance);
                var fieldAlignment = GetTypeAlignment(fieldType);
                offset = AlignUp(offset, fieldAlignment);
                offset += GetTypeSize(fieldType);
                alignment = Math.Max(alignment, fieldAlignment);
            }
            return Math.Max(AlignUp(offset, alignment), genericDefinition.ClassSize);
        }
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetTypeSize(enumUnderlyingType);
        if (type.MetadataType is MetadataType.IntPtr or MetadataType.UIntPtr)
            return pointerSize;
        if (type.MetadataType is MetadataType.Boolean or MetadataType.SByte or MetadataType.Byte or MetadataType.Char or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double)
            return GetMetadataTypeSize(type.MetadataType);
        if (IsValueType(type) && localTypes.TryGetValue(type.FullName, out var valueTypeDefinition))
            return GetTypeDefinitionSize(valueTypeDefinition);
        if (type.MetadataType is MetadataType.Object or MetadataType.Array or MetadataType.Class or MetadataType.String or MetadataType.Pointer or MetadataType.ByReference)
            return pointerSize;
        if (localTypes.TryGetValue(type.FullName, out var definition))
            return GetTypeDefinitionSize(definition);
        return GetMetadataTypeSize(type.MetadataType);
    }

    int GetTypeDefinitionSize(TypeDefinition type)
    {
        var offset = IsValueType(type) || type.BaseType is null ? 0 : GetBaseTypeSize(type.BaseType);
        var alignment = GetTypeDefinitionAlignment(type);
        foreach (var field in type.Fields.Where(field => !field.IsStatic))
        {
            var fieldAlignment = GetTypeAlignment(field.FieldType);
            offset = AlignUp(offset, fieldAlignment);
            offset += GetTypeSize(field.FieldType);
            alignment = Math.Max(alignment, fieldAlignment);
        }
        if (SameTypeDefinition(type, GetObjectTypeDescriptorField().DeclaringType))
            offset = Math.Max(offset, GetObjectHeaderSize());
        return Math.Max(AlignUp(offset, alignment), type.ClassSize);
    }

    int GetTypeDefinitionAlignment(TypeDefinition type)
    {
        var alignment = IsValueType(type) || type.BaseType is null ? 1 : GetTypeAlignment(type.BaseType);
        foreach (var field in type.Fields.Where(field => !field.IsStatic))
            alignment = Math.Max(alignment, GetTypeAlignment(field.FieldType));
        return alignment;
    }

    bool IsValueType(TypeReference type)
    {
        if (type.FullName is "System.ValueType" or "System.Enum")
            return false;
        if (type.Resolve()?.IsEnum == true)
            return false;
        if (type.MetadataType is MetadataType.Void or MetadataType.Boolean or MetadataType.Char or
            MetadataType.SByte or MetadataType.Byte or MetadataType.Int16 or MetadataType.UInt16 or
            MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or
            MetadataType.Single or MetadataType.Double or MetadataType.IntPtr or MetadataType.UIntPtr)
            return false;
        if (type is GenericInstanceType generic)
            return generic.ElementType.Resolve()?.IsValueType == true;
        return type.MetadataType == MetadataType.ValueType ||
            localTypes.TryGetValue(type.FullName, out var definition) && definition.IsValueType;
    }

    bool IsManagedReferenceType(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return IsManagedReferenceType(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return IsManagedReferenceType(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return IsManagedReferenceType(pinned.ElementType);
        if (type.FullName is "System.IntPtr" or "System.UIntPtr")
            return false;
        if (type is ArrayType || type.MetadataType is MetadataType.Object or MetadataType.Class or MetadataType.String or MetadataType.Array)
            return true;
        if (type is ByReferenceType or PointerType || type.MetadataType is MetadataType.IntPtr or MetadataType.UIntPtr ||
            GetEnumUnderlyingType(type) is not null)
            return false;
        return type.Resolve() is { IsValueType: false };
    }

    int GetTypeAlignment(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetTypeAlignment(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetTypeAlignment(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetTypeAlignment(pinned.ElementType);
        var enumUnderlyingType = GetEnumUnderlyingType(type);
        if (enumUnderlyingType is not null)
            return GetTypeAlignment(enumUnderlyingType);
        if (type is GenericInstanceType genericInstance && IsValueType(genericInstance))
        {
            var genericDefinition = genericInstance.ElementType.Resolve();
            var alignment = 1;
            foreach (var field in genericDefinition?.Fields.Where(field => !field.IsStatic) ?? [])
                alignment = Math.Max(alignment, GetTypeAlignment(SubstituteGenericTypeArguments(field.FieldType, genericInstance)));
            return alignment;
        }
        if (IsValueType(type) && localTypes.TryGetValue(type.FullName, out var definition))
            return GetTypeDefinitionAlignment(definition);
        return (int)machine.CreateTargetDataLayout().ABIAlignmentOfType(GetLLVMTypeRefFromMetadataType(type.MetadataType));
    }

    int AlignUp(int value, int alignment)
    {
        return alignment <= 1 ? value : checked((value + alignment - 1) / alignment * alignment);
    }

    ulong GetLLVMTypeSize(LLVMTypeRef type)
    {
        return (ulong)machine.CreateTargetDataLayout().ABISizeOfType(type);
    }

}
