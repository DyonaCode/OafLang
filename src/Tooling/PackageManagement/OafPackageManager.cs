using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.Parser;

namespace Oaf.Tooling.PackageManagement;

public readonly record struct OafPackageDependency(string Name, string Version)
{
    public override string ToString()
    {
        return $"{Name}@{Version}";
    }
}

public readonly record struct OafPackageRequirement(string Name, string VersionSelector)
{
    public override string ToString()
    {
        return $"{Name}@{VersionSelector}";
    }
}

public static class OafPackageManager
{
    public const string DefaultManifestFileName = "packages.txt";
    public const string DefaultLockFileName = "packages.lock";
    public const string DefaultSourcesFileName = "packages.sources";

    private const string ManifestSourceName = "manifest";
    private const string LockFormatVersion = "2";
    private const string NoArtifactHash = "none";
    private const string NoArtifactFile = "none";
    private static readonly JsonSerializerOptions SourceIndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        if (!TryParseDependencyRequirementSpec(dependencySpec, out var requirement, out _))
        {
            message = $"Invalid dependency spec '{dependencySpec}'. Expected name@version or name@range.";
            return false;
        }

        var requirements = ReadManifestRequirements(manifestPath, out var readError);
        if (requirements is null)
        {
            message = readError!;
            return false;
        }

        var index = requirements.FindIndex(existing => string.Equals(existing.Name, requirement.Name, StringComparison.Ordinal));
        if (index >= 0)
        {
            requirements[index] = requirement;
        }
        else
        {
            requirements.Add(requirement);
        }

        requirements.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return WriteManifestDependencies(manifestPath, requirements, out message);
    }

    public static bool RemoveDependency(string manifestPath, string packageName, out string message)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            message = "Package name is required.";
            return false;
        }

        var requirements = ReadManifestRequirements(manifestPath, out var readError);
        if (requirements is null)
        {
            message = readError!;
            return false;
        }

        var removed = requirements.RemoveAll(dep => string.Equals(dep.Name, packageName, StringComparison.Ordinal));
        if (removed == 0)
        {
            message = $"Package '{packageName}' is not listed in '{Path.GetFullPath(manifestPath)}'.";
            return false;
        }

        requirements.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return WriteManifestDependencies(manifestPath, requirements, out message);
    }

    public static bool Install(string manifestPath, out string message)
    {
        var requirements = ReadManifestRequirements(manifestPath, out var readError);
        if (requirements is null)
        {
            message = readError!;
            return false;
        }

        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestFullPath) ?? Directory.GetCurrentDirectory();
        var packageRoot = Path.Combine(manifestDirectory, ".oaf", "packages");
        Directory.CreateDirectory(packageRoot);

        var sources = LoadPackageSources(manifestDirectory, out var sourceError);
        if (sources is null)
        {
            message = sourceError!;
            return false;
        }

        var installedPackages = new List<PackageLockEntry>();
        if (sources.Count == 0)
        {
            foreach (var requirement in requirements.OrderBy(static req => req.Name, StringComparer.Ordinal))
            {
                if (!TryGetExactVersionFromSelector(requirement.VersionSelector, out var exactVersion))
                {
                    message = $"Package '{requirement.Name}' uses range selector '{requirement.VersionSelector}' but no package sources are configured. Add a sources index or pin an exact version.";
                    return false;
                }

                var dependency = new OafPackageDependency(requirement.Name, exactVersion);
                var packageDirectory = Path.Combine(packageRoot, dependency.Name, dependency.Version);

                if (!InstallManifestPlaceholder(dependency, packageDirectory, out var placeholderEntry, out var placeholderError))
                {
                    message = placeholderError!;
                    return false;
                }

                installedPackages.Add(placeholderEntry);
            }
        }
        else
        {
            if (!TryResolveDependencyGraph(requirements, sources, out var resolvedPackages, out var resolveError))
            {
                message = resolveError!;
                return false;
            }

            foreach (var resolvedPackage in resolvedPackages.OrderBy(static pkg => pkg.Name, StringComparer.Ordinal))
            {
                var dependency = new OafPackageDependency(resolvedPackage.Name, resolvedPackage.Version);
                var packageDirectory = Path.Combine(packageRoot, dependency.Name, dependency.Version);

                if (!InstallArtifactPackage(dependency, resolvedPackage, packageDirectory, out var lockEntry, out var installError))
                {
                    message = installError!;
                    return false;
                }

                installedPackages.Add(lockEntry);
            }
        }

        var lockPath = Path.Combine(manifestDirectory, DefaultLockFileName);
        File.WriteAllText(lockPath, BuildLockFileContent(installedPackages));

        message = $"Installed {installedPackages.Count} package(s). Lock file written to '{lockPath}'.";
        return true;
    }

    public static bool VerifyInstall(string manifestPath, out string message)
    {
        var requirements = ReadManifestRequirements(manifestPath, out var readError);
        if (requirements is null)
        {
            message = readError!;
            return false;
        }

        var manifestFullPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestFullPath) ?? Directory.GetCurrentDirectory();
        var packageRoot = Path.Combine(manifestDirectory, ".oaf", "packages");
        var lockPath = Path.Combine(manifestDirectory, DefaultLockFileName);

        if (!File.Exists(lockPath))
        {
            message = $"Lock file '{lockPath}' does not exist. Run --pkg-install first.";
            return false;
        }

        if (!TryParseLockFile(File.ReadAllText(lockPath), out var parsedLock, out var parseError))
        {
            message = $"Lock file '{lockPath}' is malformed: {parseError}";
            return false;
        }

        if (parsedLock.DependencyCount is not null && parsedLock.DependencyCount.Value != parsedLock.Entries.Count)
        {
            message = $"Lock file '{lockPath}' dependency_count does not match entry count.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parsedLock.ManifestSha256))
        {
            var expectedManifestHash = ComputeManifestHash(parsedLock.Entries);
            if (!string.Equals(parsedLock.ManifestSha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Lock file '{lockPath}' manifest hash does not match lock entries.";
                return false;
            }
        }

        var lockEntriesByName = new Dictionary<string, PackageLockEntry>(StringComparer.Ordinal);
        foreach (var entry in parsedLock.Entries)
        {
            if (!lockEntriesByName.TryAdd(entry.Name, entry))
            {
                message = $"Lock file '{lockPath}' contains duplicate entries for package '{entry.Name}'.";
                return false;
            }
        }

        foreach (var requirement in requirements)
        {
            if (!lockEntriesByName.TryGetValue(requirement.Name, out var lockEntry))
            {
                message = $"Lock file '{lockPath}' does not contain package '{requirement.Name}'.";
                return false;
            }

            if (!TryParseVersionSelector(requirement.VersionSelector, out var selector, out var selectorError))
            {
                message = $"Invalid version selector '{requirement.VersionSelector}' for '{requirement.Name}': {selectorError}";
                return false;
            }

            if (!TryParseComparableVersion(lockEntry.Version, out var resolvedVersion))
            {
                message = $"Lock file '{lockPath}' contains unparseable version '{lockEntry.Version}' for '{lockEntry.Name}'.";
                return false;
            }

            if (!IsVersionMatch(resolvedVersion, selector))
            {
                message = $"Lock file '{lockPath}' version '{lockEntry.Version}' does not satisfy selector '{requirement.VersionSelector}' for '{requirement.Name}'.";
                return false;
            }
        }

        foreach (var lockEntry in parsedLock.Entries)
        {
            var dependency = new OafPackageDependency(lockEntry.Name, lockEntry.Version);
            var expectedDependencyHash = ComputeDependencyHash(dependency, lockEntry.Source);
            if (!string.Equals(lockEntry.DependencyHash, expectedDependencyHash, StringComparison.Ordinal))
            {
                message = $"Lock entry hash mismatch for '{lockEntry.Name}@{lockEntry.Version}'.";
                return false;
            }

            var packageDirectory = Path.Combine(packageRoot, lockEntry.Name, lockEntry.Version);
            var metadataPath = Path.Combine(packageDirectory, "package.meta");
            if (!File.Exists(metadataPath))
            {
                message = $"Missing metadata for '{lockEntry.Name}@{lockEntry.Version}' at '{metadataPath}'.";
                return false;
            }

            if (!TryReadMetadata(File.ReadAllLines(metadataPath), out var metadata))
            {
                message = $"Metadata file '{metadataPath}' is malformed.";
                return false;
            }

            if (!metadata.TryGetValue("name", out var metadataName) ||
                !metadata.TryGetValue("version", out var metadataVersion) ||
                !metadata.TryGetValue("source", out var metadataSource) ||
                !metadata.TryGetValue("hash", out var metadataHash))
            {
                message = $"Metadata file '{metadataPath}' is missing required fields.";
                return false;
            }

            var metadataArtifactHash = metadata.TryGetValue("artifact_sha256", out var artifactHashValue)
                ? artifactHashValue
                : NoArtifactHash;
            var metadataArtifactFile = metadata.TryGetValue("artifact_file", out var artifactFileValue)
                ? artifactFileValue
                : NoArtifactFile;

            if (!string.Equals(metadataName, lockEntry.Name, StringComparison.Ordinal) ||
                !string.Equals(metadataVersion, lockEntry.Version, StringComparison.Ordinal) ||
                !string.Equals(metadataSource, lockEntry.Source, StringComparison.Ordinal) ||
                !string.Equals(metadataHash, lockEntry.DependencyHash, StringComparison.Ordinal) ||
                !string.Equals(metadataArtifactHash, lockEntry.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Metadata file '{metadataPath}' failed integrity checks.";
                return false;
            }

            if (string.Equals(lockEntry.ArtifactSha256, NoArtifactHash, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(metadataArtifactFile) ||
                string.Equals(metadataArtifactFile, NoArtifactFile, StringComparison.Ordinal))
            {
                message = $"Metadata file '{metadataPath}' is missing artifact_file.";
                return false;
            }

            var artifactPath = Path.Combine(packageDirectory, metadataArtifactFile);
            if (!File.Exists(artifactPath))
            {
                message = $"Missing artifact for '{lockEntry.Name}@{lockEntry.Version}' at '{artifactPath}'.";
                return false;
            }

            var artifactHash = ComputeFileSha256Hex(artifactPath);
            if (!string.Equals(artifactHash, lockEntry.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Artifact hash mismatch for '{lockEntry.Name}@{lockEntry.Version}'.";
                return false;
            }
        }

        message = $"Verified {parsedLock.Entries.Count} package(s) against lock and metadata.";
        return true;
    }

    public static bool TryComposeCompilationSource(
        string entrySource,
        string searchDirectory,
        out string composedSource,
        out string message)
    {
        var normalizedEntrySource = entrySource ?? string.Empty;
        composedSource = normalizedEntrySource;
        message = string.Empty;

        var startDirectory = string.IsNullOrWhiteSpace(searchDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(searchDirectory);
        var modulesByName = new Dictionary<string, PackageModuleSource>(StringComparer.Ordinal);
        var packageContext = FindPackageContextDirectory(startDirectory);
        if (packageContext is not null)
        {
            var lockPath = Path.Combine(packageContext, DefaultLockFileName);
            if (File.Exists(lockPath))
            {
                ParsedLockFile parsedLock;
                try
                {
                    if (!TryParseLockFile(File.ReadAllText(lockPath), out parsedLock, out var parseError))
                    {
                        message = $"Failed to parse '{lockPath}': {parseError}";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    message = $"Failed to read '{lockPath}': {ex.Message}";
                    return false;
                }

                foreach (var entry in parsedLock.Entries.OrderBy(static item => item.Name, StringComparer.Ordinal))
                {
                    var contentRoot = Path.Combine(packageContext, ".oaf", "packages", entry.Name, entry.Version, "content");
                    if (!Directory.Exists(contentRoot))
                    {
                        continue;
                    }

                    var files = Directory.GetFiles(contentRoot, "*.oaf", SearchOption.AllDirectories)
                        .OrderBy(static path => path, StringComparer.Ordinal);
                    foreach (var file in files)
                    {
                        string source;
                        try
                        {
                            source = File.ReadAllText(file);
                        }
                        catch (Exception ex)
                        {
                            message = $"Failed to read package source '{file}': {ex.Message}";
                            return false;
                        }

                        if (!TryGetExpectedModuleName(contentRoot, file, out var expectedModuleName, out var moduleNameError))
                        {
                            message = $"Invalid package module path '{file}': {moduleNameError}";
                            return false;
                        }

                        if (!TryReadModuleDescriptor(source, expectedModuleName, out var descriptor, out var normalizedSource, out var descriptorError))
                        {
                            message = $"Invalid package module file '{file}': {descriptorError}";
                            return false;
                        }

                        var moduleSource = new PackageModuleSource(descriptor.ModuleName, descriptor.Imports, normalizedSource, file);
                        if (modulesByName.TryGetValue(moduleSource.ModuleName, out var existing))
                        {
                            message = $"Duplicate module '{moduleSource.ModuleName}' found in '{existing.Path}' and '{file}'.";
                            return false;
                        }

                        modulesByName.Add(moduleSource.ModuleName, moduleSource);
                    }
                }
            }
        }

        if (!TryReadEntryImports(normalizedEntrySource, out var requestedImports, out var entryHasModuleDeclaration, out var entryError))
        {
            message = entryError!;
            return false;
        }

        if (requestedImports.Count == 0)
        {
            return true;
        }

        if (!TryLoadLocalModulesFromImports(startDirectory, requestedImports, modulesByName, out var localModuleError))
        {
            message = localModuleError!;
            return false;
        }

        if (modulesByName.Count == 0)
        {
            return true;
        }

        if (!TryResolveImportedModules(requestedImports, modulesByName, out var orderedModules, out var resolveError))
        {
            message = resolveError!;
            return false;
        }

        var builder = new StringBuilder();
        builder.AppendLine("// package prelude modules");
        foreach (var moduleName in orderedModules)
        {
            builder.AppendLine(modulesByName[moduleName].Source);
        }

        if (!string.IsNullOrWhiteSpace(normalizedEntrySource))
        {
            if (!entryHasModuleDeclaration)
            {
                builder.AppendLine("module entry.main;");
            }

            builder.AppendLine(normalizedEntrySource);
        }

        composedSource = builder.ToString();
        return true;
    }

    private static bool TryReadEntryImports(
        string source,
        out List<string> imports,
        out bool hasModuleDeclaration,
        out string? error)
    {
        imports = [];
        hasModuleDeclaration = false;
        error = null;

        try
        {
            var parser = new Parser(source);
            var unit = parser.ParseCompilationUnit();
            hasModuleDeclaration = unit.Statements.OfType<ModuleDeclarationStatementSyntax>().Any();
            imports = unit.Statements
                .OfType<ImportStatementSyntax>()
                .Select(static statement => statement.ModuleName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse imports from entry source: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadModuleDescriptor(
        string source,
        string expectedModuleName,
        out PackageModuleDescriptor descriptor,
        out string normalizedSource,
        out string? error)
    {
        descriptor = default;
        normalizedSource = string.Empty;
        error = null;

        try
        {
            var parser = new Parser(source);
            var unit = parser.ParseCompilationUnit();

            var moduleDeclaration = unit.Statements.OfType<ModuleDeclarationStatementSyntax>().FirstOrDefault();
            if (moduleDeclaration is null)
            {
                var inferredImports = unit.Statements
                    .OfType<ImportStatementSyntax>()
                    .Select(static statement => statement.ModuleName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToList();

                descriptor = new PackageModuleDescriptor(expectedModuleName, inferredImports);
                normalizedSource = $"module {expectedModuleName};{Environment.NewLine}{source}";
                return true;
            }

            if (!string.Equals(moduleDeclaration.ModuleName, expectedModuleName, StringComparison.Ordinal))
            {
                error = $"module declaration '{moduleDeclaration.ModuleName}' must match file path module '{expectedModuleName}'.";
                return false;
            }

            var imports = unit.Statements
                .OfType<ImportStatementSyntax>()
                .Select(static statement => statement.ModuleName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();

            descriptor = new PackageModuleDescriptor(moduleDeclaration.ModuleName, imports);
            normalizedSource = source;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse module source: {ex.Message}";
            return false;
        }
    }

    private static bool TryLoadLocalModulesFromImports(
        string startDirectory,
        IReadOnlyList<string> requestedImports,
        Dictionary<string, PackageModuleSource> modulesByName,
        out string? error)
    {
        error = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(requestedImports
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal));

        while (queue.Count > 0)
        {
            var moduleName = queue.Dequeue();
            if (!visited.Add(moduleName))
            {
                continue;
            }

            if (!TryGetLocalModuleFilePath(startDirectory, moduleName, out var localModulePath))
            {
                continue;
            }

            string source;
            try
            {
                source = File.ReadAllText(localModulePath);
            }
            catch (Exception ex)
            {
                error = $"Failed to read local module source '{localModulePath}': {ex.Message}";
                return false;
            }

            if (!TryReadModuleDescriptor(source, moduleName, out var descriptor, out var normalizedSource, out var descriptorError))
            {
                error = $"Invalid local module file '{localModulePath}': {descriptorError}";
                return false;
            }

            modulesByName[moduleName] = new PackageModuleSource(
                descriptor.ModuleName,
                descriptor.Imports,
                normalizedSource,
                localModulePath);

            foreach (var import in descriptor.Imports.OrderBy(static name => name, StringComparer.Ordinal))
            {
                if (!visited.Contains(import))
                {
                    queue.Enqueue(import);
                }
            }
        }

        return true;
    }

    private static bool TryGetLocalModuleFilePath(string startDirectory, string moduleName, out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return false;
        }

        var segments = moduleName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !segments.All(IsValidModuleIdentifierSegment))
        {
            return false;
        }

        var relativePath = Path.Combine(segments) + ".oaf";
        var candidatePath = Path.GetFullPath(Path.Combine(startDirectory, relativePath));
        if (!File.Exists(candidatePath))
        {
            return false;
        }

        filePath = candidatePath;
        return true;
    }

    private static bool TryGetExpectedModuleName(
        string contentRoot,
        string filePath,
        out string moduleName,
        out string? error)
    {
        moduleName = string.Empty;
        error = null;

        var relativePath = Path.GetRelativePath(contentRoot, filePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "relative module path is empty.";
            return false;
        }

        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            error = $"module file '{filePath}' must be under package content root '{contentRoot}'.";
            return false;
        }

        if (!relativePath.EndsWith(".oaf", StringComparison.OrdinalIgnoreCase))
        {
            error = "module file must use the .oaf extension.";
            return false;
        }

        var pathWithoutExtension = Path.ChangeExtension(relativePath, null);
        if (string.IsNullOrWhiteSpace(pathWithoutExtension))
        {
            error = "module file path is missing a module name.";
            return false;
        }

        var pathSegments = pathWithoutExtension.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
        {
            error = "module file path is missing identifier segments.";
            return false;
        }

        foreach (var segment in pathSegments)
        {
            if (!IsValidModuleIdentifierSegment(segment))
            {
                error = $"path segment '{segment}' is not a valid module identifier; use [A-Za-z_][A-Za-z0-9_]*.";
                return false;
            }
        }

        moduleName = string.Join('.', pathSegments);
        return true;
    }

    private static bool IsValidModuleIdentifierSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var ch = value[index];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveImportedModules(
        IReadOnlyList<string> requestedImports,
        IReadOnlyDictionary<string, PackageModuleSource> modulesByName,
        out List<string> orderedModules,
        out string? error)
    {
        orderedModules = [];
        error = null;

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var moduleName in requestedImports.OrderBy(static value => value, StringComparer.Ordinal))
        {
            if (!VisitModule(moduleName, modulesByName, state, orderedModules, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VisitModule(
        string moduleName,
        IReadOnlyDictionary<string, PackageModuleSource> modulesByName,
        Dictionary<string, int> state,
        List<string> orderedModules,
        out string? error)
    {
        error = null;
        if (state.TryGetValue(moduleName, out var existingState))
        {
            if (existingState == 2)
            {
                return true;
            }

            if (existingState == 1)
            {
                error = $"Cyclic package module import detected at '{moduleName}'.";
                return false;
            }
        }

        if (!modulesByName.TryGetValue(moduleName, out var module))
        {
            error = $"Requested package module '{moduleName}' was not found in installed package content.";
            return false;
        }

        state[moduleName] = 1;
        foreach (var dependency in module.Imports.OrderBy(static value => value, StringComparer.Ordinal))
        {
            if (!VisitModule(dependency, modulesByName, state, orderedModules, out error))
            {
                return false;
            }
        }

        state[moduleName] = 2;
        orderedModules.Add(moduleName);
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

    private static bool TryParseDependencyRequirementSpec(
        string spec,
        out OafPackageRequirement requirement,
        out string? error)
    {
        requirement = default;
        error = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "dependency spec is empty";
            return false;
        }

        var atIndex = spec.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= spec.Length - 1)
        {
            error = "expected name@version-selector";
            return false;
        }

        var name = spec[..atIndex].Trim();
        var selectorRaw = spec[(atIndex + 1)..].Trim();
        if (!IsValidDependencyPart(name))
        {
            error = $"invalid package name '{name}'";
            return false;
        }

        if (!TryParseVersionSelector(selectorRaw, out _, out var selectorError))
        {
            error = selectorError;
            return false;
        }

        requirement = new OafPackageRequirement(name, selectorRaw);
        return true;
    }

    private static bool TryGetExactVersionFromSelector(string selector, out string exactVersion)
    {
        exactVersion = string.Empty;
        if (!IsExactVersionSelector(selector))
        {
            return false;
        }

        exactVersion = selector.Trim();
        return TryParseComparableVersion(exactVersion, out _);
    }

    private static bool IsExactVersionSelector(string selector)
    {
        var trimmed = selector.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.IndexOfAny(['*', '^', '~', '>', '<', '=', ',', ' ']) >= 0)
        {
            return false;
        }

        return IsValidDependencyPart(trimmed);
    }

    private static bool TryParseVersionSelector(
        string selectorRaw,
        out VersionSelector selector,
        out string? error)
    {
        selector = default;
        error = null;

        var selectorText = selectorRaw.Trim();
        if (selectorText.Length == 0)
        {
            error = "selector is empty";
            return false;
        }

        if (string.Equals(selectorText, "*", StringComparison.Ordinal))
        {
            selector = new VersionSelector(true, []);
            return true;
        }

        if (selectorText.StartsWith("^", StringComparison.Ordinal))
        {
            var baseVersionRaw = selectorText[1..].Trim();
            if (!TryParseComparableVersion(baseVersionRaw, out var baseVersion))
            {
                error = $"invalid caret selector '{selectorRaw}'";
                return false;
            }

            var upperBound = ComputeCaretUpperBound(baseVersion);
            selector = new VersionSelector(
                false,
                [
                    new VersionComparator(VersionComparisonOperator.GreaterThanOrEqual, baseVersion),
                    new VersionComparator(VersionComparisonOperator.LessThan, upperBound)
                ]);
            return true;
        }

        if (selectorText.StartsWith("~", StringComparison.Ordinal))
        {
            var baseVersionRaw = selectorText[1..].Trim();
            if (!TryParseComparableVersion(baseVersionRaw, out var baseVersion))
            {
                error = $"invalid tilde selector '{selectorRaw}'";
                return false;
            }

            var upperBound = ComputeTildeUpperBound(baseVersion);
            selector = new VersionSelector(
                false,
                [
                    new VersionComparator(VersionComparisonOperator.GreaterThanOrEqual, baseVersion),
                    new VersionComparator(VersionComparisonOperator.LessThan, upperBound)
                ]);
            return true;
        }

        if (selectorText.Contains('*', StringComparison.Ordinal))
        {
            if (!TryParseWildcardSelector(selectorText, out selector, out error))
            {
                return false;
            }

            return true;
        }

        if (selectorText.Contains(' ', StringComparison.Ordinal) ||
            selectorText.Contains(',', StringComparison.Ordinal) ||
            StartsWithComparator(selectorText))
        {
            var comparators = new List<VersionComparator>();
            var tokens = selectorText
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (!TryParseComparatorToken(token, out var comparator, out var comparatorError))
                {
                    error = comparatorError;
                    return false;
                }

                comparators.Add(comparator);
            }

            if (comparators.Count == 0)
            {
                error = $"invalid selector '{selectorRaw}'";
                return false;
            }

            selector = new VersionSelector(false, comparators);
            return true;
        }

        if (!TryParseComparableVersion(selectorText, out var exactVersion))
        {
            error = $"invalid version selector '{selectorRaw}'";
            return false;
        }

        selector = new VersionSelector(
            false,
            [new VersionComparator(VersionComparisonOperator.Equal, exactVersion)]);
        return true;
    }

    private static bool StartsWithComparator(string token)
    {
        return token.StartsWith(">=", StringComparison.Ordinal) ||
               token.StartsWith("<=", StringComparison.Ordinal) ||
               token.StartsWith("==", StringComparison.Ordinal) ||
               token.StartsWith(">", StringComparison.Ordinal) ||
               token.StartsWith("<", StringComparison.Ordinal) ||
               token.StartsWith("=", StringComparison.Ordinal);
    }

    private static bool TryParseWildcardSelector(
        string selectorRaw,
        out VersionSelector selector,
        out string? error)
    {
        selector = default;
        error = null;

        if (!selectorRaw.EndsWith(".*", StringComparison.Ordinal))
        {
            error = $"wildcard selector '{selectorRaw}' must end with .*";
            return false;
        }

        var prefix = selectorRaw[..^2];
        if (prefix.Length == 0)
        {
            selector = new VersionSelector(true, []);
            return true;
        }

        var parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numericParts = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var parsed) || parsed < 0)
            {
                error = $"invalid wildcard selector '{selectorRaw}'";
                return false;
            }

            numericParts.Add(parsed);
        }

        var lowerParts = new List<int>(numericParts);
        while (lowerParts.Count < 3)
        {
            lowerParts.Add(0);
        }

        var upperParts = new List<int>(numericParts);
        var lastIndex = upperParts.Count - 1;
        upperParts[lastIndex]++;
        while (upperParts.Count < 3)
        {
            upperParts.Add(0);
        }

        var lower = new ComparableVersion(lowerParts, null);
        var upper = new ComparableVersion(upperParts, null);
        selector = new VersionSelector(
            false,
            [
                new VersionComparator(VersionComparisonOperator.GreaterThanOrEqual, lower),
                new VersionComparator(VersionComparisonOperator.LessThan, upper)
            ]);
        return true;
    }

    private static bool TryParseComparatorToken(
        string token,
        out VersionComparator comparator,
        out string? error)
    {
        comparator = default;
        error = null;

        string opText;
        string versionText;
        if (token.StartsWith(">=", StringComparison.Ordinal) ||
            token.StartsWith("<=", StringComparison.Ordinal) ||
            token.StartsWith("==", StringComparison.Ordinal))
        {
            opText = token[..2];
            versionText = token[2..];
        }
        else if (token.StartsWith(">", StringComparison.Ordinal) ||
                 token.StartsWith("<", StringComparison.Ordinal) ||
                 token.StartsWith("=", StringComparison.Ordinal))
        {
            opText = token[..1];
            versionText = token[1..];
        }
        else
        {
            error = $"comparator token '{token}' must start with one of >, >=, <, <=, =";
            return false;
        }

        versionText = versionText.Trim();
        if (!TryParseComparableVersion(versionText, out var version))
        {
            error = $"invalid version '{versionText}' in comparator '{token}'";
            return false;
        }

        var op = opText switch
        {
            ">" => VersionComparisonOperator.GreaterThan,
            ">=" => VersionComparisonOperator.GreaterThanOrEqual,
            "<" => VersionComparisonOperator.LessThan,
            "<=" => VersionComparisonOperator.LessThanOrEqual,
            "=" => VersionComparisonOperator.Equal,
            "==" => VersionComparisonOperator.Equal,
            _ => throw new InvalidOperationException($"Unsupported comparator '{opText}'.")
        };
        comparator = new VersionComparator(op, version);
        return true;
    }

    private static ComparableVersion ComputeCaretUpperBound(ComparableVersion version)
    {
        var major = GetVersionPart(version, 0);
        var minor = GetVersionPart(version, 1);
        var patch = GetVersionPart(version, 2);

        if (major > 0)
        {
            return new ComparableVersion([major + 1, 0, 0], null);
        }

        if (minor > 0)
        {
            return new ComparableVersion([0, minor + 1, 0], null);
        }

        return new ComparableVersion([0, 0, patch + 1], null);
    }

    private static ComparableVersion ComputeTildeUpperBound(ComparableVersion version)
    {
        var major = GetVersionPart(version, 0);
        var minor = GetVersionPart(version, 1);
        return new ComparableVersion([major, minor + 1, 0], null);
    }

    private static int GetVersionPart(ComparableVersion version, int index)
    {
        return index < version.NumericParts.Count ? version.NumericParts[index] : 0;
    }

    private static bool TryParseComparableVersion(string raw, out ComparableVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        var plusIndex = text.IndexOf('+');
        if (plusIndex >= 0)
        {
            text = text[..plusIndex];
        }

        string? preRelease = null;
        var dashIndex = text.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = text[(dashIndex + 1)..];
            text = text[..dashIndex];
        }

        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var numericParts = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var parsed) || parsed < 0)
            {
                return false;
            }

            numericParts.Add(parsed);
        }

        version = new ComparableVersion(numericParts, string.IsNullOrWhiteSpace(preRelease) ? null : preRelease.Trim());
        return true;
    }

    private static bool IsVersionMatch(ComparableVersion version, VersionSelector selector)
    {
        if (selector.MatchAny)
        {
            return true;
        }

        foreach (var comparator in selector.Comparators)
        {
            var cmp = version.CompareTo(comparator.Version);
            var isMatch = comparator.Operator switch
            {
                VersionComparisonOperator.Equal => cmp == 0,
                VersionComparisonOperator.GreaterThan => cmp > 0,
                VersionComparisonOperator.GreaterThanOrEqual => cmp >= 0,
                VersionComparisonOperator.LessThan => cmp < 0,
                VersionComparisonOperator.LessThanOrEqual => cmp <= 0,
                _ => false
            };

            if (!isMatch)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InstallManifestPlaceholder(
        OafPackageDependency dependency,
        string packageDirectory,
        out PackageLockEntry lockEntry,
        out string? error)
    {
        lockEntry = default;
        error = null;

        try
        {
            ResetDirectory(packageDirectory);

            var dependencyHash = ComputeDependencyHash(dependency, ManifestSourceName);
            WritePackageMetadata(
                packageDirectory,
                dependency.Name,
                dependency.Version,
                ManifestSourceName,
                dependencyHash,
                NoArtifactHash,
                NoArtifactFile);

            lockEntry = new PackageLockEntry(
                dependency.Name,
                dependency.Version,
                ManifestSourceName,
                dependencyHash,
                NoArtifactHash);

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to install package '{dependency}': {ex.Message}";
            return false;
        }
    }

    private static bool InstallArtifactPackage(
        OafPackageDependency dependency,
        PackageSourcePackage resolvedPackage,
        string packageDirectory,
        out PackageLockEntry lockEntry,
        out string? error)
    {
        lockEntry = default;
        error = null;

        try
        {
            if (!File.Exists(resolvedPackage.ArtifactPath))
            {
                error = $"Package artifact '{resolvedPackage.ArtifactPath}' does not exist.";
                return false;
            }

            var artifactFileName = Path.GetFileName(resolvedPackage.ArtifactPath);
            if (string.IsNullOrWhiteSpace(artifactFileName))
            {
                error = $"Unable to resolve artifact file name for '{dependency}'.";
                return false;
            }

            if (!IsSupportedZipArtifact(artifactFileName))
            {
                error = $"Unsupported package artifact '{artifactFileName}'. Supported formats: .zip, .nupkg, .oafpkg.";
                return false;
            }

            ResetDirectory(packageDirectory);

            var artifactDestination = Path.Combine(packageDirectory, artifactFileName);
            File.Copy(resolvedPackage.ArtifactPath, artifactDestination, overwrite: true);

            var artifactHash = ComputeFileSha256Hex(artifactDestination);
            if (!string.Equals(artifactHash, resolvedPackage.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Artifact hash mismatch for '{dependency}'.";
                return false;
            }

            var contentDirectory = Path.Combine(packageDirectory, "content");
            Directory.CreateDirectory(contentDirectory);
            ZipFile.ExtractToDirectory(artifactDestination, contentDirectory, overwriteFiles: true);

            var dependencyHash = ComputeDependencyHash(dependency, resolvedPackage.SourceName);
            WritePackageMetadata(
                packageDirectory,
                dependency.Name,
                dependency.Version,
                resolvedPackage.SourceName,
                dependencyHash,
                artifactHash,
                artifactFileName);

            lockEntry = new PackageLockEntry(
                dependency.Name,
                dependency.Version,
                resolvedPackage.SourceName,
                dependencyHash,
                artifactHash);

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to install package '{dependency}': {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveDependencyGraph(
        IReadOnlyList<OafPackageRequirement> rootRequirements,
        IReadOnlyList<PackageSourceIndex> sources,
        out List<PackageSourcePackage> resolvedPackages,
        out string? error)
    {
        resolvedPackages = [];
        error = null;

        var catalogByName = new Dictionary<string, List<PackageSourcePackage>>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var package in source.Packages)
            {
                if (!catalogByName.TryGetValue(package.Name, out var versions))
                {
                    versions = [];
                    catalogByName.Add(package.Name, versions);
                }

                var existing = versions.FirstOrDefault(existingPackage =>
                    string.Equals(existingPackage.Version, package.Version, StringComparison.Ordinal));
                if (existing.Name is not null)
                {
                    if (!string.Equals(existing.ArtifactSha256, package.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.ArtifactPath, package.ArtifactPath, StringComparison.Ordinal))
                    {
                        error = $"Conflicting source entries detected for '{package.Name}@{package.Version}'.";
                        return false;
                    }

                    continue;
                }

                versions.Add(package);
            }
        }

        foreach (var entry in catalogByName)
        {
            entry.Value.Sort(static (left, right) => right.ParsedVersion.CompareTo(left.ParsedVersion));
        }

        var constraints = new Dictionary<string, List<ResolutionConstraint>>(StringComparer.Ordinal);
        var requiredPackages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var requirement in rootRequirements)
        {
            if (!TryParseVersionSelector(requirement.VersionSelector, out var selector, out var selectorError))
            {
                error = $"Invalid selector '{requirement.VersionSelector}' for '{requirement.Name}': {selectorError}";
                return false;
            }

            AddConstraint(constraints, requirement.Name, new ResolutionConstraint(selector, requirement.VersionSelector, "manifest"));
            requiredPackages.Add(requirement.Name);
        }

        var chosen = new Dictionary<string, PackageSourcePackage>(StringComparer.Ordinal);
        if (!TryResolveRecursive(requiredPackages, constraints, catalogByName, chosen, out error))
        {
            return false;
        }

        resolvedPackages = chosen.Values
            .OrderBy(static package => package.Name, StringComparer.Ordinal)
            .ThenBy(static package => package.Version, StringComparer.Ordinal)
            .ToList();
        return true;
    }

    private static bool TryResolveRecursive(
        HashSet<string> requiredPackages,
        Dictionary<string, List<ResolutionConstraint>> constraints,
        Dictionary<string, List<PackageSourcePackage>> catalogByName,
        Dictionary<string, PackageSourcePackage> chosen,
        out string? error)
    {
        error = null;

        string? nextPackageName = null;
        List<PackageSourcePackage>? nextCandidates = null;

        foreach (var packageName in requiredPackages.Where(name => !chosen.ContainsKey(name)))
        {
            if (!catalogByName.TryGetValue(packageName, out var available))
            {
                error = $"Unable to resolve package '{packageName}': no versions were found in configured package sources.";
                return false;
            }

            constraints.TryGetValue(packageName, out var packageConstraints);
            packageConstraints ??= [];
            var matchingCandidates = available
                .Where(candidate => packageConstraints.All(constraint => IsVersionMatch(candidate.ParsedVersion, constraint.Selector)))
                .OrderByDescending(static candidate => candidate.ParsedVersion)
                .ToList();

            if (matchingCandidates.Count == 0)
            {
                var reasons = packageConstraints.Count == 0
                    ? "no matching versions"
                    : string.Join(
                        "; ",
                        packageConstraints.Select(static constraint => $"{constraint.Origin} requires '{constraint.RawSelector}'"));
                error = $"Unable to resolve package '{packageName}': {reasons}.";
                return false;
            }

            if (nextPackageName is null || matchingCandidates.Count < nextCandidates!.Count)
            {
                nextPackageName = packageName;
                nextCandidates = matchingCandidates;
            }
        }

        if (nextPackageName is null)
        {
            return true;
        }

        foreach (var candidate in nextCandidates!)
        {
            var clonedRequired = new HashSet<string>(requiredPackages, StringComparer.Ordinal);
            var clonedConstraints = CloneConstraints(constraints);
            var clonedChosen = new Dictionary<string, PackageSourcePackage>(chosen, StringComparer.Ordinal)
            {
                [nextPackageName] = candidate
            };

            var invalid = false;
            foreach (var dependency in candidate.Dependencies)
            {
                if (!TryParseVersionSelector(dependency.VersionSelector, out var selector, out var selectorError))
                {
                    error = $"Invalid transitive selector '{dependency.VersionSelector}' in '{candidate.Name}@{candidate.Version}': {selectorError}";
                    return false;
                }

                AddConstraint(
                    clonedConstraints,
                    dependency.Name,
                    new ResolutionConstraint(selector, dependency.VersionSelector, $"{candidate.Name}@{candidate.Version}"));
                clonedRequired.Add(dependency.Name);

                if (!clonedChosen.TryGetValue(dependency.Name, out var chosenDependency))
                {
                    continue;
                }

                var dependencyConstraints = clonedConstraints[dependency.Name];
                if (dependencyConstraints.All(constraint => IsVersionMatch(chosenDependency.ParsedVersion, constraint.Selector)))
                {
                    continue;
                }

                invalid = true;
                break;
            }

            if (invalid)
            {
                continue;
            }

            if (TryResolveRecursive(clonedRequired, clonedConstraints, catalogByName, clonedChosen, out error))
            {
                chosen.Clear();
                foreach (var resolved in clonedChosen)
                {
                    chosen.Add(resolved.Key, resolved.Value);
                }

                return true;
            }
        }

        error ??= $"Unable to resolve package '{nextPackageName}' due to conflicting constraints.";
        return false;
    }

    private static Dictionary<string, List<ResolutionConstraint>> CloneConstraints(
        Dictionary<string, List<ResolutionConstraint>> constraints)
    {
        var clone = new Dictionary<string, List<ResolutionConstraint>>(constraints.Count, StringComparer.Ordinal);
        foreach (var entry in constraints)
        {
            clone.Add(entry.Key, [..entry.Value]);
        }

        return clone;
    }

    private static void AddConstraint(
        Dictionary<string, List<ResolutionConstraint>> constraints,
        string packageName,
        ResolutionConstraint constraint)
    {
        if (!constraints.TryGetValue(packageName, out var list))
        {
            list = [];
            constraints.Add(packageName, list);
        }

        list.Add(constraint);
    }

    private static List<PackageSourceIndex>? LoadPackageSources(string manifestDirectory, out string? error)
    {
        error = null;
        var sources = new List<PackageSourceIndex>();
        var sourceReferences = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable("OAF_PACKAGE_INDEX");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            sourceReferences.Add(fromEnvironment.Trim());
        }

        var sourcesFilePath = Path.Combine(manifestDirectory, DefaultSourcesFileName);
        if (File.Exists(sourcesFilePath))
        {
            foreach (var line in File.ReadAllLines(sourcesFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                sourceReferences.Add(trimmed);
            }
        }

        if (sourceReferences.Count == 0)
        {
            return sources;
        }

        var seenSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceReference in sourceReferences)
        {
            var resolvedSourcePath = ResolvePath(sourceReference, manifestDirectory);
            if (!seenSourcePaths.Add(resolvedSourcePath))
            {
                continue;
            }

            if (!File.Exists(resolvedSourcePath))
            {
                error = $"Package source index '{resolvedSourcePath}' does not exist.";
                return null;
            }

            var source = LoadPackageSourceIndex(resolvedSourcePath, out var loadError);
            if (source is null)
            {
                error = loadError!;
                return null;
            }

            sources.Add(source.Value);
        }

        return sources;
    }

    private static PackageSourceIndex? LoadPackageSourceIndex(string indexPath, out string? error)
    {
        error = null;
        try
        {
            var json = File.ReadAllText(indexPath);
            var document = JsonSerializer.Deserialize<PackageSourceIndexDocument>(json, SourceIndexJsonOptions);
            if (document?.Packages is null)
            {
                error = $"Package source index '{indexPath}' is missing packages.";
                return null;
            }

            var sourceName = string.IsNullOrWhiteSpace(document.Source)
                ? Path.GetFileNameWithoutExtension(indexPath)
                : document.Source.Trim();
            if (string.IsNullOrWhiteSpace(sourceName) || !IsValidSourceName(sourceName))
            {
                error = $"Package source '{indexPath}' has an invalid source name '{sourceName}'.";
                return null;
            }

            var indexDirectory = Path.GetDirectoryName(indexPath) ?? Directory.GetCurrentDirectory();
            var packages = new List<PackageSourcePackage>();
            foreach (var package in document.Packages)
            {
                if (package is null ||
                    string.IsNullOrWhiteSpace(package.Name) ||
                    string.IsNullOrWhiteSpace(package.Version) ||
                    string.IsNullOrWhiteSpace(package.Artifact) ||
                    string.IsNullOrWhiteSpace(package.Sha256))
                {
                    error = $"Package source index '{indexPath}' contains an incomplete package entry.";
                    return null;
                }

                if (!TryParseDependencySpec($"{package.Name}@{package.Version}", out var dependency))
                {
                    error = $"Package source index '{indexPath}' contains invalid dependency '{package.Name}@{package.Version}'.";
                    return null;
                }

                if (!TryParseComparableVersion(dependency.Version, out var comparableVersion))
                {
                    error = $"Package source index '{indexPath}' contains invalid semantic version '{dependency.Version}' for '{dependency.Name}'.";
                    return null;
                }

                var sha = package.Sha256.Trim().ToLowerInvariant();
                if (!IsValidSha256(sha))
                {
                    error = $"Package source index '{indexPath}' contains invalid sha256 for '{dependency}'.";
                    return null;
                }

                var transitiveDependencies = new List<OafPackageRequirement>();
                if (package.Dependencies is not null)
                {
                    foreach (var dependencySpec in package.Dependencies)
                    {
                        if (string.IsNullOrWhiteSpace(dependencySpec))
                        {
                            error = $"Package source index '{indexPath}' contains an empty dependency in '{dependency}'.";
                            return null;
                        }

                        if (!TryParseDependencyRequirementSpec(dependencySpec, out var transitiveDependency, out var transitiveError))
                        {
                            error = $"Package source index '{indexPath}' contains invalid dependency '{dependencySpec}' for '{dependency}': {transitiveError}";
                            return null;
                        }

                        transitiveDependencies.Add(transitiveDependency);
                    }
                }

                var artifactPath = ResolvePath(package.Artifact.Trim(), indexDirectory);
                packages.Add(new PackageSourcePackage(
                    dependency.Name,
                    dependency.Version,
                    comparableVersion,
                    sourceName,
                    artifactPath,
                    sha,
                    transitiveDependencies));
            }

            return new PackageSourceIndex(sourceName, indexPath, packages);
        }
        catch (JsonException ex)
        {
            error = $"Package source index '{indexPath}' contains invalid JSON: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            error = $"Failed to load package source index '{indexPath}': {ex.Message}";
            return null;
        }
    }

    private static void ResetDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        Directory.CreateDirectory(directoryPath);
    }

    private static void WritePackageMetadata(
        string packageDirectory,
        string name,
        string version,
        string source,
        string dependencyHash,
        string artifactHash,
        string artifactFileName)
    {
        var metadata = new StringBuilder();
        metadata.AppendLine($"name={name}");
        metadata.AppendLine($"version={version}");
        metadata.AppendLine($"source={source}");
        metadata.AppendLine($"hash={dependencyHash}");
        metadata.AppendLine($"artifact_sha256={artifactHash}");
        metadata.AppendLine($"artifact_file={artifactFileName}");

        File.WriteAllText(Path.Combine(packageDirectory, "package.meta"), metadata.ToString());
    }

    private static bool IsSupportedZipArtifact(string artifactFileName)
    {
        return artifactFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               artifactFileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
               artifactFileName.EndsWith(".oafpkg", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string pathValue, string baseDirectory)
    {
        return Path.GetFullPath(
            Path.IsPathRooted(pathValue)
                ? pathValue
                : Path.Combine(baseDirectory, pathValue));
    }

    private static bool IsValidSourceName(string value)
    {
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

    private static bool IsValidSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindPackageContextDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, DefaultLockFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static List<OafPackageRequirement>? ReadManifestRequirements(string manifestPath, out string? error)
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

        var dependencies = new List<OafPackageRequirement>();
        foreach (var line in File.ReadAllLines(fullPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (!TryParseDependencyRequirementSpec(trimmed, out var dependency, out var parseError))
            {
                error = $"Invalid dependency entry '{trimmed}' in '{fullPath}': {parseError}";
                return null;
            }

            dependencies.RemoveAll(existing => string.Equals(existing.Name, dependency.Name, StringComparison.Ordinal));
            dependencies.Add(dependency);
        }

        return dependencies;
    }

    private static bool WriteManifestDependencies(string manifestPath, IReadOnlyList<OafPackageRequirement> dependencies, out string message)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var lines = new List<string>
        {
            "# Oaf package manifest",
            "# One dependency per line in the format name@version-or-range"
        };

        lines.AddRange(dependencies.Select(static dep => dep.ToString()));
        File.WriteAllLines(fullPath, lines);

        message = $"Manifest updated at '{fullPath}'.";
        return true;
    }

    private static bool TryReadMetadata(IEnumerable<string> lines, out Dictionary<string, string> metadata)
    {
        metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
            {
                return false;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                return false;
            }

            metadata[key] = value;
        }

        return true;
    }

    private static string BuildLockFileContent(IReadOnlyList<PackageLockEntry> entries)
    {
        var sorted = entries
            .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Version, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# Oaf package lock");
        builder.AppendLine($"format={LockFormatVersion}");
        builder.AppendLine($"dependency_count={sorted.Length}");
        builder.AppendLine($"manifest_sha256={ComputeManifestHash(sorted)}");

        foreach (var entry in sorted)
        {
            builder.AppendLine(
                $"{entry.Name}@{entry.Version} source={entry.Source} sha256={entry.DependencyHash} artifact_sha256={entry.ArtifactSha256}");
        }

        return builder.ToString();
    }

    private static bool TryParseLockFile(string contents, out ParsedLockFile lockFile, out string? error)
    {
        lockFile = default;
        error = null;

        var dependencyCount = default(int?);
        var manifestHash = default(string);
        var format = 1;
        var entries = new List<PackageLockEntry>();

        foreach (var rawLine in NormalizeLineEndings(contents).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("format=", StringComparison.Ordinal))
            {
                var formatRaw = line["format=".Length..];
                if (!int.TryParse(formatRaw, out format))
                {
                    error = $"Invalid format value '{formatRaw}'.";
                    return false;
                }

                continue;
            }

            if (line.StartsWith("dependency_count=", StringComparison.Ordinal))
            {
                var countRaw = line["dependency_count=".Length..];
                if (!int.TryParse(countRaw, out var parsedCount))
                {
                    error = $"Invalid dependency_count value '{countRaw}'.";
                    return false;
                }

                dependencyCount = parsedCount;
                continue;
            }

            if (line.StartsWith("manifest_sha256=", StringComparison.Ordinal))
            {
                manifestHash = line["manifest_sha256=".Length..].Trim();
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                error = $"Invalid lock entry '{line}'.";
                return false;
            }

            if (!TryParseDependencySpec(tokens[0], out var dependency))
            {
                error = $"Invalid lock dependency '{tokens[0]}'.";
                return false;
            }

            var source = ManifestSourceName;
            var sha = string.Empty;
            var artifactSha = NoArtifactHash;
            for (var i = 1; i < tokens.Length; i++)
            {
                var token = tokens[i];
                var separatorIndex = token.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
                {
                    error = $"Invalid lock token '{token}' in line '{line}'.";
                    return false;
                }

                var key = token[..separatorIndex];
                var value = token[(separatorIndex + 1)..];
                switch (key)
                {
                    case "source":
                        source = value;
                        break;
                    case "sha256":
                        sha = value.ToLowerInvariant();
                        break;
                    case "artifact_sha256":
                        artifactSha = value.ToLowerInvariant();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(source) || !IsValidSourceName(source))
            {
                error = $"Invalid source value '{source}' for '{dependency}'.";
                return false;
            }

            if (!IsValidSha256(sha))
            {
                error = $"Invalid sha256 value for '{dependency}'.";
                return false;
            }

            if (!string.Equals(artifactSha, NoArtifactHash, StringComparison.Ordinal) &&
                !IsValidSha256(artifactSha))
            {
                error = $"Invalid artifact_sha256 value for '{dependency}'.";
                return false;
            }

            entries.Add(new PackageLockEntry(
                dependency.Name,
                dependency.Version,
                source,
                sha,
                artifactSha));
        }

        lockFile = new ParsedLockFile(format, dependencyCount, manifestHash, entries);
        return true;
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string ComputeManifestHash(IReadOnlyList<PackageLockEntry> entries)
    {
        var payload = string.Join(
            "\n",
            entries
                .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Version, StringComparer.Ordinal)
                .Select(static entry => BuildManifestHashPayload(entry)));
        return ComputeSha256Hex(payload);
    }

    private static string BuildManifestHashPayload(PackageLockEntry entry)
    {
        return $"name={entry.Name}\nversion={entry.Version}\nsource={entry.Source}\nsha256={entry.DependencyHash}\nartifact_sha256={entry.ArtifactSha256}";
    }

    private static string ComputeDependencyHash(OafPackageDependency dependency, string sourceName)
    {
        return ComputeSha256Hex(BuildDependencyHashPayload(dependency, sourceName));
    }

    private static string BuildDependencyHashPayload(OafPackageDependency dependency, string sourceName)
    {
        return $"name={dependency.Name}\nversion={dependency.Version}\nsource={sourceName}";
    }

    private static string ComputeFileSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private readonly record struct PackageSourcePackage(
        string Name,
        string Version,
        ComparableVersion ParsedVersion,
        string SourceName,
        string ArtifactPath,
        string ArtifactSha256,
        IReadOnlyList<OafPackageRequirement> Dependencies);

    private readonly record struct PackageSourceIndex(
        string Name,
        string IndexPath,
        IReadOnlyList<PackageSourcePackage> Packages);

    private readonly record struct PackageLockEntry(
        string Name,
        string Version,
        string Source,
        string DependencyHash,
        string ArtifactSha256);

    private readonly record struct ParsedLockFile(
        int Format,
        int? DependencyCount,
        string? ManifestSha256,
        IReadOnlyList<PackageLockEntry> Entries);

    private readonly record struct PackageModuleDescriptor(
        string ModuleName,
        IReadOnlyList<string> Imports);

    private readonly record struct PackageModuleSource(
        string ModuleName,
        IReadOnlyList<string> Imports,
        string Source,
        string Path);

    private readonly record struct ResolutionConstraint(
        VersionSelector Selector,
        string RawSelector,
        string Origin);

    private enum VersionComparisonOperator
    {
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }

    private readonly record struct VersionComparator(
        VersionComparisonOperator Operator,
        ComparableVersion Version);

    private readonly record struct VersionSelector(
        bool MatchAny,
        IReadOnlyList<VersionComparator> Comparators);

    private readonly record struct ComparableVersion(
        IReadOnlyList<int> NumericParts,
        string? PreRelease) : IComparable<ComparableVersion>
    {
        public int CompareTo(ComparableVersion other)
        {
            var maxParts = Math.Max(NumericParts.Count, other.NumericParts.Count);
            for (var i = 0; i < maxParts; i++)
            {
                var left = i < NumericParts.Count ? NumericParts[i] : 0;
                var right = i < other.NumericParts.Count ? other.NumericParts[i] : 0;
                if (left != right)
                {
                    return left.CompareTo(right);
                }
            }

            var leftPre = PreRelease ?? string.Empty;
            var rightPre = other.PreRelease ?? string.Empty;
            if (leftPre.Length == 0 && rightPre.Length == 0)
            {
                return 0;
            }

            if (leftPre.Length == 0)
            {
                return 1;
            }

            if (rightPre.Length == 0)
            {
                return -1;
            }

            var leftParts = leftPre.Split('.');
            var rightParts = rightPre.Split('.');
            var maxPreParts = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < maxPreParts; i++)
            {
                if (i >= leftParts.Length)
                {
                    return -1;
                }

                if (i >= rightParts.Length)
                {
                    return 1;
                }

                var leftToken = leftParts[i];
                var rightToken = rightParts[i];

                var leftIsNumber = int.TryParse(leftToken, out var leftNumber);
                var rightIsNumber = int.TryParse(rightToken, out var rightNumber);
                if (leftIsNumber && rightIsNumber)
                {
                    if (leftNumber != rightNumber)
                    {
                        return leftNumber.CompareTo(rightNumber);
                    }

                    continue;
                }

                if (leftIsNumber != rightIsNumber)
                {
                    return leftIsNumber ? -1 : 1;
                }

                var lexical = string.Compare(leftToken, rightToken, StringComparison.Ordinal);
                if (lexical != 0)
                {
                    return lexical;
                }
            }

            return 0;
        }
    }

    internal sealed class PackageSourceIndexDocument
    {
        public string? Source { get; init; }

        public List<PackageSourcePackageDocument>? Packages { get; init; }
    }

    internal sealed class PackageSourcePackageDocument
    {
        public string? Name { get; init; }

        public string? Version { get; init; }

        public string? Artifact { get; init; }

        public string? Sha256 { get; init; }

        public List<string>? Dependencies { get; init; }
    }
}
