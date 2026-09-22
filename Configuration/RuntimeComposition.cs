namespace OnlineOs.AiOrchestrator.Configuration;

public enum ProjectComponentKind
{
    Provider,
    Validator,
    Policy,
    Capability
}

public sealed record ProjectComponentId(ProjectComponentKind Kind, string Name)
{
    public override string ToString() => $"{Kind}:{Name}";
}

/// <summary>
/// Marker for a named policy registration. Policy behavior remains owned by the
/// existing typed policy implementations; this contract only makes ownership and
/// selection explicit at the composition boundary.
/// </summary>
public interface IProjectPolicyComponent
{
    string Name { get; }
}

public sealed record NamedProjectPolicyComponent(string Name) : IProjectPolicyComponent;

public sealed record RuntimeComponentStatus(
    ProjectComponentId Id,
    bool Registered,
    bool Resolved,
    string? ContractType);

public sealed class CompositionResolutionException : InvalidOperationException
{
    public CompositionResolutionException(string message, IReadOnlyList<ProjectComponentId> missingComponents)
        : base(message) => MissingComponents = missingComponents;

    public CompositionResolutionException(string message, ProjectComponentId component, Exception innerException)
        : base(message, innerException) => FailedComponent = component;

    public IReadOnlyList<ProjectComponentId> MissingComponents { get; } = [];
    public ProjectComponentId? FailedComponent { get; }
}

/// <summary>
/// Explicit runtime registration point for one declared project composition.
/// No component is inferred from project identity, directory names, or environment
/// variables. Registrations not declared by the project are retained as unused and
/// are never instantiated.
/// </summary>
public sealed class EngineCompositionRuntimeBuilder
{
    private readonly IProjectComposition composition;
    private readonly Dictionary<string, RuntimeRegistration> registrations = new(StringComparer.OrdinalIgnoreCase);

    public EngineCompositionRuntimeBuilder(IProjectComposition composition)
    {
        this.composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public EngineCompositionRuntimeBuilder RegisterProvider<T>(string name, Func<T> factory) where T : class
        => Register(ProjectComponentKind.Provider, name, typeof(T), factory);

    public EngineCompositionRuntimeBuilder RegisterValidator<T>(string name, Func<T> factory) where T : class
        => Register(ProjectComponentKind.Validator, name, typeof(T), factory);

    public EngineCompositionRuntimeBuilder RegisterPolicy(string name, Func<IProjectPolicyComponent> factory)
        => Register(ProjectComponentKind.Policy, name, typeof(IProjectPolicyComponent), factory);

    public EngineCompositionRuntimeBuilder RegisterCapability<T>(string name, Func<T> factory) where T : class
        => Register(ProjectComponentKind.Capability, name, typeof(T), factory);

    public IReadOnlyList<ProjectComponentId> DeclaredComponents => EnumerateDeclared().ToArray();

    public IReadOnlyList<ProjectComponentId> RegisteredComponents => registrations.Values.Select(x => x.Id).ToArray();

    public EngineCompositionRuntime Build()
    {
        var declared = EnumerateDeclared().ToArray();
        var missing = declared.Where(component => !registrations.ContainsKey(Key(component))).ToArray();
        if (missing.Length > 0)
        {
            var detail = string.Join(", ", missing.Select(x => x.ToString()));
            throw new CompositionResolutionException(
                $"Project '{composition.ProjectId}' declares runtime components without registrations: {detail}. " +
                "Register each required component explicitly before creating the host.", missing);
        }

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in declared)
        {
            var registration = registrations[Key(component)];
            try
            {
                resolved[Key(component)] = registration.Create();
            }
            catch (Exception exception)
            {
                throw new CompositionResolutionException(
                    $"Runtime registration failed for {component}: {exception.Message}", component, exception);
            }
        }

        return new EngineCompositionRuntime(composition, declared, registrations.Values.Select(x => x.Id).ToArray(), resolved, registrations);
    }

    private EngineCompositionRuntimeBuilder Register(ProjectComponentKind kind, string name, Type contractType, Delegate factory)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Component name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(factory);

        var id = new ProjectComponentId(kind, name.Trim());
        if (!registrations.TryAdd(Key(id), new RuntimeRegistration(id, contractType, factory)))
            throw new InvalidOperationException($"Runtime component is already registered: {id}.");
        return this;
    }

    private IEnumerable<ProjectComponentId> EnumerateDeclared()
    {
        foreach (var provider in composition.Providers) yield return new(ProjectComponentKind.Provider, provider);
        foreach (var validator in composition.Validators) yield return new(ProjectComponentKind.Validator, validator);
        foreach (var policy in composition.Policies) yield return new(ProjectComponentKind.Policy, policy);
        foreach (var capability in composition.Capabilities) yield return new(ProjectComponentKind.Capability, capability);
    }

    internal static string Key(ProjectComponentId component) => $"{component.Kind}:{component.Name.Trim()}";

    internal sealed record RuntimeRegistration(ProjectComponentId Id, Type ContractType, Delegate Factory)
    {
        public object Create() => Factory.DynamicInvoke() ?? throw new InvalidOperationException($"Runtime factory returned null for {Id}.");
    }
}

public sealed class EngineCompositionRuntime
{
    private readonly IReadOnlyDictionary<string, object> resolved;
    private readonly IReadOnlyDictionary<string, EngineCompositionRuntimeBuilder.RuntimeRegistration> registrations;

    internal EngineCompositionRuntime(
        IProjectComposition composition,
        IReadOnlyList<ProjectComponentId> declared,
        IReadOnlyList<ProjectComponentId> registered,
        IReadOnlyDictionary<string, object> resolved,
        IReadOnlyDictionary<string, EngineCompositionRuntimeBuilder.RuntimeRegistration> registrations)
    {
        ProjectId = composition.ProjectId;
        DeclaredComponents = declared;
        RegisteredComponents = registered;
        ResolvedComponents = declared;
        this.resolved = resolved;
        this.registrations = registrations;
    }

    public string ProjectId { get; }
    public IReadOnlyList<ProjectComponentId> DeclaredComponents { get; }
    public IReadOnlyList<ProjectComponentId> RegisteredComponents { get; }
    public IReadOnlyList<ProjectComponentId> ResolvedComponents { get; }

    public IReadOnlyList<RuntimeComponentStatus> Statuses =>
        RegisteredComponents
            .Select(component => new RuntimeComponentStatus(
                component,
                RegisteredComponents.Contains(component),
                resolved.ContainsKey(EngineCompositionRuntimeBuilder.Key(component)),
                registrations.TryGetValue(EngineCompositionRuntimeBuilder.Key(component), out var registration)
                    ? registration.ContractType.FullName
                    : null))
            .ToArray();

    public T ResolveProvider<T>(string name) where T : class => Resolve<T>(ProjectComponentKind.Provider, name);
    public T ResolveValidator<T>(string name) where T : class => Resolve<T>(ProjectComponentKind.Validator, name);
    public IProjectPolicyComponent ResolvePolicy(string name) => Resolve<IProjectPolicyComponent>(ProjectComponentKind.Policy, name);
    public T ResolveCapability<T>(string name) where T : class => Resolve<T>(ProjectComponentKind.Capability, name);

    private T Resolve<T>(ProjectComponentKind kind, string name) where T : class
    {
        var id = new ProjectComponentId(kind, name);
        if (!resolved.TryGetValue(EngineCompositionRuntimeBuilder.Key(id), out var value))
            throw new CompositionResolutionException($"Runtime component was not resolved: {id}.", [id]);
        if (value is not T typed)
            throw new InvalidOperationException($"Runtime component {id} does not implement {typeof(T).FullName}.");
        return typed;
    }
}
