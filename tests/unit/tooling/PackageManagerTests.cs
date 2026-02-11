using Oaf.Tests.Framework;
using Oaf.Tooling.PackageManagement;

namespace Oaf.Tests.Unit.Tooling;

public static class PackageManagerTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("parses_dependency_spec", ParsesDependencySpec),
            ("installs_manifest_dependencies", InstallsManifestDependencies)
        ];
    }

    private static void ParsesDependencySpec()
    {
        TestAssertions.True(OafPackageManager.TryParseDependencySpec("core.math@1.2.3", out var dependency));
        TestAssertions.Equal("core.math", dependency.Name);
        TestAssertions.Equal("1.2.3", dependency.Version);

        TestAssertions.False(OafPackageManager.TryParseDependencySpec("missingVersion@", out _));
        TestAssertions.False(OafPackageManager.TryParseDependencySpec("@1.0.0", out _));
        TestAssertions.False(OafPackageManager.TryParseDependencySpec("bad name@1.0.0", out _));
    }

    private static void InstallsManifestDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "packages.txt");

            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "beta@2.1.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.1", out _));
            TestAssertions.True(OafPackageManager.RemoveDependency(manifestPath, "beta", out _));
            TestAssertions.True(OafPackageManager.Install(manifestPath, out _));

            var manifestText = File.ReadAllText(manifestPath);
            TestAssertions.True(manifestText.Contains("alpha@1.0.1", StringComparison.Ordinal));
            TestAssertions.False(manifestText.Contains("beta@2.1.0", StringComparison.Ordinal));

            var lockPath = Path.Combine(root, "packages.lock");
            TestAssertions.True(File.Exists(lockPath), "Expected lock file to be generated.");
            var lockText = File.ReadAllText(lockPath);
            TestAssertions.True(lockText.Contains("alpha@1.0.1", StringComparison.Ordinal));

            var metadataPath = Path.Combine(root, ".oaf", "packages", "alpha", "1.0.1", "package.meta");
            TestAssertions.True(File.Exists(metadataPath), "Expected package metadata file.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
