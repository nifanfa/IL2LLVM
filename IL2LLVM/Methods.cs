enum ArrayRuntimeMethodKind
{
    None,
    Constructor,
    Get,
    Set,
    Address
}

sealed class Methods(Translator translator) : TranslationComponent(translator)
{
    internal new ArrayRuntimeMethodKind GetArrayRuntimeMethodKind(MethodReference method)
    {
        if (method.DeclaringType is not ArrayType { Rank: > 1 } array || !method.HasThis)
            return ArrayRuntimeMethodKind.None;

        var rank = array.Rank;
        bool HasIndexParameters(int count) => method.Parameters.Count == count &&
            method.Parameters.Take(rank).All(parameter => parameter.ParameterType.MetadataType == MetadataType.Int32);

        if (HasIndexParameters(rank))
        {
            if (IsVoidType(method.ReturnType))
                return ArrayRuntimeMethodKind.Constructor;
            if (SameType(method.ReturnType, array.ElementType))
                return ArrayRuntimeMethodKind.Get;
            if (method.ReturnType is ByReferenceType byReference &&
                SameType(byReference.ElementType, array.ElementType))
                return ArrayRuntimeMethodKind.Address;
        }
        if (HasIndexParameters(rank + 1) && IsVoidType(method.ReturnType) &&
            SameType(method.Parameters[rank].ParameterType, array.ElementType))
            return ArrayRuntimeMethodKind.Set;
        return ArrayRuntimeMethodKind.None;
    }

    internal new bool IsRuntimeDelegateConstructor(MethodReference method)
    {
        if (!IsDelegateType(method.DeclaringType))
            return false;
        var definition = FindMethodDefinition(method);
        return definition is { IsConstructor: true, IsStatic: false, HasBody: false } &&
            (definition.ImplAttributes & MethodImplAttributes.Runtime) != 0;
    }

    internal new bool IsRuntimeDelegateInvoke(MethodReference method)
    {
        if (!IsDelegateType(method.DeclaringType))
            return false;
        var definition = FindMethodDefinition(method);
        return definition is { IsConstructor: false, IsStatic: false, IsVirtual: true, HasBody: false } &&
            (definition.ImplAttributes & MethodImplAttributes.Runtime) != 0;
    }

    internal new MethodDefinition GetRuntimeDelegateInvokeMethod(TypeReference type)
    {
        var definition = type.Resolve() ??
            throw new NotSupportedException($"Delegate type is not defined: {type.FullName}");
        var matches = definition.Methods.Where(IsRuntimeDelegateInvoke).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new NotSupportedException(
                $"Expected one invocation method on delegate type {type.FullName}, found {matches.Count}.");
    }

    internal new bool IsTypeInitializer(MethodReference method)
    {
        return FindMethodDefinition(method) is
        {
            IsConstructor: true,
            IsStatic: true,
            HasParameters: false
        };
    }

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
        var definition = ResolveInputType(receiverType);
        if (definition is null)
            return ResolveCallTarget(targetMethod);
        var implementation = FindMethodImplementation(receiverType, targetMethod);
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
        var key = $"{method.DeclaringType.Scope?.Name}|{method.FullName}";
        if (methodDefinitionCache.TryGetValue(key, out var cached))
            return cached;
        var resolved = method.Resolve();
        if (resolved is not null)
        {
            methodDefinitionCache[key] = resolved;
            return resolved;
        }
        var declaringType = method.DeclaringType.Resolve();
        if (declaringType is null)
        {
            methodDefinitionCache[key] = null;
            return null;
        }
        var result = declaringType.Methods.FirstOrDefault(candidate =>
        {
            if (candidate.Name != method.Name || candidate.HasThis != method.HasThis ||
                candidate.Parameters.Count != method.Parameters.Count ||
                GetGenericMethodArity(candidate) != GetGenericMethodArity(method))
                return false;
            return SameMethodSignature(BindMethodToDeclaringType(candidate, method.DeclaringType, method), method);
        });
        methodDefinitionCache[key] = result;
        return result;
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
        if (ReferenceEquals(left, right) ||
            left.DeclaringType.Scope?.Name == right.DeclaringType.Scope?.Name &&
            left.FullName == right.FullName)
            return true;
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
        var friendlyName = GetFriendlyMethodName(method);
        if (moduleMethods.TryGetValue(friendlyName, out var candidate) &&
            SameMethodInstantiation(candidate.Item3, method))
            return candidate;

        var returnName = GetFriendlyParameterTypeName(SubstituteGenericParameter(method.ReturnType, method));
        var collisionName = $"{friendlyName}_Returns_{returnName}";
        if (moduleMethods.TryGetValue(collisionName, out candidate) &&
            SameMethodInstantiation(candidate.Item3, method))
            return candidate;

        return moduleMethods.Values.FirstOrDefault(candidate => SameMethodInstantiation(candidate.Item3, method));
    }

    bool IsGCReferenceBearingType(TypeReference type)
    {
        while (type is RequiredModifierType or OptionalModifierType or PinnedType)
            type = type switch
            {
                RequiredModifierType required => required.ElementType,
                OptionalModifierType optional => optional.ElementType,
                PinnedType pinned => pinned.ElementType,
                _ => type
            };
        if (type is PointerType)
            return false;
        if (type is ByReferenceType byReference)
            return IsGCReferenceBearingType(byReference.ElementType);
        return IsByReferenceValue(type) || IsManagedReferenceType(type) ||
            IsValueType(type) && GetGCReferenceOffsets(type).Any();
    }

    internal new bool IsGCFrameFree(MethodReference method)
    {
        var definition = method.Resolve();
        if (definition?.Body is not { } body || body.ExceptionHandlers.Count != 0)
            return false;

        bool HasManagedType(TypeReference type) => IsGCReferenceBearingType(type);
        if (method.HasThis && HasManagedType(method.DeclaringType) ||
            method.Parameters.Any(parameter => HasManagedType(parameter.ParameterType)) ||
            HasManagedType(method.ReturnType) ||
            body.Variables.Any(variable => HasManagedType(variable.VariableType)))
            return false;

        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode.Code is Code.Ldstr or Code.Newobj or Code.Newarr or Code.Box or
                Code.Ldvirtftn)
                return false;
            if (instruction.Operand is MethodReference target &&
                (target.HasThis && HasManagedType(target.DeclaringType) ||
                 target.Parameters.Any(parameter => HasManagedType(parameter.ParameterType)) ||
                 HasManagedType(target.ReturnType)))
                return false;
            if (instruction.Operand is FieldReference field && HasManagedType(field.FieldType))
                return false;
            if (instruction.Operand is TypeReference referencedType && HasManagedType(referencedType))
                return false;
            if (instruction.Operand is VariableDefinition variable && HasManagedType(variable.VariableType))
                return false;
        }
        return true;
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
        targetMethod = CloseMethodContract(type, targetMethod);
        var contractIsInterface = targetMethod.DeclaringType.Resolve()?.IsInterface == true;
        var targetIsStatic = !targetMethod.HasThis;
        var currentType = type;
        if (type is TypeDefinition typeDefinition && typeDefinition.HasGenericParameters &&
            TryCloseRuntimeType(typeDefinition, targetMethod.DeclaringType, out var closedType))
            currentType = closedType;

        while (ResolveInputType(currentType) is { } current)
        {
            foreach (var method in current.Methods.Where(method => method.IsStatic == targetIsStatic))
            {
                if (!method.Overrides.Any(@override =>
                    SameMethodDefinition(@override, targetMethod) ||
                    (GetRuntimeTypeKey(@override.DeclaringType) == GetRuntimeTypeKey(targetMethod.DeclaringType) &&
                     SameMethodSignature(@override, targetMethod))))
                    continue;
                return BindMethodToDeclaringType(method, currentType, targetMethod);
            }
            foreach (var method in current.Methods.Where(method => method.IsStatic == targetIsStatic && method.Name == targetMethod.Name))
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

    private TypeDefinition? ResolveInputType(TypeReference type)
    {
        var name = type is GenericInstanceType generic ? generic.ElementType.FullName : type.FullName;
        return localTypes.TryGetValue(name, out var definition) ? definition : type.Resolve();
    }

    private MethodReference CloseMethodContract(TypeReference runtimeType, MethodReference targetMethod)
    {
        if (!ContainsGenericParameter(targetMethod.DeclaringType))
            return targetMethod;

        for (TypeReference? current = runtimeType; current is not null; current = GetClosedBaseType(current))
        {
            if (ContainsGenericParameter(current) ||
                !SameTypeDefinition(current, targetMethod.DeclaringType))
                continue;
            var definition = FindMethodDefinition(targetMethod);
            return definition is null
                ? targetMethod
                : BindMethodToDeclaringType(definition, current, targetMethod);
        }

        foreach (var contract in GetImplementedInterfaces(runtimeType))
        {
            if (ContainsGenericParameter(contract) ||
                !SameTypeDefinition(contract, targetMethod.DeclaringType))
                continue;
            var definition = FindMethodDefinition(targetMethod);
            return definition is null
                ? targetMethod
                : BindMethodToDeclaringType(definition, contract, targetMethod);
        }

        return targetMethod;
    }

    internal new LLVMTypeRef CreateLLVMFunction(LLVMModuleRef module, MethodReference method)
    {
        if (GetArrayRuntimeMethodKind(method) == ArrayRuntimeMethodKind.Constructor)
        {
            var arrayConstructorParameters = method.Parameters.Select(parameter =>
                GetCallType(SubstituteGenericParameter(parameter.ParameterType, method))).ToArray();
            return LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(int8Type, 0), arrayConstructorParameters);
        }

        List<LLVMTypeRef> paramTypes = new List<LLVMTypeRef>();
        if (method.HasThis)
        {
            // "this" will be a parameter
            paramTypes.Add(LLVMTypeRef.CreatePointer(int8Type, 0));
        }
        var definition = method.Resolve();
        var parameters = method.CallingConvention == MethodCallingConvention.VarArg
            ? definition?.Parameters ?? method.Parameters
            : method.Parameters;
        foreach (var p in parameters)
        {
            var parameterType = SubstituteGenericParameter(p.ParameterType, method);
            paramTypes.Add(GetCallType(parameterType));
        }
        if (method.CallingConvention == MethodCallingConvention.VarArg && UsesArgumentList(method))
        {
            paramTypes.Add(LLVMTypeRef.CreatePointer(int8Type, 0));
            paramTypes.Add(int32Type);
        }
        LLVMTypeRef returnType = GetCallType(SubstituteGenericParameter(method.ReturnType, method));
        var func = LLVMTypeRef.CreateFunction(returnType, paramTypes.ToArray(),
            method.CallingConvention == MethodCallingConvention.VarArg);
        return func;
    }

    internal new bool TryGetReturnedParameterAddress(MethodReference method, out int parameterIndex)
    {
        parameterIndex = -1;
        var definition = method.Resolve();
        if (definition?.HasBody != true || definition.Body.Instructions.Count == 0 ||
            (method.ReturnType is not PointerType && method.ReturnType is not ByReferenceType))
            return false;

        var instructions = definition.Body.Instructions;
        var entryStates = new Dictionary<int, (List<int?> Stack, Dictionary<int, int?> Locals)>();
        var work = new Queue<int>();
        entryStates[0] = ([], new Dictionary<int, int?>());
        work.Enqueue(0);
        int? returned = null;

        void Merge(int index, List<int?> stack, Dictionary<int, int?> locals)
        {
            if (!entryStates.TryGetValue(index, out var existing))
            {
                entryStates[index] = (new(stack), new(locals));
                work.Enqueue(index);
                return;
            }
            if (existing.Stack.Count != stack.Count)
                return;
            bool changed = false;
            for (int i = 0; i < stack.Count; i++)
            {
                var value = existing.Stack[i] == stack[i] ? existing.Stack[i] : null;
                changed |= value != existing.Stack[i];
                existing.Stack[i] = value;
            }
            foreach (var key in existing.Locals.Keys.Union(locals.Keys).ToArray())
            {
                existing.Locals.TryGetValue(key, out var oldValue);
                locals.TryGetValue(key, out var newValue);
                var value = oldValue == newValue ? oldValue : null;
                changed |= !existing.Locals.TryGetValue(key, out var current) || current != value;
                existing.Locals[key] = value;
            }
            if (changed)
            {
                entryStates[index] = existing;
                work.Enqueue(index);
            }
        }

        int ArgumentIndex(Instruction instruction)
        {
            return instruction.Operand is ParameterDefinition parameter
                ? parameter.Index
                : instruction.OpCode.Code switch
                {
                    Code.Ldarg_0 or Code.Ldarga => 0,
                    Code.Ldarg_1 => 1,
                    Code.Ldarg_2 => 2,
                    Code.Ldarg_3 => 3,
                    _ => Convert.ToInt32(instruction.Operand)
                };
        }

        while (work.Count != 0)
        {
            int index = work.Dequeue();
            var state = entryStates[index];
            var stack = new List<int?>(state.Stack);
            var locals = new Dictionary<int, int?>(state.Locals);
            var instruction = instructions[index];
            var code = instruction.OpCode.Code;
            bool stop = false;

            switch (code)
            {
                case Code.Ldarga:
                case Code.Ldarga_S:
                    stack.Add(ArgumentIndex(instruction));
                    break;
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg:
                case Code.Ldarg_S:
                    stack.Add(null);
                    break;
                case Code.Conv_I:
                case Code.Conv_I1:
                case Code.Conv_I2:
                case Code.Conv_I4:
                case Code.Conv_I8:
                case Code.Conv_U:
                case Code.Conv_U1:
                case Code.Conv_U2:
                case Code.Conv_U4:
                case Code.Conv_U8:
                    break;
                case Code.Dup:
                    if (stack.Count != 0)
                        stack.Add(stack[^1]);
                    break;
                case Code.Pop:
                    if (stack.Count != 0)
                        stack.RemoveAt(stack.Count - 1);
                    break;
                case Code.Stloc_0:
                case Code.Stloc_1:
                case Code.Stloc_2:
                case Code.Stloc_3:
                case Code.Stloc:
                case Code.Stloc_S:
                    if (stack.Count != 0)
                    {
                        int localIndex = code switch
                        {
                            Code.Stloc_0 => 0,
                            Code.Stloc_1 => 1,
                            Code.Stloc_2 => 2,
                            Code.Stloc_3 => 3,
                            _ => ((VariableDefinition)instruction.Operand).Index
                        };
                        locals[localIndex] = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                    }
                    break;
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloc:
                case Code.Ldloc_S:
                    int loadIndex = code switch
                    {
                        Code.Ldloc_0 => 0,
                        Code.Ldloc_1 => 1,
                        Code.Ldloc_2 => 2,
                        Code.Ldloc_3 => 3,
                        _ => ((VariableDefinition)instruction.Operand).Index
                    };
                    stack.Add(locals.TryGetValue(loadIndex, out var localValue) ? localValue : null);
                    break;
                case Code.Ret:
                    if (stack.Count != 0 && stack[^1] is int addressParameter)
                        returned = returned is null || returned == addressParameter ? addressParameter : -1;
                    stop = true;
                    break;
                case Code.Br:
                case Code.Br_S:
                    Merge(instructions.IndexOf((Instruction)instruction.Operand), stack, locals);
                    stop = true;
                    break;
                default:
                    if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                    {
                        if (stack.Count != 0)
                            stack.RemoveAt(stack.Count - 1);
                        if (instruction.Operand is Instruction target)
                            Merge(instructions.IndexOf(target), stack, locals);
                    }
                    else if (instruction.OpCode.FlowControl == FlowControl.Call)
                    {
                        var called = instruction.Operand as MethodReference;
                        int popCount = (called?.Parameters.Count ?? 0) + (called?.HasThis == true ? 1 : 0);
                        while (popCount-- > 0 && stack.Count != 0)
                            stack.RemoveAt(stack.Count - 1);
                        if (called is not null && !IsVoidType(called.ReturnType))
                            stack.Add(null);
                    }
                    else
                    {
                        int popCount = instruction.OpCode.StackBehaviourPop switch
                        {
                            StackBehaviour.Pop0 => 0,
                            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or
                            StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
                            StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
                            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
                            StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 => 3,
                            StackBehaviour.PopAll => stack.Count,
                            _ => 0
                        };
                        while (popCount-- > 0 && stack.Count != 0)
                            stack.RemoveAt(stack.Count - 1);
                        if (instruction.OpCode.StackBehaviourPush != StackBehaviour.Push0)
                            stack.Add(null);
                    }
                    break;
            }

            if (!stop && index + 1 < instructions.Count)
                Merge(index + 1, stack, locals);
        }

        if (returned is not int result || result < 0)
            return false;
        parameterIndex = result;
        return true;
    }

    internal new string GetFriendlyMethodName(MethodReference method, TypeReference? methodDeclareType = null)
    {
        TypeReference declareType = methodDeclareType ?? method.DeclaringType;
        List<string> names = [GetFriendlyTypeName(declareType), SanitizeSymbolPart(method.Name)];
        if (method is GenericInstanceMethod genericMethod)
            names.AddRange(genericMethod.GenericArguments.Select(argument => GetFriendlyTypeName(argument)));
        else if (method.GenericParameters.Count != 0)
            names.AddRange(method.GenericParameters.Select(parameter =>
                string.IsNullOrEmpty(parameter.Name)
                    ? $"M{parameter.Position}"
                    : SanitizeSymbolPart(parameter.Name)));
        names.AddRange(method.Parameters.Select(parameter =>
            GetFriendlyParameterTypeName(SubstituteGenericParameter(parameter.ParameterType, method))));
        return string.Join("_", names);
    }

    internal new string GetFriendlyTypeName(TypeReference type, bool includeGenericMarker = true)
        => GetFriendlyTypeName(type, includeGenericMarker, false);

    string GetFriendlyTypeName(TypeReference type, bool includeGenericMarker, bool nested)
    {
        if (type is RequiredModifierType requiredModifier)
            return GetFriendlyTypeName(requiredModifier.ElementType, includeGenericMarker, nested);
        if (type is OptionalModifierType optionalModifier)
            return GetFriendlyTypeName(optionalModifier.ElementType, includeGenericMarker, nested);
        if (type is PinnedType pinned)
            return GetFriendlyTypeName(pinned.ElementType, includeGenericMarker, nested);
        if (type is GenericInstanceType generic)
            return GetFriendlyTypeName(generic.ElementType, false, nested) + "_" +
                string.Join("_", generic.GenericArguments.Select(argument =>
                    GetFriendlyTypeName(argument, true, true)));
        if (type is ArrayType array)
            return nested
                ? $"{GetFriendlyTypeName(array.ElementType, true, true)}__Array{array.Rank}"
                : $"{GetFriendlyTypeName(array.ElementType)}_Array{array.Rank}";
        if (type is ByReferenceType byReference)
            return nested
                ? $"{GetFriendlyTypeName(byReference.ElementType, true, true)}__ByReference"
                : $"{GetFriendlyTypeName(byReference.ElementType)}_ByReference";
        if (type is PointerType pointer)
            return nested
                ? $"{GetFriendlyTypeName(pointer.ElementType, true, true)}__Pointer"
                : $"{GetFriendlyTypeName(pointer.ElementType)}_Pointer";
        if (type is GenericParameter parameter)
            return string.IsNullOrEmpty(parameter.Name)
                ? $"{parameter.Type}{parameter.Position}"
                : SanitizeSymbolPart(parameter.Name);
        var name = RemoveGenericArity(type.FullName);
        if (includeGenericMarker && !type.FullName.Contains('/') &&
            localTypes.TryGetValue(type.FullName, out var definition) &&
            definition.GenericParameters.Count > 0)
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

    internal new string RegisterGeneratedSymbol(string name, string identity)
    {
        if (generatedSymbolIdentities.TryGetValue(name, out var existingIdentity))
        {
            if (existingIdentity != identity)
                throw new InvalidOperationException(
                    $"LLVM generated symbol collision: '{name}' represents both '{existingIdentity}' and '{identity}'.");
            return name;
        }
        generatedSymbolIdentities.Add(name, identity);
        return name;
    }

    internal new Tuple<LLVMValueRef, LLVMTypeRef> GetStaticField(FieldReference field, MethodReference? context = null)
    {
        var declaringType = context is null ? field.DeclaringType : ResolveGenericType(field.DeclaringType, context);
        var fieldName = $"{GetFriendlyTypeName(declaringType)}_{SanitizeSymbolPart(field.Name)}";
        if (staticFields.TryGetValue(fieldName, out var existing))
            return existing;
        var fieldReferenceType = SubstituteFieldType(field, context);
        var fieldType = GetLLVMTypeRef(fieldReferenceType);
        var definition = GetLocalField(field);
        var initialValue = definition.InitialValue;
        if (initialValue is { Length: > 0 })
        {
            var storageSize = Math.Max(initialValue.Length, Math.Max(1, GetTypeSize(fieldReferenceType)));
            var storageType = LLVMTypeRef.CreateArray(int8Type, (uint)storageSize);
            var storage = AddInternalGlobal(storageType, fieldName);
            storage.Initializer = LLVMValueRef.CreateConstArray(int8Type, Enumerable.Range(0, storageSize)
                .Select(index => LLVMValueRef.CreateConstInt(int8Type,
                    index < initialValue.Length ? initialValue[index] : 0ul, false))
                .ToArray());
            var storageResult = new Tuple<LLVMValueRef, LLVMTypeRef>(storage, fieldType);
            staticFields.Add(fieldName, storageResult);
            staticFieldTypes.Add(fieldName, fieldReferenceType);
            return storageResult;
        }
        if (IsValueType(fieldReferenceType) && !IsByReferenceValue(fieldReferenceType))
        {
            var storageSize = Math.Max(1, GetTypeSize(fieldReferenceType));
            var storageType = LLVMTypeRef.CreateArray(int8Type, (uint)storageSize);
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
            var name = $"__field_data_{GetFriendlyTypeName(definition.DeclaringType)}_{SanitizeSymbolPart(definition.Name)}";
            data = AddInternalGlobal(dataType, RegisterGeneratedSymbol(name, key));
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
        if (method.CallingConvention == MethodCallingConvention.VarArg &&
            method.Resolve() is { } varargDefinition &&
            method.Parameters.Count != varargDefinition.Parameters.Count)
            method = BindMethodToDeclaringType(varargDefinition, method.DeclaringType, method);
        var isRuntimeGenerated = GetArrayRuntimeMethodKind(method) != ArrayRuntimeMethodKind.None ||
            IsRuntimeDelegateConstructor(method) || IsRuntimeDelegateInvoke(method);
        if (method.DeclaringType.Resolve()?.IsInterface == true &&
            method.Resolve()?.HasBody != true && method.Resolve()?.PInvokeInfo is null)
            return;

        var runtimeExportName = GetRuntimeExportName(method);
        var directExport = runtimeExportName is not null;
        var isEntryPoint = entryPoint is not null && SameMethodDefinition(method, entryPoint);
        var hasDiscardableBody = (instructions?.Count > 0 || isRuntimeGenerated) && !directExport && !isEntryPoint;

        void SetBodyLinkage(LLVMValueRef function)
        {
            if (!hasDiscardableBody)
                return;
            function.Linkage = LLVMLinkage.LLVMInternalLinkage;
        }

        TypeReference declareType = method.DeclaringType;
        string friendlyName = GetFriendlyMethodName(method, declareType);
        if (moduleMethods.TryGetValue(friendlyName, out var existing))
        {
            if (SameMethodInstantiation(existing.Item3, method))
            {
                SetBodyLinkage(existing.Item1);
                if (existing.Item4 is null && instructions is not null)
                {
                    moduleMethods[friendlyName] = new(existing.Item1, existing.Item2, existing.Item3, instructions);
                    if (instructions.Count != 0 && queuedMethodTranslations.Add(friendlyName))
                        pendingMethodTranslations.Enqueue(friendlyName);
                }
                return;
            }
            var returnType = GetFriendlyParameterTypeName(SubstituteGenericParameter(method.ReturnType, method));
            friendlyName = $"{friendlyName}_Returns_{returnType}";
            if (moduleMethods.TryGetValue(friendlyName, out existing))
            {
                if (!SameMethodInstantiation(existing.Item3, method))
                    throw new InvalidOperationException($"LLVM method symbol collision: {existing.Item3.FullName} and {method.FullName}.");
                SetBodyLinkage(existing.Item1);
                if (existing.Item4 is null && instructions is not null)
                {
                    moduleMethods[friendlyName] = new(existing.Item1, existing.Item2, existing.Item3, instructions);
                    if (instructions.Count != 0 && queuedMethodTranslations.Add(friendlyName))
                        pendingMethodTranslations.Enqueue(friendlyName);
                }
                return;
            }
        }

        var pinvoke = method.Resolve()?.PInvokeInfo;
        var nativeSymbolName = pinvoke is null ? null : GetPInvokeNativeSymbolName(method, friendlyName, pinvoke);
        var funcType = CreateLLVMFunction(module, method);
        var exportedName = isEntryPoint
            ? "managed_Main"
            : runtimeExportName ?? symbolName ?? nativeSymbolName ?? friendlyName;
        var reusableNativeSymbol = pinvoke is not null || directExport || isEntryPoint;
        var funcValue = reusableNativeSymbol ? module.GetNamedFunction(exportedName) : default;
        if (funcValue == default)
        {
            funcValue = module.AddFunction(exportedName, funcType);
        }
        else if (!GetFunctionType(funcValue).Equals(funcType))
        {
            throw new InvalidOperationException($"Native symbol '{exportedName}' has incompatible signatures.");
        }
        funcValue.FunctionCallConv = (uint)LLVMCallConv.LLVMCCallConv;
        SetBodyLinkage(funcValue);
        moduleMethods.Add(friendlyName, new(funcValue, funcType, method, instructions));
        if (isRuntimeGenerated)
        {
            runtimeGeneratedMethods.Add((funcValue, funcType, method));
            return;
        }
        if (instructions?.Count > 0 && queuedMethodTranslations.Add(friendlyName))
            pendingMethodTranslations.Enqueue(friendlyName);
    }

    internal new void GenerateRuntimeMethodBodies()
    {
        foreach (var (function, functionType, method) in runtimeGeneratedMethods)
        {
            var arrayKind = GetArrayRuntimeMethodKind(method);
            if (arrayKind != ArrayRuntimeMethodKind.None)
                GenerateArrayRuntimeMethod(function, method, arrayKind);
            else if (IsRuntimeDelegateConstructor(method))
                GenerateDelegateConstructor(function);
            else if (IsRuntimeDelegateInvoke(method))
                GenerateDelegateInvoke(function, method);
            else
                throw new InvalidOperationException($"Unknown runtime-generated method: {method.FullName}");
        }
    }

    private void GenerateDelegateConstructor(LLVMValueRef function)
    {
        var builder = context.CreateBuilder();
        builder.PositionAtEnd(function.AppendBasicBlock("entry"));
        var instance = function.GetParam(0);
        StoreField(builder, instance, coreLib.DelegateTargetField, function.GetParam(1));
        StoreField(builder, instance, coreLib.DelegateFunctionField, function.GetParam(2));
        builder.BuildRetVoid();
        builder.Dispose();
    }

    private void GenerateDelegateInvoke(LLVMValueRef function, MethodReference method)
    {
        var builder = context.CreateBuilder();
        var entry = function.AppendBasicBlock("entry");
        var dispatch = function.AppendBasicBlock("dispatch");
        var call = function.AppendBasicBlock("call");
        var continuation = function.AppendBasicBlock("continuation");
        builder.PositionAtEnd(entry);

        var returnType = SubstituteGenericParameter(method.ReturnType, method);
        var currentSlot = builder.BuildAlloca(LLVMTypeRef.CreatePointer(int8Type, 0));
        builder.BuildStore(function.GetParam(0), currentSlot);
        LLVMValueRef resultSlot = default;
        if (!IsVoidType(returnType))
            resultSlot = builder.BuildAlloca(GetCallType(returnType));
        builder.BuildBr(dispatch);

        builder.PositionAtEnd(dispatch);
        var current = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0), currentSlot);
        builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, current,
            LLVMValueRef.CreateConstNull(current.TypeOf)), continuation, call);

        builder.PositionAtEnd(call);
        var target = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, current, coreLib.DelegateTargetField));
        var targetParameters = method.Parameters.Select(parameter =>
            GetCallType(SubstituteGenericParameter(parameter.ParameterType, method))).ToArray();
        var targetFunctionType = LLVMTypeRef.CreateFunction(GetCallType(returnType),
            [LLVMTypeRef.CreatePointer(int8Type, 0), .. targetParameters]);
        var callArguments = new List<LLVMValueRef>();
        callArguments.Add(target);
        for (var index = 0; index < method.Parameters.Count; index++)
            callArguments.Add(function.GetParam(1u + (uint)index));
        var functionPointer = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, current, coreLib.DelegateFunctionField));
        var result = builder.BuildCall2(targetFunctionType, functionPointer, callArguments.ToArray());
        if (!IsVoidType(returnType))
            builder.BuildStore(result, resultSlot);
        var next = builder.BuildLoad2(LLVMTypeRef.CreatePointer(int8Type, 0),
            GetFieldAddress(builder, current, coreLib.DelegateNextField));
        builder.BuildStore(next, currentSlot);
        builder.BuildBr(dispatch);

        builder.PositionAtEnd(continuation);
        if (IsVoidType(returnType))
            builder.BuildRetVoid();
        else
            builder.BuildRet(builder.BuildLoad2(GetCallType(returnType), resultSlot));
        builder.Dispose();
    }

    private void GenerateArrayRuntimeMethod(LLVMValueRef function, MethodReference method,
        ArrayRuntimeMethodKind kind)
    {
        var arrayType = (ArrayType)method.DeclaringType;
        var elementType = arrayType.ElementType;
        var builder = context.CreateBuilder();
        builder.PositionAtEnd(function.AppendBasicBlock("entry"));
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        var exceptionThrow = EnsureMethodRegistered(coreLib.ExceptionThrowMethod);

        void EmitException(LLVMValueRef condition, TypeReference exceptionType)
        {
            var fail = function.AppendBasicBlock($"fail.{nextVirtualDispatchId++}");
            var next = function.AppendBasicBlock($"next.{nextVirtualDispatchId++}");
            builder.BuildCondBr(condition, fail, next);
            builder.PositionAtEnd(fail);
            var exception = BuildAllocation(builder, GetObjectSize(exceptionType));
            InitializeRuntimeType(builder, exception, exceptionType);
            builder.BuildCall2(exceptionThrow.Item2, exceptionThrow.Item1, [exception]);
            builder.BuildUnreachable();
            builder.PositionAtEnd(next);
        }

        if (kind == ArrayRuntimeMethodKind.Constructor)
        {
            var dimensions = Enumerable.Range(0, arrayType.Rank)
                .Select(index => function.GetParam((uint)index)).ToArray();
            var total = LLVMValueRef.CreateConstInt(sizeType, 1, false);
            var maximum = LLVMValueRef.CreateConstInt(sizeType,
                pointerSize == 4 ? uint.MaxValue : ulong.MaxValue, false);
            foreach (var dimension in dimensions)
            {
                var nativeDimension = ConvertValue(builder, dimension, sizeType);
                EmitException(builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, nativeDimension,
                    LLVMValueRef.CreateConstInt(sizeType, 0, false)), coreLib.OverflowException);
                var isNonZero = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, nativeDimension,
                    LLVMValueRef.CreateConstInt(sizeType, 0, false));
                var exceeds = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, total,
                    builder.BuildUDiv(maximum, builder.BuildSelect(isNonZero, nativeDimension,
                        LLVMValueRef.CreateConstInt(sizeType, 1, false))));
                EmitException(builder.BuildAnd(isNonZero, exceeds), coreLib.OverflowException);
                total = builder.BuildMul(total, nativeDimension);
            }

            EmitException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, total, maximum),
                coreLib.OverflowException);
            var allocationCount = builder.BuildAdd(total, LLVMValueRef.CreateConstInt(sizeType, 1, false));
            var elementSize = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetTypeSize(elementType), false);
            var hasElements = builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, allocationCount,
                LLVMValueRef.CreateConstInt(sizeType, 0, false));
            var dataOverflow = builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, allocationCount,
                builder.BuildUDiv(maximum, builder.BuildSelect(hasElements, elementSize,
                    LLVMValueRef.CreateConstInt(sizeType, 1, false))));
            EmitException(builder.BuildAnd(hasElements, dataOverflow), coreLib.OverflowException);
            var dataSize = builder.BuildMul(allocationCount, elementSize);
            var baseSize = LLVMValueRef.CreateConstInt(sizeType,
                (ulong)GetTypeDefinitionSize(coreLib.Array), false);
            EmitException(builder.BuildICmp(LLVMIntPredicate.LLVMIntUGT, baseSize,
                builder.BuildSub(maximum, dataSize)), coreLib.OverflowException);
            var array = BuildAllocationSize(builder, builder.BuildAdd(baseSize, dataSize));
            StoreField(builder, array, GetArrayLengthField(), total);
            InitializeRuntimeType(builder, array, arrayType);

            var rootSlot = builder.BuildAlloca(pointerType);
            rootSlot.Alignment = (uint)pointerSize;
            builder.BuildStore(array, rootSlot);
            var rootEntriesType = LLVMTypeRef.CreateArray(pointerType, 2);
            var rootEntries = builder.BuildAlloca(rootEntriesType);
            rootEntries.Alignment = (uint)pointerSize;
            var zero = LLVMValueRef.CreateConstInt(sizeType, 0, false);
            builder.BuildStore(builder.BuildBitCast(rootSlot, pointerType),
                builder.BuildGEP2(rootEntriesType, rootEntries, [zero, zero]));
            builder.BuildStore(LLVMValueRef.CreateConstNull(pointerType),
                builder.BuildGEP2(rootEntriesType, rootEntries,
                    [zero, LLVMValueRef.CreateConstInt(sizeType, 1, false)]));
            var rootFrameType = LLVMTypeRef.CreateArray(int8Type, (uint)GetTypeSize(coreLib.GCFrame));
            var rootFrame = builder.BuildAlloca(rootFrameType);
            rootFrame.Alignment = (uint)pointerSize;
            var gcPush = EnsureMethodRegistered(coreLib.GCPushMethod);
            var gcPop = EnsureMethodRegistered(coreLib.GCPopMethod);
            builder.BuildCall2(gcPush.Item2, gcPush.Item1,
                [rootFrame, rootEntries, LLVMValueRef.CreateConstInt(int32Type, 1, false)]);
            StoreField(builder, array, GetArrayLengthsField(), BuildArrayLengthTable(builder, dimensions));
            builder.BuildCall2(gcPop.Item2, gcPop.Item1, [rootFrame]);
            builder.BuildRet(array);
            builder.Dispose();
            return;
        }

        var arrayValue = function.GetParam(0);
        EmitException(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, arrayValue,
            LLVMValueRef.CreateConstNull(pointerType)), coreLib.NullReferenceException);
        var indices = Enumerable.Range(0, arrayType.Rank)
            .Select(index => function.GetParam(1u + (uint)index)).ToArray();
        var lengths = builder.BuildLoad2(GetLLVMTypeRef(GetArrayLengthsField().FieldType),
            GetFieldAddress(builder, arrayValue, GetArrayLengthsField()));
        for (var index = 0; index < indices.Length; index++)
        {
            var nativeIndex = ConvertValue(builder, indices[index], sizeType);
            var length = ConvertValue(builder, builder.BuildLoad2(int32Type,
                GetArrayElementAddress(builder, lengths,
                    LLVMValueRef.CreateConstInt(sizeType, (ulong)index, false), int32Type)), sizeType, false);
            EmitException(builder.BuildICmp(LLVMIntPredicate.LLVMIntUGE, nativeIndex, length),
                coreLib.IndexOutOfRangeException);
        }
        var address = GetMultiArrayElementAddress(builder, arrayValue, indices,
            GetLLVMTypeRef(elementType), GetTypeSize(elementType));
        if (kind == ArrayRuntimeMethodKind.Set)
        {
            var value = function.GetParam(1u + (uint)arrayType.Rank);
            StoreValue(builder, address, value, elementType);
            builder.BuildRetVoid();
        }
        else if (kind == ArrayRuntimeMethodKind.Get)
            builder.BuildRet(builder.BuildLoad2(GetCallType(elementType), address));
        else
            builder.BuildRet(ConvertValue(builder, address, GetLLVMTypeRef(method.ReturnType)));
        builder.Dispose();
    }

    private string GetPInvokeNativeSymbolName(MethodReference method, string friendlyName, PInvokeInfo pinvoke)
    {
        if (!string.IsNullOrEmpty(pinvoke.EntryPoint) && pinvoke.EntryPoint != method.Name)
            return pinvoke.EntryPoint;

        var definition = method.Resolve();
        var hasPInvokeOverloads = definition?.DeclaringType.Methods.Count(candidate =>
            candidate.Name == method.Name && candidate.PInvokeInfo is not null) > 1;
        return pinvoke.Module?.Name == CoreLibMetadata.NativeModuleName && hasPInvokeOverloads
            ? friendlyName
            : method.Name;
    }

    private string? GetRuntimeExportName(MethodReference method)
    {
        var definition = method.Resolve();
        if (definition is not { HasBody: true, IsStatic: true } || definition.IsSpecialName)
            return null;

        var export = definition.CustomAttributes.FirstOrDefault(attribute =>
            coreLib.IsRuntimeExportAttribute(attribute.AttributeType));
        if (export is null)
            return null;
        if (export.ConstructorArguments.Count != 1 ||
            export.ConstructorArguments[0].Value is not string name || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"RuntimeExport on '{method.FullName}' must specify a non-empty export name.");
        return name;
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
