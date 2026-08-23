using System.Reflection;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Shouldly;
using Xunit;

namespace Lifestyle.ArchitectureTests;

/// <summary>
/// Enforces the rules in docs/04-CODEBASE-STRUCTURE.md §2.1. These are the tests that keep the
/// modular monolith modular — without them the boundaries are a convention, and conventions erode.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private const string ContractsNamespaceSegment = ".Contracts";

    private static readonly Assembly Identity = typeof(IdentityModule).Assembly;
    private static readonly Assembly Vendors = typeof(VendorsModule).Assembly;
    private static readonly Assembly Catalog = typeof(CatalogModule).Assembly;
    private static readonly Assembly MediaAssembly = typeof(MediaModule).Assembly;
    private static readonly Assembly PlatformAssembly = typeof(PlatformModule).Assembly;

    public static TheoryData<Assembly> Modules =>
        [Identity, Vendors, Catalog, MediaAssembly, PlatformAssembly];

    /// <summary>
    /// Rule 1: a module may reference another module's <c>Contracts</c> namespace and nothing else.
    /// This is the single most important rule in the codebase — it is what makes a module
    /// extractable later, and what stops the whole thing quietly becoming a big ball of mud.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_uses_only_other_modules_contracts(Assembly module)
    {
        var ownPrefix = ModulePrefix(module);
        var violations = new List<string>();

        foreach (var type in module.GetTypes())
        {
            foreach (var referenced in ReferencedModuleTypes(type))
            {
                var referencedNamespace = referenced.Namespace ?? string.Empty;

                // Types from this module itself are fine.
                if (referencedNamespace.StartsWith(ownPrefix, StringComparison.Ordinal)) continue;

                if (!referencedNamespace.Contains(ContractsNamespaceSegment, StringComparison.Ordinal))
                    violations.Add($"{type.FullName} -> {referenced.FullName}");
            }
        }

        violations.ShouldBeEmpty(
            $"A module may only use another module's Contracts namespace. Violations:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Distinct(StringComparer.Ordinal).Take(25)));
    }

    /// <summary>Rule 2: SharedKernel references no other project in the solution.</summary>
    [Fact]
    public void SharedKernel_does_not_reference_any_module_or_the_host()
    {
        var offenders = typeof(SharedKernel.Domain.Entity).Assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null
                        && a.Name.StartsWith("Lifestyle", StringComparison.Ordinal))
            .Select(a => a.Name!)
            .ToList();

        offenders.ShouldBeEmpty(
            "SharedKernel must not depend on any other Lifestyle project. "
            + $"Found: {string.Join(", ", offenders)}");
    }

    /// <summary>Rule 4: no module references Infrastructure or the API host.</summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_infrastructure_or_api(Assembly module)
    {
        var offenders = module.GetReferencedAssemblies()
            .Where(a => a.Name is "Lifestyle.Infrastructure" or "Lifestyle.Api")
            .Select(a => a.Name!)
            .ToList();

        offenders.ShouldBeEmpty(
            $"{module.GetName().Name} must not reference {string.Join(", ", offenders)}. "
            + "Dependencies point inward: Api -> Infrastructure -> Modules -> SharedKernel.");
    }

    /// <summary>
    /// A module has exactly two public surfaces: its <c>Contracts</c> namespace (what other modules
    /// may use) and the request/response records under <c>Features</c> (the HTTP contract, which
    /// the OpenAPI document publishes to the world anyway).
    /// <para>
    /// Everything that carries behaviour or state — entities, handlers, services, facades, EF
    /// configurations, DbContext interfaces — must be internal. Those are the types that, once
    /// public, get depended on and turn the boundary into a suggestion.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Behavioural_types_are_never_public(Assembly module)
    {
        var offenders = module.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => !(t.Namespace ?? string.Empty).Contains(ContractsNamespaceSegment, StringComparison.Ordinal))
            // The module entry point and its options must be public: the host calls
            // Add*Module()/Map*Endpoints() and binds the options from configuration.
            .Where(t => !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("ModuleOptions", StringComparison.Ordinal))
            // DTOs are data with no behaviour. A public record of primitives cannot be used to
            // reach into this module — rule 1 already stops another module referencing it.
            .Where(t => !IsDataTransferObject(t))
            .Select(t => t.FullName!)
            .ToList();

        offenders.ShouldBeEmpty(
            "Only Contracts types, the module entry point, and HTTP DTOs may be public. "
            + $"Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders.Take(25))}");
    }

    /// <summary>
    /// A DTO is an immutable record with no methods of its own beyond the compiler-generated ones.
    /// A record that grew a service method is no longer a DTO and should not be public.
    /// </summary>
    private static bool IsDataTransferObject(Type type)
    {
        var isRecord = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;
        if (!isRecord) return false;

        var declaredMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.Name is not ("<Clone>$" or "Equals" or "GetHashCode" or "ToString" or "Deconstruct"))
            .ToList();

        return declaredMethods.Count == 0;
    }

    private static string ModulePrefix(Assembly module) => module.GetName().Name!;

    /// <summary>
    /// Every Lifestyle.Modules type this type touches through its own signature: base types,
    /// interfaces, field and property types, and method parameters and returns.
    /// </summary>
    private static IEnumerable<Type> ReferencedModuleTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                                     | BindingFlags.Instance | BindingFlags.Static
                                                     | BindingFlags.DeclaredOnly;

        var candidates = new List<Type?> { type.BaseType };
        candidates.AddRange(type.GetInterfaces());
        candidates.AddRange(type.GetFields(All).Select(f => f.FieldType));
        candidates.AddRange(type.GetProperties(All).Select(p => p.PropertyType));

        foreach (var method in type.GetMethods(All))
        {
            candidates.Add(method.ReturnType);
            candidates.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        foreach (var constructor in type.GetConstructors(All))
            candidates.AddRange(constructor.GetParameters().Select(p => p.ParameterType));

        return candidates
            .Where(t => t is not null)
            .SelectMany(Unwrap!)
            .Where(t => (t.Namespace ?? string.Empty).StartsWith("Lifestyle.Modules.", StringComparison.Ordinal))
            .Distinct();
    }

    /// <summary>Unwraps generics and arrays so Task&lt;Result&lt;VendorSnapshot&gt;&gt; is checked too.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
        {
            foreach (var inner in Unwrap(element)) yield return inner;
        }

        if (!type.IsGenericType) yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Unwrap(argument)) yield return inner;
        }
    }
}
