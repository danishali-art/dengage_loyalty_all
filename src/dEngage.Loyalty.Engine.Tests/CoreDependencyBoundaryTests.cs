using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Platform alignment D13: loyalty must run without dEngage (standalone product). The core may use
// dEngage.Common.* libraries, which boot from config files only. dEngage.Framework, Parameters,
// Models, DbManager/ConManager etc. assume the dEngage database and secrets — dEngage.Framework
// even queries sys.global_config from static fields when its types load — so they belong only
// in the optional dEngage connector projects. This test reads each core project's resolved
// dependency closure (obj/project.assets.json) so a transitive reference is caught as well.
public sealed class CoreDependencyBoundaryTests
{
    // dEngage.SecMan arrives transitively through dEngage.Common.Shared (via Common.Hosting).
    // Nothing in the core calls it; allowing it here keeps the platform host usable.
    private static readonly string[] AllowedDengagePackagePrefixes = ["dEngage.Common.", "dEngage.SecMan"];

    // The only projects that may reference dEngage platform libraries (alignment Section 7).
    private static readonly string[] ConnectorProjects =
        ["dEngage.Loyalty.Integrations.Dengage", "dEngage.Loyalty.Connector.Dengage"];

    [Fact]
    public void Core_projects_only_depend_on_config_only_dEngage_libraries()
    {
        var srcDir = Path.Combine(FindRepoRoot(), "src");
        var coreProjects = Directory.GetDirectories(srcDir)
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("dEngage.Loyalty.", StringComparison.Ordinal))
            .Where(name => !ConnectorProjects.Contains(name))
            .Where(name => !name!.EndsWith("Tests", StringComparison.Ordinal))
            .ToList();
        coreProjects.Should().NotBeEmpty();

        var violations = new List<string>();
        foreach (var project in coreProjects)
        {
            var assetsFile = Path.Combine(srcDir, project!, "obj", "project.assets.json");
            File.Exists(assetsFile).Should().BeTrue($"{project} must be restored before this test runs ({assetsFile})");

            using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
            foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package") continue;
                var packageId = library.Name.Split('/')[0];
                if (!packageId.StartsWith("dEngage.", StringComparison.OrdinalIgnoreCase)) continue;
                if (AllowedDengagePackagePrefixes.Any(p => packageId.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                violations.Add($"{project} -> {library.Name}");
            }
        }

        violations.Should().BeEmpty(
            "only the dEngage connector projects may depend on dEngage platform libraries (alignment D13)");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dEngage.Loyalty.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("dEngage.Loyalty.sln not found above the test binaries.");
    }
}
