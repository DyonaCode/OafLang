using System.Security.Cryptography;
using System.Text;

namespace Oaf.Tooling.PackageManagement;

public readonly record struct OafPackageDependency(string Name, string Version)
{
    public override string ToString()
    {
        return $"{Name}@{Version}";
    }
}

public static class OafPackageManager
{
    public const string DefaultManifestFileName = "packages.txt";
    public const string DefaultLockFileName = "packages.lock";

    public static bool InitManifest(string manifestPath, out string message)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            message = "Manifest path is required.";
            return false;
        }

        var fullPath = Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            message = $"Manifest already exists at '{fullPath}'.";
            return true;
        }

        File.WriteAllLines(
            fullPath,
            [
                "# Oaf package manifest",
                "# One dependency per line in the format name@version"
            ]);

        message = $"Created manifest at '{fullPath}'.";
        return true;
    }

    public static bool AddDependency(string manifestPath, string dependencySpec, out string message)
    {
        if (!TryParseDependencySpec(dependencySpec, out var dependency))
        {
            message = $"Invalid dependency spec '{dependencySpec}'. Expected name@version.";
            return false;
        }

        var dependencies = ReadManifestDependencies(manifestPath, out var readError);
        if (dependencies is null)
        {
            message = readError!;
            return false;
        }

        var index = dependencies.FindIndex(existing => string.Equals(existing.Name, dependency.Name, StringComparison.Ordinal));
        if (index >= 0)
        {
            dependencies[index] = dependency;
        }
        else
        {
            dependencies.Add(dependency);
        }

        dependencies.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return WriteManifestDependencies(manifestPath, dependencies, out message);
    }

    public static bool RemoveDependency(string manifestPath, string packageName, out string message)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            message = "Package name is required.";
            return false;
        }

        var dependencies = ReadManifestDependencies(manifestPath, out var readError);
        if (dependencies is null)
        {
            message = readError!;
            return false;
        }

        var removed = dependencies.RemoveAll(dep => string.Equals(dep.Name, packageName, StringComparison.Ordinal));
        if (removed == 0)
        {
            message = $"Package '{packageName}' is not listed in '{Path.GetFullPath(manifestPath)}'.";
            return false;
        }

        dependencies.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return WriteManifestDependencies(manifestPath, dependencies, out message);
    }

    public static bool Install(string manifestPath, out string message)
    {
        var dependencies = ReadManifestDependencies(manifestPath, out var readError);
        if (dependencies is null)
        {
            message = readError!;
            return false;
        }

        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestFullPath) ?? Directory.GetCurrentDirectory();
        var packageRoot = Path.Combine(manifestDirectory, ".oaf", "packages");
        Directory.CreateDirectory(packageRoot);

        foreach (var dependency in dependencies)
        {
            var packageDirectory = Path.Combine(packageRoot, dependency.Name, dependency.Version);
            Directory.CreateDirectory(packageDirectory);

            var metadata = new StringBuilder();
            metadata.AppendLine($"name={dependency.Name}");
            metadata.AppendLine($"version={dependency.Version}");
            metadata.AppendLine($"source=manifest");
            metadata.AppendLine($"hash={ComputeDependencyHash(dependency)}");

            File.WriteAllText(Path.Combine(packageDirectory, "package.meta"), metadata.ToString());
        }

        var lockPath = Path.Combine(manifestDirectory, DefaultLockFileName);
        File.WriteAllText(lockPath, BuildLockFileContent(dependencies));

        message = $"Installed {dependencies.Count} package(s). Lock file written to '{lockPath}'.";
        return true;
    }

    public static bool TryParseDependencySpec(string spec, out OafPackageDependency dependency)
    {
        dependency = default;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var atIndex = spec.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= spec.Length - 1)
        {
            return false;
        }

        var name = spec[..atIndex].Trim();
        var version = spec[(atIndex + 1)..].Trim();
        if (!IsValidDependencyPart(name) || !IsValidDependencyPart(version))
        {
            return false;
        }

        dependency = new OafPackageDependency(name, version);
        return true;
    }

    private static bool IsValidDependencyPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                continue;
            }

            if (ch is '-' or '_' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static List<OafPackageDependency>? ReadManifestDependencies(string manifestPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            error = "Manifest path is required.";
            return null;
        }

        var fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
        {
            error = $"Manifest file '{fullPath}' does not exist.";
            return null;
        }

        var dependencies = new List<OafPackageDependency>();
        var lines = File.ReadAllLines(fullPath);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!TryParseDependencySpec(trimmed, out var dependency))
            {
                error = $"Invalid dependency entry '{trimmed}' in '{fullPath}'.";
                return null;
            }

            dependencies.RemoveAll(existing => string.Equals(existing.Name, dependency.Name, StringComparison.Ordinal));
            dependencies.Add(dependency);
        }

        return dependencies;
    }

    private static bool WriteManifestDependencies(string manifestPath, IReadOnlyList<OafPackageDependency> dependencies, out string message)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var lines = new List<string>
        {
            "# Oaf package manifest",
            "# One dependency per line in the format name@version"
        };

        lines.AddRange(dependencies.Select(static dep => dep.ToString()));
        File.WriteAllLines(fullPath, lines);

        message = $"Manifest updated at '{fullPath}'.";
        return true;
    }

    private static string BuildLockFileContent(IReadOnlyList<OafPackageDependency> dependencies)
    {
        var sorted = dependencies.OrderBy(static dep => dep.Name, StringComparer.Ordinal).ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# Oaf package lock");
        builder.AppendLine("format=1");
        builder.AppendLine($"dependency_count={sorted.Length}");

        foreach (var dependency in sorted)
        {
            builder.AppendLine($"{dependency.Name}@{dependency.Version} sha256={ComputeDependencyHash(dependency)}");
        }

        return builder.ToString();
    }

    private static string ComputeDependencyHash(OafPackageDependency dependency)
    {
        var bytes = Encoding.UTF8.GetBytes($"{dependency.Name}@{dependency.Version}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
