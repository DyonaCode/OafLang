using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tooling.Benchmarking;
using Oaf.Tooling.PackageManagement;

namespace Oaf.Tooling.Sdk;

public enum OafProjectTestRuntime
{
    Vm,
    Native
}

public sealed class OafProjectTestCaseResult
{
    public OafProjectTestCaseResult(string filePath, bool success, string message, object? returnValue = null)
    {
        FilePath = filePath;
        Success = success;
        Message = message;
        ReturnValue = returnValue;
    }

    public string FilePath { get; }

    public bool Success { get; }

    public string Message { get; }

    public object? ReturnValue { get; }
}

public sealed class OafProjectTestReport
{
    public OafProjectTestReport(
        IReadOnlyList<OafProjectTestCaseResult> results,
        bool packageVerificationAttempted,
        bool packageVerificationSucceeded,
        string? packageVerificationMessage)
    {
        Results = results;
        PackageVerificationAttempted = packageVerificationAttempted;
        PackageVerificationSucceeded = packageVerificationSucceeded;
        PackageVerificationMessage = packageVerificationMessage;
    }

    public IReadOnlyList<OafProjectTestCaseResult> Results { get; }

    public bool PackageVerificationAttempted { get; }

    public bool PackageVerificationSucceeded { get; }

    public string? PackageVerificationMessage { get; }

    public int TotalFiles => Results.Count;

    public int PassedFiles => Results.Count(static result => result.Success);

    public int FailedFiles => Results.Count(static result => !result.Success);

    public bool HasFailures => FailedFiles > 0 || (PackageVerificationAttempted && !PackageVerificationSucceeded);
}

public static class OafProjectTester
{
    public static OafProjectTestReport Run(
        string? targetPath,
        CompilerCompilationTarget compilationTarget = CompilerCompilationTarget.Bytecode,
        OafProjectTestRuntime runtime = OafProjectTestRuntime.Vm)
    {
        var resolvedTarget = ResolveTargetPath(targetPath);
        var files = DiscoverTestFiles(resolvedTarget);
        var results = new List<OafProjectTestCaseResult>(files.Count);

        if (runtime == OafProjectTestRuntime.Native && !OafNativeKernelExecutor.IsNativeCompilerAvailable())
        {
            return new OafProjectTestReport(
                [new OafProjectTestCaseResult(
                    resolvedTarget,
                    false,
                    "Native test execution requested, but no C compiler is available.")],
                packageVerificationAttempted: false,
                packageVerificationSucceeded: true,
                packageVerificationMessage: null);
        }

        var manifestPath = ResolveNearestManifestPath(resolvedTarget);
        var packageVerifyAttempted = false;
        var packageVerifySucceeded = true;
        string? packageVerifyMessage = null;
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();
            var lockPath = Path.Combine(manifestDirectory, OafPackageManager.DefaultLockFileName);
            if (File.Exists(lockPath))
            {
                packageVerifyAttempted = true;
                packageVerifySucceeded = OafPackageManager.VerifyInstall(manifestPath, out packageVerifyMessage);
            }
            else
            {
                packageVerifyMessage = $"Skipped package verification (no {OafPackageManager.DefaultLockFileName} present).";
            }
        }

        var driver = new CompilerDriver(enableCompilationCache: false);
        var vm = runtime == OafProjectTestRuntime.Vm ? new BytecodeVirtualMachine() : null;

        if (files.Count == 0)
        {
            results.Add(new OafProjectTestCaseResult(
                resolvedTarget,
                false,
                "No .oaf files were found to test. Add tests/ or examples/ files, or pass an explicit target path."));
            return new OafProjectTestReport(results, packageVerifyAttempted, packageVerifySucceeded, packageVerifyMessage);
        }

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var searchDirectory = Path.GetDirectoryName(file) ?? Directory.GetCurrentDirectory();

            if (!OafPackageManager.TryComposeCompilationSource(source, searchDirectory, out var composedSource, out var compositionError))
            {
                results.Add(new OafProjectTestCaseResult(file, false, $"Composition failed: {compositionError}"));
                continue;
            }

            var compilation = driver.CompileSource(composedSource, compilationTarget);
            if (!compilation.Success)
            {
                var diagnostics = string.Join(
                    Environment.NewLine,
                    compilation.Diagnostics.Select(static diagnostic => diagnostic.ToString()));
                results.Add(new OafProjectTestCaseResult(file, false, diagnostics));
                continue;
            }

            if (runtime == OafProjectTestRuntime.Vm)
            {
                var execution = vm!.Execute(compilation.BytecodeProgram);
                if (!execution.Success)
                {
                    results.Add(new OafProjectTestCaseResult(file, false, execution.ErrorMessage ?? "VM execution failed."));
                    continue;
                }

                results.Add(new OafProjectTestCaseResult(file, true, "ok", execution.ReturnValue));
                continue;
            }

            try
            {
                using var nativeHandle = OafNativeKernelExecutor.CreateHandle(compilation.BytecodeProgram);
                var value = nativeHandle.ExecuteOnce();
                results.Add(new OafProjectTestCaseResult(file, true, "ok", value));
            }
            catch (Exception ex)
            {
                results.Add(new OafProjectTestCaseResult(file, false, ex.Message));
            }
        }

        return new OafProjectTestReport(results, packageVerifyAttempted, packageVerifySucceeded, packageVerifyMessage);
    }

    private static string ResolveTargetPath(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(targetPath);
    }

    private static List<string> DiscoverTestFiles(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            if (!targetPath.EndsWith(".oaf", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return [targetPath];
        }

        var files = new List<string>();
        if (Directory.Exists(targetPath))
        {
            var testsPath = Path.Combine(targetPath, "tests");
            var examplesPath = Path.Combine(targetPath, "examples");

            if (Directory.Exists(testsPath))
            {
                files.AddRange(EnumerateOafFiles(testsPath));
            }

            if (Directory.Exists(examplesPath))
            {
                files.AddRange(EnumerateOafFiles(examplesPath));
            }

            if (files.Count == 0)
            {
                var mainPath = Path.Combine(targetPath, "main.oaf");
                if (File.Exists(mainPath))
                {
                    files.Add(mainPath);
                }
            }

            if (files.Count == 0)
            {
                files.AddRange(EnumerateOafFiles(targetPath));
            }
        }

        return files
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> EnumerateOafFiles(string rootPath)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath, "*.oaf", SearchOption.AllDirectories))
        {
            if (IsIgnoredPath(file))
            {
                continue;
            }

            yield return Path.GetFullPath(file);
        }
    }

    private static bool IsIgnoredPath(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/.oaf/packages/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveNearestManifestPath(string targetPath)
    {
        string startingDirectory;
        if (File.Exists(targetPath))
        {
            startingDirectory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        }
        else if (Directory.Exists(targetPath))
        {
            startingDirectory = targetPath;
        }
        else
        {
            startingDirectory = Directory.GetCurrentDirectory();
        }

        var directory = new DirectoryInfo(startingDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, OafPackageManager.DefaultManifestFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
