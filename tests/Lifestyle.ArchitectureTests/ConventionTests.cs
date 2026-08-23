using System.Reflection;
using Lifestyle.Modules.Catalog;
using Lifestyle.Modules.Identity;
using Lifestyle.Modules.Media;
using Lifestyle.Modules.Platform;
using Lifestyle.Modules.Vendors;
using Lifestyle.SharedKernel.Domain;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;
using Xunit;

namespace Lifestyle.ArchitectureTests;

/// <summary>
/// Conventions that keep the codebase predictable. Each of these is a rule from
/// docs/04-CODEBASE-STRUCTURE.md §10 that a reviewer would otherwise have to remember.
/// </summary>
public sealed class ConventionTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(IdentityModule).Assembly,
        typeof(VendorsModule).Assembly,
        typeof(CatalogModule).Assembly,
        typeof(MediaModule).Assembly,
        typeof(PlatformModule).Assembly
    ];

    public static TheoryData<Assembly> Modules => [.. ModuleAssemblies];

    /// <summary>
    /// Rule 7: no <c>DateTime.Now</c> / <c>UtcNow</c> in module code. Time comes from
    /// <see cref="Lifestyle.SharedKernel.Abstractions.IClock"/>, or tests cannot control it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_do_not_read_the_system_clock(Assembly module)
    {
        var forbidden = new[]
        {
            "System.DateTime::get_Now()",
            "System.DateTime::get_UtcNow()",
            "System.DateTimeOffset::get_Now()",
            "System.DateTimeOffset::get_UtcNow()"
        };

        var violations = FindCalls(module, forbidden);

        violations.ShouldBeEmpty(
            "Module code must take time from IClock, never the system clock directly. "
            + $"Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations.Take(20))}");
    }

    /// <summary>
    /// <c>Guid.NewGuid()</c> produces a random v4 id, which fragments every index it is written to.
    /// <see cref="Entity"/> assigns <c>Guid.CreateVersion7()</c>, which is time-ordered.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_do_not_use_random_guids(Assembly module)
    {
        var violations = FindCalls(module, ["System.Guid::NewGuid()"]);

        violations.ShouldBeEmpty(
            "Use Guid.CreateVersion7() — v4 ids fragment the index. "
            + $"Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations.Take(20))}");
    }

    /// <summary>
    /// Every aggregate root must be sealed. An inheritable aggregate invites a subclass that
    /// bypasses the parent's invariants.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Aggregates_are_sealed(Assembly module)
    {
        var offenders = module.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(AggregateRoot).IsAssignableFrom(t))
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName!)
            .ToList();

        offenders.ShouldBeEmpty($"Aggregate roots must be sealed: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Entities expose no public setters. State changes go through named methods that can enforce
    /// invariants; a public setter is a way to put an aggregate into an impossible state.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Entities_have_no_public_setters(Assembly module)
    {
        var offenders = module.GetTypes()
            .Where(t => t.IsClass && typeof(Entity).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"))
            // ISoftDeletable's members are set by the SaveChanges interceptor, which sits outside
            // the aggregate and so cannot use a private setter.
            .Where(name => !name.EndsWith(".DeletedAt", StringComparison.Ordinal)
                           && !name.EndsWith(".DeletedBy", StringComparison.Ordinal)
                           && !name.EndsWith(".CreatedAt", StringComparison.Ordinal)
                           && !name.EndsWith(".CreatedBy", StringComparison.Ordinal)
                           && !name.EndsWith(".UpdatedAt", StringComparison.Ordinal)
                           && !name.EndsWith(".UpdatedBy", StringComparison.Ordinal))
            .ToList();

        offenders.ShouldBeEmpty(
            "Entity state must change through methods, not public setters. "
            + $"Found: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Every module exposes exactly one registration entry point, so the host never needs to know
    /// what is inside (docs/04 §3.1).
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_has_a_single_entry_point(Assembly module)
    {
        var entryPoints = module.GetTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true, IsPublic: true })
            .Where(t => t.Name.EndsWith("Module", StringComparison.Ordinal))
            .ToList();

        entryPoints.Count.ShouldBe(1,
            $"{module.GetName().Name} must expose exactly one public static *Module class.");

        var entryPoint = entryPoints[0];

        entryPoint.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ShouldContain(m => m.Name.StartsWith("Add", StringComparison.Ordinal),
                $"{entryPoint.Name} must expose an Add*Module extension method.");
    }

    /// <summary>
    /// Reads the IL to find calls to specific methods. Reflection alone cannot see method bodies,
    /// so Cecil does the work.
    /// </summary>
    private static List<string> FindCalls(Assembly assembly, string[] forbiddenSignatures)
    {
        using var definition = AssemblyDefinition.ReadAssembly(assembly.Location);
        var violations = new List<string>();

        foreach (var type in definition.MainModule.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                    if (instruction.Operand is not MethodReference called) continue;

                    var signature = $"{called.DeclaringType.FullName}::{called.Name}()";

                    if (forbiddenSignatures.Contains(signature, StringComparer.Ordinal))
                        violations.Add($"{type.FullName}.{method.Name} calls {signature}");
                }
            }
        }

        return violations;
    }
}
