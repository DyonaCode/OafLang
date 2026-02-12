using Oaf.Tooling.PackageManagement;

namespace Oaf.Tooling.Sdk;

public readonly record struct OafProjectScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> CreatedPaths,
    string Message);

public static class OafProjectScaffolder
{
    public static OafProjectScaffoldResult Create(string? targetPath, bool force = false)
    {
        var projectPath = ResolveProjectPath(targetPath);
        var created = new List<string>();

        try
        {
            EnsureProjectDirectory(projectPath, force);

            var projectName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "oaf-app";
            }

            WriteFile(Path.Combine(projectPath, "main.oaf"), BuildMainFileContent(projectName), force, created);
            WriteFile(Path.Combine(projectPath, "tests", "smoke.oaf"), BuildSmokeTestContent(), force, created);
            WriteFile(Path.Combine(projectPath, "examples", "hello.oaf"), BuildExampleContent(), force, created);
            WriteFile(Path.Combine(projectPath, ".gitignore"), BuildGitIgnoreContent(), force, created);
            WriteFile(Path.Combine(projectPath, "README.md"), BuildReadmeContent(projectName), force, created);

            var manifestPath = Path.Combine(projectPath, OafPackageManager.DefaultManifestFileName);
            if (force || !File.Exists(manifestPath))
            {
                if (!OafPackageManager.InitManifest(manifestPath, out var manifestMessage))
                {
                    return new OafProjectScaffoldResult(false, projectPath, created, manifestMessage);
                }

                if (force || !created.Contains(manifestPath, StringComparer.Ordinal))
                {
                    created.Add(manifestPath);
                }
            }

            return new OafProjectScaffoldResult(
                true,
                projectPath,
                created,
                $"Created Oaf project scaffold at '{projectPath}'.");
        }
        catch (Exception ex)
        {
            return new OafProjectScaffoldResult(false, projectPath, created, ex.Message);
        }
    }

    private static string ResolveProjectPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(targetPath);
    }

    private static void EnsureProjectDirectory(string projectPath, bool force)
    {
        if (!Directory.Exists(projectPath))
        {
            Directory.CreateDirectory(projectPath);
            return;
        }

        if (force)
        {
            return;
        }

        var hasExistingContent = Directory.EnumerateFileSystemEntries(projectPath).Any();
        if (hasExistingContent)
        {
            throw new InvalidOperationException(
                $"Target directory '{projectPath}' is not empty. Re-run with --force to overwrite scaffold files.");
        }
    }

    private static void WriteFile(string filePath, string content, bool force, List<string> created)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!force && File.Exists(filePath))
        {
            return;
        }

        File.WriteAllText(filePath, content);
        if (!created.Contains(filePath, StringComparer.Ordinal))
        {
            created.Add(filePath);
        }
    }

    private static string BuildMainFileContent(string projectName)
    {
        return $$"""
flux iterations = 5;
flux total = 0;
loop iterations > 0 => {
    total += iterations;
    iterations -= 1;
}
return total; // {{projectName}}
""";
    }

    private static string BuildSmokeTestContent()
    {
        return """
flux value = 40 + 2;
return value;
""";
    }

    private static string BuildExampleContent()
    {
        return """
flux i = 1;
flux acc = 0;
loop i <= 10 => {
    acc += i;
    i += 1;
}
return acc;
""";
    }

    private static string BuildGitIgnoreContent()
    {
        return """
.oaf/build/
.oaf/publish/
""";
    }

    private static string BuildReadmeContent(string projectName)
    {
        return $$"""
# {{projectName}}

## Commands

```bash
oaf run
oaf build main.oaf
oaf publish main.oaf
oaf test
```
""";
    }
}
