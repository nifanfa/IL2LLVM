using Mono.Cecil;
using Mono.Cecil.Cil;

sealed class AssemblyTrimmer
{
    internal readonly record struct TrimStatistics(int Definitions, int Bodies, int IlBytes);

    private readonly AssemblyDefinition assembly;
    private readonly CoreLibMetadata coreLib;
    private readonly Dictionary<string, MethodDefinition> methods;
    private readonly bool hasDirectFactoryAccess;
    private readonly HashSet<MethodDefinition> reachable = [];
    private readonly Queue<MethodDefinition> pending = [];
    private readonly Dictionary<string, MethodReference> virtualCalls = new(StringComparer.Ordinal);
    private readonly HashSet<TypeDefinition> boxedValueTypes = [];
    private bool hasUnresolvedBox;

    private AssemblyTrimmer(AssemblyDefinition assembly)
    {
        this.assembly = assembly;
        var types = GetAllTypes(assembly.MainModule.Types).ToArray();
        coreLib = new CoreLibMetadata(types.ToDictionary(type => type.FullName, StringComparer.Ordinal));
        methods = types
            .SelectMany(type => type.Methods)
            .ToDictionary(method => method.FullName, StringComparer.Ordinal);
        var factoryField = coreLib.TypeFactoryField.FullName;
        var activatorMethod = coreLib.ActivatorCreateInstanceMethod;
        hasDirectFactoryAccess = methods.Values.Any(method => method.HasBody &&
            !ReferenceEquals(method, activatorMethod) && method.Body.Instructions.Any(instruction =>
                instruction.Operand is FieldReference field && field.FullName == factoryField));
    }

    internal static TrimStatistics Trim(AssemblyDefinition assembly)
    {
        var trimmer = new AssemblyTrimmer(assembly);
        trimmer.StripUnusedAttributes();
        trimmer.AddRoots();
        trimmer.Process();
        var removed = trimmer.methods.Values.Where(method => !trimmer.reachable.Contains(method)).ToArray();
        var removedBodies = removed.Where(method => method.HasBody).ToArray();
        var statistics = new TrimStatistics(removed.Length, removedBodies.Length,
            removedBodies.Sum(method => method.Body.CodeSize));
        trimmer.RemoveUnreachableMethods();
        return statistics;
    }

    private void AddRoots()
    {
        Add(assembly.EntryPoint);
        foreach (var method in methods.Values)
            if (method.CustomAttributes.Any(attribute =>
                    coreLib.IsRuntimeExportAttribute(attribute.AttributeType)))
                Add(method);

        foreach (var method in coreLib.RuntimeRoots)
            Add(method);
    }

    private void StripUnusedAttributes()
    {
        Strip(assembly.CustomAttributes);
        Strip(assembly.MainModule.CustomAttributes);
        foreach (var type in GetAllTypes(assembly.MainModule.Types))
        {
            Strip(type.CustomAttributes);
            foreach (var field in type.Fields)
                Strip(field.CustomAttributes);
            foreach (var property in type.Properties)
                Strip(property.CustomAttributes);
            foreach (var eventDefinition in type.Events)
                Strip(eventDefinition.CustomAttributes);
            foreach (var parameter in type.GenericParameters)
                Strip(parameter.CustomAttributes);
            foreach (var method in type.Methods)
            {
                Strip(method.CustomAttributes);
                Strip(method.MethodReturnType.CustomAttributes);
                foreach (var parameter in method.Parameters)
                    Strip(parameter.CustomAttributes);
                foreach (var parameter in method.GenericParameters)
                    Strip(parameter.CustomAttributes);
            }
        }

        void Strip(Collection<CustomAttribute> attributes)
        {
            for (int index = attributes.Count - 1; index >= 0; index--)
            {
                var attribute = attributes[index];
                if (coreLib.IsFlagsAttribute(attribute.AttributeType) ||
                    coreLib.IsRuntimeExportAttribute(attribute.AttributeType))
                    Add(Resolve(attribute.Constructor));
                else
                    attributes.RemoveAt(index);
            }
        }
    }

    private void Process()
    {
        do
        {
            while (pending.Count != 0)
            {
                var method = pending.Dequeue();
                foreach (var ovr in method.Overrides)
                    Add(Resolve(ovr));
                if (!method.HasBody)
                    continue;

                Add(method.DeclaringType.Methods.FirstOrDefault(candidate => candidate.IsConstructor && candidate.IsStatic));
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference methodReference)
                    {
                        var target = Resolve(methodReference);
                        Add(target);
                        if (methodReference is GenericInstanceMethod genericMethod)
                            foreach (var argument in genericMethod.GenericArguments)
                                AddDefaultConstructor(argument);
                        AddGenericTypeConstructors(methodReference.DeclaringType);
                        if (methodReference.DeclaringType.Resolve()?.IsInterface == true ||
                            ((instruction.OpCode.Code is Code.Callvirt or Code.Ldvirtftn) && target?.IsVirtual != false))
                            virtualCalls.TryAdd(methodReference.FullName, methodReference);
                    }
                    else if (instruction.Operand is FieldReference fieldReference && fieldReference.Resolve()?.IsStatic == true)
                    {
                        var fieldType = fieldReference.DeclaringType.Resolve();
                        Add(fieldType?.Methods.FirstOrDefault(candidate => candidate.IsConstructor && candidate.IsStatic));
                        AddGenericTypeConstructors(fieldReference.DeclaringType);
                    }
                    else if (instruction.Operand is TypeReference typeReference)
                    {
                        if (instruction.OpCode.Code is Code.Box or Code.Constrained)
                            RecordBoxedType(typeReference);
                        if (instruction.OpCode.Code == Code.Ldtoken && hasDirectFactoryAccess)
                        {
                            AddDefaultConstructor(typeReference);
                            AddGenericTypeConstructors(typeReference);
                        }
                    }
                }
            }

            foreach (var target in virtualCalls.Values)
                AddVirtualImplementations(target);
        }
        while (pending.Count != 0);
    }

    private void RecordBoxedType(TypeReference type)
    {
        if (HasUnresolvedType(type))
        {
            hasUnresolvedBox = true;
            return;
        }
        if (type is GenericInstanceType nullable && coreLib.IsNullable(nullable))
            type = nullable.GenericArguments[0];
        if (type.Resolve() is { IsValueType: true } definition)
            boxedValueTypes.Add(definition);
    }

    private static bool HasUnresolvedType(TypeReference type)
    {
        return type switch
        {
            GenericParameter => true,
            GenericInstanceType generic => generic.GenericArguments.Any(HasUnresolvedType),
            TypeSpecification specification => HasUnresolvedType(specification.ElementType),
            _ => type.HasGenericParameters
        };
    }

    private void AddGenericTypeConstructors(TypeReference type)
    {
        if (type is GenericInstanceType genericType)
            foreach (var argument in genericType.GenericArguments)
                AddDefaultConstructor(argument);
    }

    private void AddDefaultConstructor(TypeReference type)
    {
        var definition = type.Resolve();
        if (definition is not null)
            Add(definition.Methods.FirstOrDefault(method => method.IsConstructor && !method.IsStatic &&
                method.IsPublic && method.Parameters.Count == 0));
    }

    private void AddVirtualImplementations(MethodReference target)
    {
        var targetDefinition = target.Resolve();
        if (targetDefinition is null)
            return;

        var implementingTypes = targetDefinition.DeclaringType.IsInterface
            ? GetAllTypes(assembly.MainModule.Types)
                .Where(type => !type.IsInterface && IsAssignableTo(type, targetDefinition.DeclaringType)).ToArray()
            : [];
        foreach (var candidate in methods.Values.Where(method =>
                     (method.IsVirtual || targetDefinition.DeclaringType.IsInterface) &&
                     method.IsStatic == targetDefinition.IsStatic &&
                     (method.Name == targetDefinition.Name || method.Name.EndsWith("." + targetDefinition.Name, StringComparison.Ordinal) ||
                      method.Overrides.Any(ovr => ovr.Resolve()?.FullName == targetDefinition.FullName)) &&
                     method.Parameters.Count == targetDefinition.Parameters.Count &&
                     CouldMatchParameters(method, target) &&
                     method.GenericParameters.Count == targetDefinition.GenericParameters.Count &&
                     (IsAssignableTo(method.DeclaringType, targetDefinition.DeclaringType) ||
                      implementingTypes.Any(type => IsAssignableTo(type, method.DeclaringType)))))
        {
            if (!hasUnresolvedBox && candidate.DeclaringType.IsValueType &&
                !boxedValueTypes.Contains(candidate.DeclaringType))
                continue;
            if (!hasUnresolvedBox && ReferenceEquals(candidate.DeclaringType, coreLib.Enum) &&
                !boxedValueTypes.Any(type => type.IsEnum))
                continue;
            Add(candidate);
        }
    }

    private static bool CouldMatchParameters(MethodDefinition candidate, MethodReference target)
    {
        for (int index = 0; index < candidate.Parameters.Count; index++)
        {
            var candidateType = candidate.Parameters[index].ParameterType;
            var targetType = target.Parameters[index].ParameterType;
            if (!HasUnresolvedType(candidateType) && !HasUnresolvedType(targetType) &&
                candidateType.FullName != targetType.FullName)
                return false;
        }
        return true;
    }

    private MethodDefinition? Resolve(MethodReference reference)
    {
        var definition = reference.Resolve();
        if (definition is null || !methods.TryGetValue(definition.FullName, out var localMethod))
            return null;
        return localMethod;
    }

    private void Add(MethodDefinition? method)
    {
        if (method is not null && reachable.Add(method))
            pending.Enqueue(method);
    }

    private void RemoveUnreachableMethods()
    {
        foreach (var type in GetAllTypes(assembly.MainModule.Types))
            for (int index = type.Methods.Count - 1; index >= 0; index--)
                if (!reachable.Contains(type.Methods[index]))
                    type.Methods.RemoveAt(index);
    }

    private bool IsAssignableTo(TypeDefinition candidate, TypeDefinition target)
    {
        var pendingTypes = new Queue<TypeDefinition>();
        var visited = new HashSet<TypeDefinition>();
        pendingTypes.Enqueue(candidate);
        while (pendingTypes.Count != 0)
        {
            var current = pendingTypes.Dequeue();
            if (!visited.Add(current))
                continue;
            if (current.FullName == target.FullName)
                return true;
            if (current.BaseType?.Resolve() is { } baseType)
                pendingTypes.Enqueue(baseType);
            foreach (var implemented in current.Interfaces)
                if (implemented.InterfaceType.Resolve() is { } interfaceType)
                    pendingTypes.Enqueue(interfaceType);
        }
        return false;
    }

    private static IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in GetAllTypes(type.NestedTypes))
                yield return nested;
        }
    }
}
