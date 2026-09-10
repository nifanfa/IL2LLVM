sealed class Methods(Translator translator) : TranslationComponent(translator)
{
    internal new MethodReference ResolveCallTarget(MethodReference targetMethod)
    {
        var declaringType = targetMethod.DeclaringType.Resolve();
        if (declaringType is null || !declaringType.IsInterface)
            return targetMethod;

        foreach (var type in localTypes.Values.Where(type => !type.IsInterface))
        {
            if (!ImplementsInterface(type, targetMethod.DeclaringType))
                continue;

            var implementation = FindMethodImplementation(type, targetMethod);
            if (implementation is not null && implementation.Resolve()?.IsAbstract != true)
                return implementation;
        }

        return targetMethod;
    }

    internal new MethodReference ResolveVirtualTarget(MethodReference targetMethod, TypeReference receiverType)
    {
        if (receiverType is ByReferenceType byReference)
            receiverType = byReference.ElementType;
        var definition = receiverType.Resolve();
        if (definition is null)
            return ResolveCallTarget(targetMethod);
        var implementation = FindMethodImplementation(definition, targetMethod);
        return implementation is not null && implementation.Resolve()?.IsAbstract != true
            ? implementation
            : ResolveCallTarget(targetMethod);
    }

    internal new bool ImplementsInterface(TypeDefinition type, TypeReference interfaceType)
    {
        return TryCloseRuntimeType(type, interfaceType, out _);
    }

    internal new bool TryCloseRuntimeType(TypeDefinition type, TypeReference contractType, out TypeReference runtimeType)
    {
        runtimeType = type;
        if (contractType.Resolve()?.IsInterface != true)
            return SameType(type, contractType);

        foreach (var implementedInterface in GetImplementedInterfaces(type))
        {
            var bindings = new Dictionary<int, TypeReference>();
            var matches = TryBindTypePattern(implementedInterface, contractType, type, bindings);
            if (!matches)
                continue;
            if (!type.HasGenericParameters)
                return true;
            if (type.GenericParameters.Any(parameter => !bindings.ContainsKey(parameter.Position)))
                continue;
            var genericType = new GenericInstanceType(type);
            foreach (var parameter in type.GenericParameters)
                genericType.GenericArguments.Add(bindings[parameter.Position]);
            runtimeType = genericType;
            return true;
        }
        return false;
    }

    internal new IEnumerable<TypeReference> GetImplementedInterfaces(TypeReference type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Visit(type);

        IEnumerable<TypeReference> Visit(TypeReference currentType)
        {
            var definition = currentType.Resolve();
            if (definition is null)
                yield break;
            foreach (var implementation in definition.Interfaces)
            {
                var interfaceType = currentType is GenericInstanceType genericType
                    ? SubstituteGenericTypeArguments(implementation.InterfaceType, genericType)
                    : implementation.InterfaceType;
                if (seen.Add(GetRuntimeTypeKey(interfaceType)))
                    yield return interfaceType;
                foreach (var inherited in Visit(interfaceType))
                    yield return inherited;
            }
            if (definition.BaseType is null)
                yield break;
            var baseType = currentType is GenericInstanceType genericCurrent
                ? SubstituteGenericTypeArguments(definition.BaseType, genericCurrent)
                : definition.BaseType;
            foreach (var inherited in Visit(baseType))
                yield return inherited;
        }
    }

    internal new bool TryBindTypePattern(TypeReference pattern, TypeReference actual, TypeDefinition owner,
        Dictionary<int, TypeReference> bindings)
    {
        if (pattern is GenericParameter parameter && parameter.Type == GenericParameterType.Type &&
            parameter.Owner is TypeReference parameterOwner && SameTypeDefinition(parameterOwner, owner))
        {
            if (bindings.TryGetValue(parameter.Position, out var bound))
                return SameType(bound, actual);
            bindings.Add(parameter.Position, actual);
            return true;
        }
        if (pattern is GenericInstanceType patternGeneric && actual is GenericInstanceType actualGeneric)
        {
            if (!SameType(patternGeneric.ElementType, actualGeneric.ElementType) ||
                patternGeneric.GenericArguments.Count != actualGeneric.GenericArguments.Count)
                return false;
            for (int i = 0; i < patternGeneric.GenericArguments.Count; i++)
                if (!TryBindTypePattern(patternGeneric.GenericArguments[i], actualGeneric.GenericArguments[i], owner, bindings))
                    return false;
            return true;
        }
        if (pattern is ArrayType patternArray && actual is ArrayType actualArray)
            return patternArray.Rank == actualArray.Rank &&
                TryBindTypePattern(patternArray.ElementType, actualArray.ElementType, owner, bindings);
        if (pattern is ByReferenceType patternByReference && actual is ByReferenceType actualByReference)
            return TryBindTypePattern(patternByReference.ElementType, actualByReference.ElementType, owner, bindings);
        if (pattern is PointerType patternPointer && actual is PointerType actualPointer)
            return TryBindTypePattern(patternPointer.ElementType, actualPointer.ElementType, owner, bindings);
        return SameType(pattern, actual);
    }

    internal new bool SameType(TypeReference left, TypeReference right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is RequiredModifierType leftRequired)
            return SameType(leftRequired.ElementType, right);
        if (right is RequiredModifierType rightRequired)
            return SameType(left, rightRequired.ElementType);
        if (left is OptionalModifierType leftOptional)
            return SameType(leftOptional.ElementType, right);
        if (right is OptionalModifierType rightOptional)
            return SameType(left, rightOptional.ElementType);
        if (left is PinnedType leftPinned)
            return SameType(leftPinned.ElementType, right);
        if (right is PinnedType rightPinned)
            return SameType(left, rightPinned.ElementType);
        if (left is GenericParameter leftParameter && right is GenericParameter rightParameter)
            return leftParameter.Type == rightParameter.Type && leftParameter.Position == rightParameter.Position;
        if (left is GenericInstanceType leftGeneric && right is GenericInstanceType rightGeneric)
            return SameType(leftGeneric.ElementType, rightGeneric.ElementType) &&
                leftGeneric.GenericArguments.Count == rightGeneric.GenericArguments.Count &&
                leftGeneric.GenericArguments.Zip(rightGeneric.GenericArguments).All(pair => SameType(pair.First, pair.Second));
        if (left is GenericInstanceType leftOpen && IsOpenSelfInstantiation(leftOpen))
            return SameType(leftOpen.ElementType, right);
        if (right is GenericInstanceType rightOpen && IsOpenSelfInstantiation(rightOpen))
            return SameType(left, rightOpen.ElementType);
        if (left is GenericInstanceType || right is GenericInstanceType)
            return false;
        if (left is ArrayType leftArray && right is ArrayType rightArray)
            return leftArray.Rank == rightArray.Rank && SameType(leftArray.ElementType, rightArray.ElementType);
        if (left is ArrayType || right is ArrayType)
            return false;
        if (left is ByReferenceType leftByReference && right is ByReferenceType rightByReference)
            return SameType(leftByReference.ElementType, rightByReference.ElementType);
        if (left is ByReferenceType || right is ByReferenceType)
            return false;
        if (left is PointerType leftPointer && right is PointerType rightPointer)
            return SameType(leftPointer.ElementType, rightPointer.ElementType);
        if (left is PointerType || right is PointerType)
            return false;
        return SameTypeDefinition(left, right);
    }

    internal new bool IsOpenSelfInstantiation(GenericInstanceType type)
    {
        var definition = type.ElementType.Resolve();
        if (definition is null || definition.GenericParameters.Count != type.GenericArguments.Count)
            return false;
        for (int i = 0; i < type.GenericArguments.Count; i++)
            if (type.GenericArguments[i] is not GenericParameter parameter ||
                parameter.Type != GenericParameterType.Type || parameter.Position != i ||
                parameter.Owner is not TypeReference owner || !SameTypeDefinition(owner, definition))
                return false;
        return true;
    }

    internal new bool SameTypeDefinition(TypeReference left, TypeReference right)
    {
        if (localTypes.TryGetValue(left.FullName, out var leftLocal) &&
            localTypes.TryGetValue(right.FullName, out var rightLocal))
            return leftLocal.MetadataToken == rightLocal.MetadataToken && leftLocal.Module.Mvid == rightLocal.Module.Mvid;
        var leftDefinition = left.Resolve();
        var rightDefinition = right.Resolve();
        if (leftDefinition is not null && rightDefinition is not null)
            return leftDefinition.MetadataToken == rightDefinition.MetadataToken &&
                leftDefinition.Module.Mvid == rightDefinition.Module.Mvid;
        return left.Namespace == right.Namespace && left.Name == right.Name && left.Scope?.Name == right.Scope?.Name;
    }

    internal new bool SameMethodDefinition(MethodReference left, MethodReference right)
    {
        var leftDefinition = FindMethodDefinition(left);
        var rightDefinition = FindMethodDefinition(right);
        return leftDefinition is not null && rightDefinition is not null &&
            leftDefinition.MetadataToken == rightDefinition.MetadataToken &&
            leftDefinition.Module.Mvid == rightDefinition.Module.Mvid;
    }

    internal new MethodDefinition? FindMethodDefinition(MethodReference method)
    {
        var resolved = method.Resolve();
        if (resolved is not null)
            return resolved;
        var declaringType = method.DeclaringType.Resolve();
        if (declaringType is null)
            return null;
        return declaringType.Methods.FirstOrDefault(candidate =>
        {
            if (candidate.Name != method.Name || candidate.HasThis != method.HasThis ||
                candidate.Parameters.Count != method.Parameters.Count ||
                GetGenericMethodArity(candidate) != GetGenericMethodArity(method))
                return false;
            return SameMethodSignature(BindMethodToDeclaringType(candidate, method.DeclaringType, method), method);
        });
    }

    internal new bool SameMethodDeclarationSignature(MethodReference left, MethodReference right)
    {
        if (left.Name != right.Name || left.HasThis != right.HasThis ||
            left.Parameters.Count != right.Parameters.Count || GetGenericMethodArity(left) != GetGenericMethodArity(right) ||
            !SameType(left.ReturnType, right.ReturnType))
            return false;
        for (int i = 0; i < left.Parameters.Count; i++)
            if (!SameType(left.Parameters[i].ParameterType, right.Parameters[i].ParameterType))
                return false;
        return true;
    }

    internal new bool SameMethodSignature(MethodReference left, MethodReference right)
    {
        if (left.Name != right.Name || left.Parameters.Count != right.Parameters.Count ||
            GetGenericMethodArity(left) != GetGenericMethodArity(right))
            return false;
        for (int i = 0; i < left.Parameters.Count; i++)
            if (!SameType(SubstituteGenericParameter(left.Parameters[i].ParameterType, left),
                    SubstituteGenericParameter(right.Parameters[i].ParameterType, right)))
                return false;
        return SameType(SubstituteGenericParameter(left.ReturnType, left),
            SubstituteGenericParameter(right.ReturnType, right));
    }

    internal new bool SameMethodInstantiation(MethodReference left, MethodReference right)
    {
        if (!SameMethodDefinition(left, right) && !SameMethodSignature(left, right))
            return false;
        if (!SameType(left.DeclaringType, right.DeclaringType))
            return false;
        var leftArguments = left is GenericInstanceMethod leftGeneric
            ? leftGeneric.GenericArguments
            : [];
        var rightArguments = right is GenericInstanceMethod rightGeneric
            ? rightGeneric.GenericArguments
            : [];
        return leftArguments.Count == rightArguments.Count &&
            leftArguments.Zip(rightArguments).All(pair => SameType(pair.First, pair.Second));
    }

    internal new Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>? GetRegisteredMethod(MethodReference method)
    {
        return moduleMethods.Values.FirstOrDefault(candidate => SameMethodInstantiation(candidate.Item3, method));
    }

    internal new int GetGenericMethodArity(MethodReference method)
    {
        return method is GenericInstanceMethod genericMethod
            ? genericMethod.GenericArguments.Count
            : method.GenericParameters.Count;
    }

    internal new MethodReference BindMethodToDeclaringType(MethodDefinition method, TypeReference declaringType,
        MethodReference? requestedMethod = null)
    {
        var reference = new MethodReference(method.Name,
            declaringType is GenericInstanceType genericType
                ? SubstituteGenericTypeArguments(method.ReturnType, genericType)
                : method.ReturnType,
            declaringType)
        {
            HasThis = method.HasThis,
            ExplicitThis = method.ExplicitThis,
            CallingConvention = method.CallingConvention
        };
        foreach (var parameter in method.Parameters)
            reference.Parameters.Add(new ParameterDefinition(declaringType is GenericInstanceType genericDeclaringType
                ? SubstituteGenericTypeArguments(parameter.ParameterType, genericDeclaringType)
                : parameter.ParameterType));
        foreach (var parameter in method.GenericParameters)
            reference.GenericParameters.Add(new GenericParameter(parameter.Name, reference));
        if (requestedMethod is GenericInstanceMethod requestedGeneric && method.HasGenericParameters)
        {
            var genericMethod = new GenericInstanceMethod(reference);
            foreach (var argument in requestedGeneric.GenericArguments)
                genericMethod.GenericArguments.Add(argument);
            return genericMethod;
        }
        return reference;
    }

    internal new MethodReference? FindMethodImplementation(TypeReference type, MethodReference targetMethod)
    {
        var contractIsInterface = targetMethod.DeclaringType.Resolve()?.IsInterface == true;
        var currentType = type;
        if (type is TypeDefinition typeDefinition && typeDefinition.HasGenericParameters &&
            TryCloseRuntimeType(typeDefinition, targetMethod.DeclaringType, out var closedType))
            currentType = closedType;

        while (currentType.Resolve() is { } current)
        {
            foreach (var method in current.Methods.Where(method => !method.IsStatic))
            {
                if (!method.Overrides.Any(@override => SameMethodDefinition(@override, targetMethod)))
                    continue;
                return BindMethodToDeclaringType(method, currentType, targetMethod);
            }
            foreach (var method in current.Methods.Where(method => !method.IsStatic && method.Name == targetMethod.Name))
            {
                var implementation = BindMethodToDeclaringType(method, currentType, targetMethod);
                if (SameMethodSignature(implementation, targetMethod) &&
                    (contractIsInterface || SameMethodDefinition(method, targetMethod) || method.IsVirtual && !method.IsNewSlot))
                    return implementation;
            }

            if (current.BaseType is null)
                break;
            currentType = currentType is GenericInstanceType genericCurrent
                ? SubstituteGenericTypeArguments(current.BaseType, genericCurrent)
                : current.BaseType;
        }
        return null;
    }

    internal new bool UsesValueReturnBuffer(MethodReference method)
    {
        var returnType = SubstituteGenericParameter(method.ReturnType, method);
        return !IsExternalMethod(method) && IsValueType(returnType) && !IsByReferenceValue(returnType);
    }

    internal new LLVMTypeRef CreateLLVMFunction(LLVMModuleRef module, MethodReference method)
    {
        List<LLVMTypeRef> paramTypes = new List<LLVMTypeRef>();
        if (UsesValueReturnBuffer(method))
            paramTypes.Add(LLVMTypeRef.CreatePointer(int8Type, 0));
        if (method.HasThis)
        {
            // "this" will be a parameter
            paramTypes.Add(LLVMTypeRef.CreatePointer(int8Type, 0));
        }
        foreach (var p in method.Parameters)
        {
            paramTypes.Add(GetLLVMTypeRef(SubstituteGenericParameter(p.ParameterType, method)));
        }
        LLVMTypeRef returnType = UsesValueReturnBuffer(method)
            ? voidType
            : GetLLVMTypeRef(SubstituteGenericParameter(method.ReturnType, method));
        var func = LLVMTypeRef.CreateFunction(returnType, paramTypes.ToArray());
        return func;
    }

    internal new string GetFriendlyMethodName(MethodReference method, TypeReference? methodDeclareType = null)
    {
        TypeReference declareType = methodDeclareType ?? method.DeclaringType;
        List<string> names = [GetFriendlyTypeName(declareType), SanitizeSymbolPart(method.Name)];
        if (method is GenericInstanceMethod genericMethod)
            names.AddRange(genericMethod.GenericArguments.Select(argument => GetFriendlyTypeName(argument)));
        else if (method.GenericParameters.Count != 0)
            names.Add($"G{method.GenericParameters.Count}");
        names.AddRange(method.Parameters.Select(parameter =>
            GetFriendlyParameterTypeName(SubstituteGenericParameter(parameter.ParameterType, method))));
        return string.Join("_", names);
    }

    internal new string GetFriendlyTypeName(TypeReference type, bool includeGenericMarker = true)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetFriendlyTypeName(requiredModifier.ElementType, includeGenericMarker);
        if (type is OptionalModifierType optionalModifier)
            return GetFriendlyTypeName(optionalModifier.ElementType, includeGenericMarker);
        if (type is PinnedType pinned)
            return GetFriendlyTypeName(pinned.ElementType, includeGenericMarker);
        if (type is GenericInstanceType generic)
            return GetFriendlyTypeName(generic.ElementType, false) + "_" + string.Join("_", generic.GenericArguments.Select(argument => GetFriendlyTypeName(argument)));
        if (type is ArrayType array)
            return $"{GetFriendlyTypeName(array.ElementType)}_Array{array.Rank}";
        if (type is ByReferenceType byReference)
            return GetFriendlyTypeName(byReference.ElementType) + "_ByReference";
        if (type is PointerType pointer)
            return GetFriendlyTypeName(pointer.ElementType) + "_Pointer";
        if (type is GenericParameter parameter)
            return $"{parameter.Type}{parameter.Position}";
        var name = RemoveGenericArity(type.FullName);
        if (includeGenericMarker && type.Resolve() is { GenericParameters.Count: > 0 } definition)
            name += "_" + string.Join("_", definition.GenericParameters.Select(parameter => SanitizeSymbolPart(parameter.Name)));
        return SanitizeSymbolPart(name);
    }

    internal new static string RemoveGenericArity(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '`' && index + 1 < value.Length && char.IsDigit(value[index + 1]))
            {
                while (index + 1 < value.Length && char.IsDigit(value[index + 1]))
                    index++;
                continue;
            }
            result.Append(value[index]);
        }
        return result.ToString();
    }

    internal new string GetFriendlyParameterTypeName(TypeReference type)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetFriendlyParameterTypeName(requiredModifier.ElementType);
        if (type is OptionalModifierType optionalModifier)
            return GetFriendlyParameterTypeName(optionalModifier.ElementType);
        if (type is PinnedType pinned)
            return GetFriendlyParameterTypeName(pinned.ElementType);
        return type.MetadataType is MetadataType.Class or MetadataType.ValueType or MetadataType.GenericInstance or
            MetadataType.Array or MetadataType.ByReference or MetadataType.Pointer or MetadataType.Var or MetadataType.MVar
            ? GetFriendlyTypeName(type)
            : type.MetadataType.ToString();
    }

    internal new string SanitizeSymbolPart(string value)
    {
        return new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_'
            ? character
            : '_').ToArray());
    }

    internal new string GetStableSymbolSuffix(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return $"{SanitizeSymbolPart(RemoveGenericArity(value))}_{hash:X8}";
    }

    internal new Tuple<LLVMValueRef, LLVMTypeRef> GetStaticField(FieldReference field, MethodReference? context = null)
    {
        var declaringType = context is null ? field.DeclaringType : ResolveGenericType(field.DeclaringType, context);
        var fieldName = $"{GetFriendlyTypeName(declaringType)}_{SanitizeSymbolPart(field.Name)}";
        if (staticFields.TryGetValue(fieldName, out var existing))
            return existing;
        var fieldReferenceType = SubstituteFieldType(field, context);
        var fieldType = GetLLVMTypeRef(fieldReferenceType);
        if (IsValueType(fieldReferenceType))
        {
            var storageType = LLVMTypeRef.CreateArray(int8Type, (uint)Math.Max(1, GetTypeSize(fieldReferenceType)));
            var storage = AddInternalGlobal(storageType, fieldName);
            storage.Initializer = LLVMValueRef.CreateConstNull(storageType);
            var storageResult = new Tuple<LLVMValueRef, LLVMTypeRef>(storage, fieldType);
            staticFields.Add(fieldName, storageResult);
            staticFieldTypes.Add(fieldName, fieldReferenceType);
            return storageResult;
        }
        var fieldValue = AddInternalGlobal(fieldType, fieldName);
        fieldValue.Initializer = LLVMValueRef.CreateConstNull(fieldType);
        var result = new Tuple<LLVMValueRef, LLVMTypeRef>(fieldValue, fieldType);
        staticFields.Add(fieldName, result);
        staticFieldTypes.Add(fieldName, fieldReferenceType);
        return result;
    }

    internal new LLVMValueRef GetRuntimeFieldHandle(LLVMBuilderRef builder, LLVMBuilderRef allocationBuilder, FieldReference field)
    {
        var definition = GetLocalField(field);
        var key = definition.FullName;
        var initialValue = definition.InitialValue ?? [];
        var dataType = LLVMTypeRef.CreateArray(int8Type, (uint)Math.Max(1, initialValue.Length));
        if (!runtimeFieldData.TryGetValue(key, out var data))
        {
            data = AddInternalGlobal(dataType, $"__field_data_{GetStableSymbolSuffix(key)}");
            data.Initializer = LLVMValueRef.CreateConstArray(int8Type,
                initialValue.Length == 0
                    ? [LLVMValueRef.CreateConstNull(int8Type)]
                    : initialValue.Select(value => LLVMValueRef.CreateConstInt(int8Type, value, false)).ToArray());
            runtimeFieldData.Add(key, data);
        }

        var handleType = coreLib.RuntimeFieldHandle;
        var storage = CreateLocalStorage(allocationBuilder, handleType);
        var handle = builder.BuildLoad2(storage.Item2, storage.Item1);
        var dataPointer = builder.BuildGEP2(dataType, data,
            [LLVMValueRef.CreateConstInt(sizeType, 0, false), LLVMValueRef.CreateConstInt(sizeType, 0, false)]);
        StoreField(builder, handle, coreLib.RuntimeFieldDataField, dataPointer);
        StoreField(builder, handle, coreLib.RuntimeFieldLengthField,
            LLVMValueRef.CreateConstInt(int32Type, (ulong)(definition.InitialValue?.Length ?? 0), false));
        return handle;
    }

    internal new void RegisterMethodFunction(LLVMModuleRef module, MethodReference method, Collection<Instruction>? instructions,
        string? symbolName = null)
    {
        if (method.DeclaringType is ArrayType array && array.Rank > 1 &&
            method.Name is ".ctor" or "Get" or "Set" or "Address")
            return;
        if (method.DeclaringType.Resolve()?.IsInterface == true)
            return;
        if (IsDelegateType(method.DeclaringType) && (method.Name is ".ctor" or "Invoke"))
            return;

        TypeReference declareType = method.DeclaringType;
        string friendlyName = GetFriendlyMethodName(method, declareType);
        if (moduleMethods.TryGetValue(friendlyName, out var existing))
        {
            if (SameMethodInstantiation(existing.Item3, method))
            {
                if (existing.Item4 is null && instructions is not null)
                    moduleMethods[friendlyName] = new(existing.Item1, existing.Item2, existing.Item3, instructions);
                return;
            }
            throw new InvalidOperationException($"LLVM method symbol collision: {existing.Item3.FullName} and {method.FullName}.");
        }

        var funcType = CreateLLVMFunction(module, method);
        var importedName = method.Resolve()?.PInvokeInfo?.EntryPoint;
        var nativeSymbolName = !string.IsNullOrEmpty(importedName) && importedName != method.Name
            ? importedName
            : null;
        var exportedName = entryPoint is not null && SameMethodDefinition(method, entryPoint)
            ? "managed_Main"
            : symbolName ?? nativeSymbolName ?? friendlyName;
        var funcValue = module.AddFunction(exportedName, funcType);
        if (method.Resolve()?.HasBody == true && (entryPoint is null || !SameMethodDefinition(method, entryPoint)))
            funcValue.Linkage = LLVMLinkage.LLVMInternalLinkage;
        moduleMethods.Add(friendlyName, new(funcValue, funcType, method, instructions));
    }

    internal new LLVMValueRef AddInternalGlobal(LLVMTypeRef type, string name)
    {
        var value = module.AddGlobal(type, name);
        value.Linkage = LLVMLinkage.LLVMInternalLinkage;
        return value;
    }

    internal new unsafe LLVMTypeRef GetFunctionType(LLVMValueRef function)
    {
        return new LLVMTypeRef((IntPtr)LLVM.GlobalGetValueType((LLVMOpaqueValue*)function.Handle));
    }
}
