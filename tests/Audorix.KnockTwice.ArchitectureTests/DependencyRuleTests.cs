using System.Reflection;
using System.Xml.Linq;

namespace Audorix.KnockTwice.ArchitectureTests;

/// <summary>
/// Enforces the inward dependency rules from docs/ARCHITECTURE.md §3.2 and §17.1 at two
/// levels: declared project/package references, and references in compiled assemblies.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string Domain = "Audorix.KnockTwice.Domain";
    private const string Application = "Audorix.KnockTwice.Application";
    private const string Contracts = "Audorix.KnockTwice.Contracts";
    private const string Infrastructure = "Audorix.KnockTwice.Infrastructure";
    private const string Sqlite = "Audorix.KnockTwice.Persistence.Sqlite";
    private const string PostgreSql = "Audorix.KnockTwice.Persistence.PostgreSql";
    private const string Api = "Audorix.KnockTwice.Api";

    /// <summary>The complete allowed project graph. Adding an edge requires updating this table and the docs.</summary>
    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { Domain, [] },
        { Contracts, [] },
        { Application, [Domain] },
        { Infrastructure, [Application, Domain] },
        { Sqlite, [Infrastructure] },
        { PostgreSql, [Infrastructure] },
        { Api, [Application, Contracts, Infrastructure, Sqlite, PostgreSql] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Project_references_match_the_documented_graph(string project, string[] allowed)
    {
        var actual = ReadProject(project).ProjectReferences;
        Assert.Equal(allowed.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_source_project_is_covered_by_the_graph()
    {
        var known = AllowedProjectReferences.Select(row => (string)((ITheoryDataRow)row).GetData()[0]!).ToHashSet(StringComparer.Ordinal);
        var discovered = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToList();
        Assert.All(discovered, name => Assert.Contains(name, known));
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Contracts)]
    public void Inner_projects_have_no_package_references(string project) =>
        Assert.Empty(ReadProject(project).PackageReferences);

    [Fact]
    public void Application_uses_only_framework_abstractions()
    {
        Assert.All(ReadProject(Application).PackageReferences, package =>
            Assert.StartsWith("Microsoft.Extensions.", package, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Domain, new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Microsoft.Data.Sqlite", "Audorix.KnockTwice" })]
    [InlineData(Contracts, new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Microsoft.Data.Sqlite", "Audorix.KnockTwice" })]
    [InlineData(Application, new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "Microsoft.Data.Sqlite", Infrastructure, Sqlite, PostgreSql, Api })]
    [InlineData(Infrastructure, new[] { "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Routing", "Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.SignalR", "Npgsql", "Microsoft.Data.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", Sqlite, PostgreSql, Api })]
    [InlineData(Sqlite, new[] { "Npgsql", "Microsoft.AspNetCore", PostgreSql, Api })]
    [InlineData(PostgreSql, new[] { "Microsoft.Data.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.AspNetCore", Sqlite, Api })]
    public void Compiled_assembly_does_not_reference_forbidden_assemblies(string assemblyName, string[] forbiddenPrefixes)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(r => r.Name!).ToList();

        var violations = references
            .Where(r => r != assemblyName)
            .Where(r => forbiddenPrefixes.Any(prefix => r == prefix || r.StartsWith(prefix + ".", StringComparison.Ordinal)))
            // Domain and Contracts may not reference other ChatX assemblies, but Application may reference Domain.
            .Where(r => !(assemblyName == Application && r == Domain))
            .ToList();

        Assert.True(violations.Count == 0, $"{assemblyName} references forbidden assemblies: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Infrastructure may use ASP.NET Core only for EF-backed persistence of Identity and Data
    /// Protection keys (plus their own abstractions), never HTTP pipeline types.
    /// </summary>
    [Fact]
    public void Infrastructure_uses_only_persistence_parts_of_AspNetCore()
    {
        string[] allowed =
        [
            "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
            "Microsoft.AspNetCore.DataProtection",
            "Microsoft.AspNetCore.DataProtection.Abstractions",
            "Microsoft.AspNetCore.DataProtection.EntityFrameworkCore",
            "Microsoft.AspNetCore.Cryptography.Internal",
        ];
        var aspNetCore = Assembly.Load(Infrastructure).GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToList();
        Assert.All(aspNetCore, name => Assert.Contains(name, allowed));
        Assert.All(ReadProject(Infrastructure).PackageReferences.Where(p => p.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)),
            package => Assert.Contains(package, allowed));
    }

    [Fact]
    public void Domain_and_Application_do_not_reference_Identity()
    {
        foreach (var name in new[] { Domain, Application })
        {
            var references = Assembly.Load(name).GetReferencedAssemblies().Select(r => r.Name!).ToList();
            Assert.DoesNotContain(references, r => r.Contains("Identity", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Contract_types_expose_only_primitive_or_contract_types()
    {
        var contracts = Assembly.Load(Contracts);
        var violations = new List<string>();

        foreach (var type in contracts.GetExportedTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var used in Flatten(property.PropertyType))
                {
                    var ns = used.Namespace ?? string.Empty;
                    var allowed = ns == "System" || ns.StartsWith("System.Collections", StringComparison.Ordinal)
                        || ns.StartsWith(Contracts, StringComparison.Ordinal);
                    if (!allowed)
                    {
                        violations.Add($"{type.FullName}.{property.Name}: {used.FullName}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Contracts must not expose domain or persistence types: " + string.Join("; ", violations));
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.IsArray)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Flatten(argument))
            {
                yield return inner;
            }
        }
    }

    private static (IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences) ReadProject(string name)
    {
        var path = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), name + ".csproj", SearchOption.AllDirectories).Single();
        var document = XDocument.Load(path);
        var projects = document.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .ToList();
        var packages = document.Descendants("PackageReference").Select(e => e.Attribute("Include")!.Value).ToList();
        return (projects, packages);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Audorix.KnockTwice.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (Audorix.KnockTwice.slnx) not found.");
    }
}
