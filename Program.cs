using System.Globalization;
using System.Reflection;
using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.CodeGen;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tooling.Benchmarking;
using Oaf.Tooling.Documentation;
using Oaf.Tooling.Formatting;
using Oaf.Tooling.PackageManagement;
using Oaf.Tooling.Sdk;
using Oaf.Tests;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    Environment.ExitCode = TestRunner.RunAll(Console.Out);
    return;
}

if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
{
    Console.WriteLine(GetCliVersion());
    return;
}

if (args.Length == 0)
{
    if (File.Exists("main.oaf"))
    {
        var runArgs = new[] { "run", "main.oaf" };
        if (TryHandleSdkCommand(runArgs, out var defaultRunExitCode))
        {
            Environment.ExitCode = defaultRunExitCode;
            return;
        }
    }

    PrintUsage();
    return;
}

if (args.Contains("--help", StringComparer.Ordinal)
    || string.Equals(args[0], "help", StringComparison.Ordinal))
{
    PrintUsage();
    return;
}

if (TryHandleSdkCommand(args, out var sdkExitCode))
{
    Environment.ExitCode = sdkExitCode;
    return;
}

if (TryHandleToolingCommand(args, out var toolingExitCode))
{
    Environment.ExitCode = toolingExitCode;
    return;
}

var sourceArg = args[0];
if (sourceArg.StartsWith("-", StringComparison.Ordinal))
{
    Console.WriteLine("Missing input source. Use <file.oaf> or inline source.");
    Environment.ExitCode = 1;
    return;
}

if (!TryResolveCompilationTargetFromArgs(args, out var directCompilationTarget, out var directCompilationTargetError))
{
    Console.WriteLine(directCompilationTargetError);
    Environment.ExitCode = 1;
    return;
}

if (!TryCompileSdkInput(sourceArg, directCompilationTarget, out var result, out var setupError))
{
    Console.WriteLine($"Compilation setup failed: {setupError}");
    Environment.ExitCode = 1;
    return;
}

PrintCompilationArtifacts(result, args);

if (args.Contains("--run-bytecode", StringComparer.Ordinal))
{
    var execution = ExecuteBytecodeVm(result.BytecodeProgram);
    if (!execution.Success)
    {
        Console.WriteLine($"Bytecode runtime error: {execution.ErrorMessage}");
        Environment.ExitCode = 1;
        return;
    }

    if (execution.ReturnValue is not null)
    {
        Console.WriteLine($"Return: {execution.ReturnValue}");
    }
}

PrintDiagnostics(result.Diagnostics);
Environment.ExitCode = result.Success ? 0 : 1;

static bool TryHandleSdkCommand(string[] args, out int exitCode)
{
    exitCode = 0;
    if (args.Length == 0)
    {
        return false;
    }

    switch (args[0])
    {
        case "run":
            return HandleSdkRun(args, out exitCode);
        case "build":
            return HandleSdkBuild(args, out exitCode);
        case "publish":
            return HandleSdkPublish(args, out exitCode);
        case "new":
            return HandleSdkNew(args, out exitCode);
        case "test":
            return HandleSdkTest(args, out exitCode);
        case "version":
            return HandleSdkVersion(args, out exitCode);
        case "clean":
            return HandleSdkClean(args, out exitCode);
        default:
            return false;
    }
}

static bool HandleSdkRun(string[] args, out int exitCode)
{
    exitCode = 0;
    if (!TryParseSdkCommandOptions(args, requireInput: true, allowOutput: false, allowRuntime: true, out var options, out var errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeSdkRuntime(options.Runtime, out var runtimeMode, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeCompilationTarget(options.CompilationTarget, out var compilationTarget, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryCompileSdkInput(options.Input!, compilationTarget, out var result, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    PrintCompilationArtifacts(result, args);
    PrintDiagnostics(result.Diagnostics);
    if (!result.Success)
    {
        exitCode = 1;
        return true;
    }

    if (runtimeMode == SdkRuntimeMode.Vm)
    {
        var execution = ExecuteBytecodeVm(result.BytecodeProgram);
        if (!execution.Success)
        {
            Console.WriteLine($"Bytecode runtime error: {execution.ErrorMessage}");
            exitCode = 1;
            return true;
        }

        if (execution.ReturnValue is not null)
        {
            Console.WriteLine($"Return: {execution.ReturnValue}");
        }

        return true;
    }

    if (!OafNativeKernelExecutor.IsNativeCompilerAvailable())
    {
        Console.WriteLine("No C compiler found for -r native. Install a compiler or use -r vm.");
        exitCode = 1;
        return true;
    }

    try
    {
        using var nativeHandle = OafNativeKernelExecutor.CreateHandle(result.BytecodeProgram);
        var value = nativeHandle.ExecuteOnce();
        Console.WriteLine($"Return: {value}");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Native runtime error: {ex.Message}");
        exitCode = 1;
        return true;
    }
}

static bool HandleSdkBuild(string[] args, out int exitCode)
{
    exitCode = 0;
    if (!TryParseSdkCommandOptions(args, requireInput: true, allowOutput: true, allowRuntime: true, out var options, out var errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeSdkRuntime(options.Runtime, out var runtimeMode, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeCompilationTarget(options.CompilationTarget, out var compilationTarget, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryCompileSdkInput(options.Input!, compilationTarget, out var result, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    PrintCompilationArtifacts(result, args);
    PrintDiagnostics(result.Diagnostics);
    if (!result.Success)
    {
        exitCode = 1;
        return true;
    }

    var outputPath = ResolveBuildOutputPath(options.OutputPath, options.Input!, runtimeMode);
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    if (runtimeMode == SdkRuntimeMode.Vm)
    {
        File.WriteAllText(outputPath, BytecodePrinter.Print(result.BytecodeProgram));
        Console.WriteLine($"Built bytecode artifact: {Path.GetFullPath(outputPath)}");
        return true;
    }

    if (!OafNativeKernelExecutor.IsNativeCompilerAvailable())
    {
        Console.WriteLine("No C compiler found for -r native. Install a compiler or use -r vm.");
        exitCode = 1;
        return true;
    }

    try
    {
        using var nativeHandle = OafNativeKernelExecutor.CreateHandle(result.BytecodeProgram);
        File.Copy(nativeHandle.ExecutablePath, outputPath, overwrite: true);
        EnsureExecutablePermissions(outputPath);
        Console.WriteLine($"Built native executable: {Path.GetFullPath(outputPath)}");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Native build failed: {ex.Message}");
        exitCode = 1;
        return true;
    }
}

static bool HandleSdkPublish(string[] args, out int exitCode)
{
    exitCode = 0;
    if (!TryParseSdkCommandOptions(args, requireInput: true, allowOutput: true, allowRuntime: true, out var options, out var errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!options.RuntimeProvided)
    {
        options.Runtime = "native";
    }

    if (!TryNormalizeSdkRuntime(options.Runtime, out var runtimeMode, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeCompilationTarget(options.CompilationTarget, out var compilationTarget, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (runtimeMode != SdkRuntimeMode.Native)
    {
        Console.WriteLine("Publish currently targets native executables only. Use -r native.");
        exitCode = 1;
        return true;
    }

    if (!TryCompileSdkInput(options.Input!, compilationTarget, out var result, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    PrintCompilationArtifacts(result, args);
    PrintDiagnostics(result.Diagnostics);
    if (!result.Success)
    {
        exitCode = 1;
        return true;
    }

    if (!OafNativeKernelExecutor.IsNativeCompilerAvailable())
    {
        Console.WriteLine("No C compiler found for publish. Install a compiler, or use `oaf build` for bytecode artifacts.");
        exitCode = 1;
        return true;
    }

    var outputPath = ResolvePublishOutputPath(options.OutputPath, options.Input!);
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    try
    {
        using var nativeHandle = OafNativeKernelExecutor.CreateHandle(result.BytecodeProgram);
        File.Copy(nativeHandle.ExecutablePath, outputPath, overwrite: true);
        EnsureExecutablePermissions(outputPath);
        Console.WriteLine($"Published executable: {Path.GetFullPath(outputPath)}");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Publish failed: {ex.Message}");
        exitCode = 1;
        return true;
    }
}

static bool HandleSdkNew(string[] args, out int exitCode)
{
    exitCode = 0;

    string? targetPath = null;
    var force = false;
    for (var i = 1; i < args.Length; i++)
    {
        var token = args[i];
        if (string.Equals(token, "--force", StringComparison.Ordinal))
        {
            force = true;
            continue;
        }

        if (token.StartsWith("-", StringComparison.Ordinal))
        {
            Console.WriteLine($"Unknown option '{token}'.");
            exitCode = 1;
            return true;
        }

        if (targetPath is not null)
        {
            Console.WriteLine($"Unexpected argument '{token}'. Use: oaf new [path] [--force]");
            exitCode = 1;
            return true;
        }

        targetPath = token;
    }

    var scaffold = OafProjectScaffolder.Create(targetPath, force);
    Console.WriteLine(scaffold.Message);
    if (!scaffold.Success)
    {
        exitCode = 1;
        return true;
    }

    foreach (var created in scaffold.CreatedPaths.OrderBy(static path => path, StringComparer.Ordinal))
    {
        var relative = Path.GetRelativePath(scaffold.ProjectPath, created);
        Console.WriteLine($"created: {relative}");
    }

    return true;
}

static bool HandleSdkTest(string[] args, out int exitCode)
{
    exitCode = 0;
    if (!TryParseSdkCommandOptions(args, requireInput: false, allowOutput: false, allowRuntime: true, out var options, out var errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeSdkRuntime(options.Runtime, out var runtimeMode, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    if (!TryNormalizeCompilationTarget(options.CompilationTarget, out var compilationTarget, out errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    var runtime = runtimeMode == SdkRuntimeMode.Native
        ? OafProjectTestRuntime.Native
        : OafProjectTestRuntime.Vm;
    var report = OafProjectTester.Run(options.Input, compilationTarget, runtime);

    if (report.PackageVerificationAttempted)
    {
        var packageStatus = report.PackageVerificationSucceeded ? "PASS" : "FAIL";
        Console.WriteLine($"{packageStatus} package-verify: {report.PackageVerificationMessage}");
    }

    foreach (var result in report.Results)
    {
        var status = result.Success ? "PASS" : "FAIL";
        var displayPath = Path.GetFullPath(result.FilePath);
        if (result.Success)
        {
            if (result.ReturnValue is null)
            {
                Console.WriteLine($"{status} {displayPath}");
            }
            else
            {
                Console.WriteLine($"{status} {displayPath} (return={result.ReturnValue})");
            }
        }
        else
        {
            Console.WriteLine($"{status} {displayPath}: {result.Message}");
        }
    }

    Console.WriteLine($"Executed {report.TotalFiles} file(s), {report.FailedFiles} failed.");
    exitCode = report.HasFailures ? 1 : 0;
    return true;
}

static bool HandleSdkVersion(string[] args, out int exitCode)
{
    exitCode = 0;

    var oafHome = ResolveOafHomePath();
    var versionsDir = Path.Combine(oafHome, "versions");
    var currentVersionFilePath = Path.Combine(oafHome, "current.txt");
    var installedVersions = GetInstalledVersions(versionsDir);

    if (args.Length == 1)
    {
        Console.WriteLine($"CLI version: {GetCliVersion()}");
        Console.WriteLine($"Install root: {oafHome}");

        if (installedVersions.Count == 0)
        {
            Console.WriteLine("Installed versions: (none)");
            return true;
        }

        var currentVersion = ReadCurrentVersion(currentVersionFilePath);
        Console.WriteLine("Installed versions:");
        foreach (var version in installedVersions)
        {
            var marker = string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            Console.WriteLine($"{marker} {version}");
        }

        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            Console.WriteLine("Current version: (not set)");
        }
        else
        {
            Console.WriteLine($"Current version: {currentVersion}");
        }

        return true;
    }

    if (args.Length != 2)
    {
        Console.WriteLine("Usage: oaf version [<version>]");
        exitCode = 1;
        return true;
    }

    if (installedVersions.Count == 0)
    {
        Console.WriteLine($"No installed versions were found in '{versionsDir}'.");
        exitCode = 1;
        return true;
    }

    var requestedVersion = args[1];
    var matchedVersion = ResolveRequestedVersion(requestedVersion, installedVersions);
    if (matchedVersion is null)
    {
        Console.WriteLine($"Version '{requestedVersion}' is not installed.");
        Console.WriteLine($"Installed versions: {string.Join(", ", installedVersions)}");
        exitCode = 1;
        return true;
    }

    WriteCurrentVersion(currentVersionFilePath, matchedVersion);
    Console.WriteLine($"Active Oaf version set to {matchedVersion}.");
    return true;
}

static bool HandleSdkClean(string[] args, out int exitCode)
{
    exitCode = 0;
    if (!TryParseSdkCommandOptions(args, requireInput: false, allowOutput: true, allowRuntime: true, out var options, out var errorMessage))
    {
        Console.WriteLine(errorMessage);
        exitCode = 1;
        return true;
    }

    var targetPath = string.IsNullOrWhiteSpace(options.OutputPath)
        ? Path.Combine(".oaf", "build")
        : options.OutputPath!;

    if (File.Exists(targetPath))
    {
        File.Delete(targetPath);
        Console.WriteLine($"Removed file '{Path.GetFullPath(targetPath)}'.");
        return true;
    }

    if (Directory.Exists(targetPath))
    {
        Directory.Delete(targetPath, recursive: true);
        Console.WriteLine($"Removed directory '{Path.GetFullPath(targetPath)}'.");
        return true;
    }

    Console.WriteLine($"Nothing to clean at '{Path.GetFullPath(targetPath)}'.");
    return true;
}

static bool TryCompileSdkInput(
    string inputArg,
    CompilerCompilationTarget compilationTarget,
    out CompilationResult result,
    out string? errorMessage)
{
    result = null!;
    errorMessage = null;

    var isFileInput = File.Exists(inputArg);
    var source = isFileInput
        ? File.ReadAllText(inputArg)
        : inputArg;
    var searchDirectory = isFileInput
        ? Path.GetDirectoryName(Path.GetFullPath(inputArg)) ?? Directory.GetCurrentDirectory()
        : Directory.GetCurrentDirectory();

    if (!OafPackageManager.TryComposeCompilationSource(source, searchDirectory, out var composedSource, out errorMessage))
    {
        return false;
    }

    var driver = new CompilerDriver();
    result = driver.CompileSource(composedSource, compilationTarget);
    return true;
}

static string ResolveBuildOutputPath(string? outputOption, string inputArg, SdkRuntimeMode runtimeMode)
{
    var defaultFileName = ResolveDefaultArtifactFileName(inputArg, runtimeMode);

    if (string.IsNullOrWhiteSpace(outputOption))
    {
        return Path.Combine(".oaf", "build", defaultFileName);
    }

    return ResolveOutputPathWithDefault(outputOption, defaultFileName);
}

static string ResolvePublishOutputPath(string? outputOption, string inputArg)
{
    var defaultFileName = ResolveDefaultArtifactFileName(inputArg, SdkRuntimeMode.Native);

    if (string.IsNullOrWhiteSpace(outputOption))
    {
        return Path.Combine(".oaf", "publish", defaultFileName);
    }

    return ResolveOutputPathWithDefault(outputOption, defaultFileName);
}

static string ResolveDefaultArtifactFileName(string inputArg, SdkRuntimeMode runtimeMode)
{
    var baseName = File.Exists(inputArg)
        ? Path.GetFileNameWithoutExtension(inputArg)
        : "snippet";
    var executableSuffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    return runtimeMode == SdkRuntimeMode.Native
        ? $"{baseName}{executableSuffix}"
        : $"{baseName}.bytecode.txt";
}

static string ResolveOutputPathWithDefault(string? outputOption, string defaultFileName)
{
    var path = outputOption!;
    var endsWithSeparator = path.EndsWith(Path.DirectorySeparatorChar)
        || path.EndsWith(Path.AltDirectorySeparatorChar);
    if (Directory.Exists(path) || endsWithSeparator)
    {
        return Path.Combine(path, defaultFileName);
    }

    if (!Path.HasExtension(path))
    {
        return Path.Combine(path, defaultFileName);
    }

    return path;
}

static void EnsureExecutablePermissions(string outputPath)
{
    if (OperatingSystem.IsWindows())
    {
        return;
    }

    try
    {
        var mode = File.GetUnixFileMode(outputPath);
        mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(outputPath, mode);
    }
    catch
    {
        // Best-effort only; publish still succeeds if chmod is unavailable.
    }
}

static string GetCliVersion()
{
    var assembly = typeof(SdkCommandOptions).Assembly;
    var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informationalVersion))
    {
        var plusIndex = informationalVersion.IndexOf('+');
        return plusIndex >= 0
            ? informationalVersion[..plusIndex]
            : informationalVersion;
    }

    var assemblyVersion = assembly.GetName().Version;
    return assemblyVersion?.ToString() ?? "unknown";
}

static string ResolveOafHomePath()
{
    var configuredPath = Environment.GetEnvironmentVariable("OAF_HOME");
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return configuredPath;
    }

    var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(userHome, ".oaf");
}

static string ReadCurrentVersion(string currentVersionFilePath)
{
    if (!File.Exists(currentVersionFilePath))
    {
        return string.Empty;
    }

    return File.ReadAllText(currentVersionFilePath).Trim();
}

static void WriteCurrentVersion(string currentVersionFilePath, string version)
{
    var directory = Path.GetDirectoryName(currentVersionFilePath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(currentVersionFilePath, $"{version}{Environment.NewLine}");
}

static List<string> GetInstalledVersions(string versionsDir)
{
    var executableName = OperatingSystem.IsWindows() ? "oaf.exe" : "oaf";
    var versions = new List<string>();
    if (!Directory.Exists(versionsDir))
    {
        return versions;
    }

    foreach (var candidatePath in Directory.EnumerateDirectories(versionsDir))
    {
        var versionName = Path.GetFileName(candidatePath);
        if (string.IsNullOrWhiteSpace(versionName))
        {
            continue;
        }

        var binaryPath = Path.Combine(candidatePath, "bin", executableName);
        if (File.Exists(binaryPath))
        {
            versions.Add(versionName);
        }
    }

    versions.Sort(CompareVersionTokensDescending);
    return versions;
}

static string? ResolveRequestedVersion(string requestedVersion, IReadOnlyList<string> installedVersions)
{
    var requestedToken = NormalizeVersionToken(requestedVersion);

    foreach (var installed in installedVersions)
    {
        if (string.Equals(NormalizeVersionToken(installed), requestedToken, StringComparison.OrdinalIgnoreCase))
        {
            return installed;
        }
    }

    var prefixedMatches = installedVersions
        .Where((installed) =>
            NormalizeVersionToken(installed).StartsWith($"{requestedToken}.", StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (prefixedMatches.Count > 0)
    {
        prefixedMatches.Sort(CompareVersionTokensDescending);
        return prefixedMatches[0];
    }

    return null;
}

static string NormalizeVersionToken(string version)
{
    return version.Trim().TrimStart('v', 'V');
}

static int CompareVersionTokensDescending(string left, string right)
{
    var leftToken = NormalizeVersionToken(left);
    var rightToken = NormalizeVersionToken(right);

    var leftIsSemver = Version.TryParse(leftToken, out var leftVersion);
    var rightIsSemver = Version.TryParse(rightToken, out var rightVersion);
    if (leftIsSemver && rightIsSemver)
    {
        return rightVersion!.CompareTo(leftVersion);
    }

    if (leftIsSemver)
    {
        return -1;
    }

    if (rightIsSemver)
    {
        return 1;
    }

    return string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
}

static bool TryNormalizeSdkRuntime(string runtimeRaw, out SdkRuntimeMode runtimeMode, out string? errorMessage)
{
    errorMessage = null;
    var normalized = runtimeRaw.Trim().ToLowerInvariant();
    switch (normalized)
    {
        case "vm":
            runtimeMode = SdkRuntimeMode.Vm;
            return true;
        case "native":
        case "exe":
            runtimeMode = SdkRuntimeMode.Native;
            return true;
        default:
            runtimeMode = SdkRuntimeMode.Vm;
            errorMessage = $"Unsupported runtime '{runtimeRaw}'. Use -r vm or -r native.";
            return false;
    }
}

static bool TryNormalizeCompilationTarget(string targetRaw, out CompilerCompilationTarget compilationTarget, out string? errorMessage)
{
    errorMessage = null;
    var normalized = targetRaw.Trim().ToLowerInvariant();
    switch (normalized)
    {
        case "bytecode":
        case "bc":
            compilationTarget = CompilerCompilationTarget.Bytecode;
            return true;
        case "mlir":
            compilationTarget = CompilerCompilationTarget.Mlir;
            return true;
        default:
            compilationTarget = CompilerCompilationTarget.Bytecode;
            errorMessage = $"Unsupported compilation target '{targetRaw}'. Use --compilation-target bytecode or --compilation-target mlir.";
            return false;
    }
}

static bool TryResolveCompilationTargetFromArgs(string[] args, out CompilerCompilationTarget compilationTarget, out string? errorMessage)
{
    var targetRaw = TryGetOptionValue(args, "--compilation-target");
    if (string.IsNullOrWhiteSpace(targetRaw))
    {
        compilationTarget = CompilerCompilationTarget.Bytecode;
        errorMessage = null;
        return true;
    }

    return TryNormalizeCompilationTarget(targetRaw, out compilationTarget, out errorMessage);
}

static void PrintCompilationArtifacts(CompilationResult result, string[] args)
{
    if (args.Contains("--ast", StringComparer.Ordinal))
    {
        Console.WriteLine(AstPrinter.Print(result.SyntaxTree));
    }

    if (args.Contains("--ir", StringComparer.Ordinal))
    {
        Console.WriteLine(IrPrinter.Print(result.IrModule));
    }

    if (args.Contains("--bytecode", StringComparer.Ordinal))
    {
        Console.WriteLine(BytecodePrinter.Print(result.BytecodeProgram));
    }

    if (args.Contains("--mlir", StringComparer.Ordinal))
    {
        Console.WriteLine(MlirPrinter.Print(result.IrModule));
    }
}

static BytecodeExecutionResult ExecuteBytecodeVm(BytecodeProgram program)
{
    var vm = new BytecodeVirtualMachine();
    return vm.Execute(program);
}

static void PrintDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
{
    foreach (var diagnostic in diagnostics)
    {
        Console.WriteLine(diagnostic);
    }
}

static bool TryParseSdkCommandOptions(
    string[] args,
    bool requireInput,
    bool allowOutput,
    bool allowRuntime,
    out SdkCommandOptions options,
    out string? errorMessage)
{
    options = new SdkCommandOptions();
    errorMessage = null;

    for (var i = 1; i < args.Length; i++)
    {
        var token = args[i];
        if (token is "-o" or "--output")
        {
            if (!allowOutput)
            {
                errorMessage = "-o/--output is not supported for this command.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                errorMessage = "Missing value for -o/--output.";
                return false;
            }

            options.OutputPath = args[++i];
            continue;
        }

        if (token is "-r" or "--runtime")
        {
            if (!allowRuntime)
            {
                errorMessage = "-r/--runtime is not supported for this command.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                errorMessage = "Missing value for -r/--runtime.";
                return false;
            }

            options.Runtime = args[++i];
            options.RuntimeProvided = true;
            continue;
        }

        if (token == "--compilation-target")
        {
            if (i + 1 >= args.Length)
            {
                errorMessage = "Missing value for --compilation-target.";
                return false;
            }

            options.CompilationTarget = args[++i];
            continue;
        }

        if (token == "--ast")
        {
            options.ShowAst = true;
            continue;
        }

        if (token == "--ir")
        {
            options.ShowIr = true;
            continue;
        }

        if (token == "--bytecode")
        {
            options.ShowBytecode = true;
            continue;
        }

        if (token == "--mlir")
        {
            options.ShowMlir = true;
            continue;
        }

        if (token.StartsWith("-", StringComparison.Ordinal))
        {
            errorMessage = $"Unknown option '{token}'.";
            return false;
        }

        if (options.Input is not null)
        {
            errorMessage = $"Unexpected argument '{token}'. Provide only one input source/file.";
            return false;
        }

        options.Input = token;
    }

    if (!requireInput)
    {
        return true;
    }

    if (!string.IsNullOrWhiteSpace(options.Input))
    {
        return true;
    }

    if (File.Exists("main.oaf"))
    {
        options.Input = "main.oaf";
        return true;
    }

    errorMessage = "Missing input source. Use <file.oaf> or inline source.";
    return false;
}

static void PrintUsage()
{
    Console.WriteLine("Oaf SDK/CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  oaf                           (runs ./main.oaf when present)");
    Console.WriteLine("  oaf --version");
    Console.WriteLine("  oaf version [<version>]");
    Console.WriteLine("  oaf new [path] [--force]");
    Console.WriteLine("  oaf test [path] [-r vm|native] [--compilation-target bytecode|mlir]");
    Console.WriteLine("  oaf run <file-or-source> [-r vm|native] [--compilation-target bytecode|mlir] [--ast] [--ir] [--bytecode] [--mlir]");
    Console.WriteLine("  oaf build <file-or-source> [-o <output-path>] [-r vm|native] [--compilation-target bytecode|mlir] [--ast] [--ir] [--bytecode] [--mlir]");
    Console.WriteLine("  oaf publish <file-or-source> [-o <output-path>] [-r native] [--compilation-target bytecode|mlir] [--ast] [--ir] [--bytecode]");
    Console.WriteLine("  oaf clean [-o <path>]");
    Console.WriteLine("  oaf --self-test");
    Console.WriteLine("  oaf --pkg-init [manifestPath]");
    Console.WriteLine("  oaf --pkg-add <name@version> [manifestPath]");
    Console.WriteLine("  oaf --pkg-remove <name> [manifestPath]");
    Console.WriteLine("  oaf --pkg-install [manifestPath]");
    Console.WriteLine("  oaf --pkg-verify [manifestPath]");
    Console.WriteLine("  oaf add [package] <name@version> [manifestPath]");
    Console.WriteLine("  oaf remove [package] <name> [manifestPath]");
    Console.WriteLine("  oaf restore [manifestPath]");
    Console.WriteLine("  oaf verify [manifestPath]");
    Console.WriteLine("  oaf --gen-docs <file-or-directory> [--out <outputPath>]");
    Console.WriteLine("  oaf --format <file-or-source> [--check] [--write]");
    Console.WriteLine("  oaf --benchmark [iterations] [--comparison-statistic mean|median|p95] [--max-ratio <value>] [--max-ratio-for <benchmark>=<value>] [--fail-on-regression]");
    Console.WriteLine("  oaf --benchmark-kernels [--iterations <n>] [--sum-n <n>] [--prime-n <n>] [--matrix-n <n>] [--native|--tiered] [--compilation-target bytecode|mlir]");
    Console.WriteLine("  oaf \"source code\" [--compilation-target bytecode|mlir] [--ast] [--ir] [--bytecode] [--mlir] [--run-bytecode]");
    Console.WriteLine("  oaf /path/to/file.oaf [--compilation-target bytecode|mlir] [--ast] [--ir] [--bytecode] [--mlir] [--run-bytecode]");
}

static bool TryHandleToolingCommand(string[] args, out int exitCode)
{
    exitCode = 0;
    if (args.Length == 0)
    {
        return false;
    }

    args = NormalizePackageCommandAliases(args);
    var command = args[0];
    switch (command)
    {
        case "--pkg-init":
        {
            var manifestPath = args.Length > 1 ? args[1] : OafPackageManager.DefaultManifestFileName;
            var success = OafPackageManager.InitManifest(manifestPath, out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--pkg-add":
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Missing dependency spec. Use --pkg-add <name@version> [manifestPath].");
                exitCode = 1;
                return true;
            }

            var manifestPath = args.Length > 2 ? args[2] : OafPackageManager.DefaultManifestFileName;
            var success = OafPackageManager.AddDependency(manifestPath, args[1], out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--pkg-remove":
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Missing package name. Use --pkg-remove <name> [manifestPath].");
                exitCode = 1;
                return true;
            }

            var manifestPath = args.Length > 2 ? args[2] : OafPackageManager.DefaultManifestFileName;
            var success = OafPackageManager.RemoveDependency(manifestPath, args[1], out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--pkg-install":
        {
            var manifestPath = args.Length > 1 ? args[1] : OafPackageManager.DefaultManifestFileName;
            var success = OafPackageManager.Install(manifestPath, out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--pkg-verify":
        {
            var manifestPath = args.Length > 1 ? args[1] : OafPackageManager.DefaultManifestFileName;
            var success = OafPackageManager.VerifyInstall(manifestPath, out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--gen-docs":
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Missing input path. Use --gen-docs <file-or-directory> [--out <outputPath>].");
                exitCode = 1;
                return true;
            }

            var outputPath = TryGetOptionValue(args, "--out");
            var success = OafDocumentationGenerator.GenerateFromPath(args[1], outputPath, out var message);
            Console.WriteLine(message);
            exitCode = success ? 0 : 1;
            return true;
        }

        case "--format":
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Missing input. Use --format <file-or-source> [--check] [--write].");
                exitCode = 1;
                return true;
            }

            var input = args[1];
            var checkOnly = args.Contains("--check", StringComparer.Ordinal);
            var writeBack = args.Contains("--write", StringComparer.Ordinal);

            var isFile = File.Exists(input);
            if (writeBack && !isFile)
            {
                Console.WriteLine("--write requires a file path input.");
                exitCode = 1;
                return true;
            }

            var source = isFile ? File.ReadAllText(input) : input;
            var formatted = OafCodeFormatter.Format(source);
            var normalizedOriginal = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            var normalizedFormatted = formatted.Replace("\r\n", "\n", StringComparison.Ordinal);
            var alreadyFormatted = string.Equals(normalizedOriginal, normalizedFormatted, StringComparison.Ordinal);

            if (writeBack && isFile)
            {
                File.WriteAllText(input, formatted);
                Console.WriteLine($"Formatted '{Path.GetFullPath(input)}'.");
            }
            else if (!checkOnly)
            {
                Console.Write(formatted);
            }

            if (checkOnly && !alreadyFormatted)
            {
                Console.WriteLine("Formatting changes are required.");
                exitCode = 1;
                return true;
            }

            exitCode = 0;
            return true;
        }

        case "--benchmark":
        {
            var iterations = 200;
            if (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) && !int.TryParse(args[1], out iterations))
            {
                Console.WriteLine($"Invalid iteration count '{args[1]}'.");
                exitCode = 1;
                return true;
            }

            if (iterations <= 0)
            {
                Console.WriteLine("Iteration count must be greater than zero.");
                exitCode = 1;
                return true;
            }

            var comparisonStatistic = BenchmarkStatistic.Mean;
            var comparisonStatisticRaw = TryGetOptionValue(args, "--comparison-statistic");
            if (comparisonStatisticRaw is not null && !TryParseBenchmarkStatistic(comparisonStatisticRaw, out comparisonStatistic))
            {
                Console.WriteLine($"Invalid comparison statistic '{comparisonStatisticRaw}'. Use mean, median, or p95.");
                exitCode = 1;
                return true;
            }

            var maxRatioValue = TryGetOptionValue(args, "--max-ratio");
            var maxMeanRatioValue = TryGetOptionValue(args, "--max-mean-ratio");
            if (maxRatioValue is not null && maxMeanRatioValue is not null)
            {
                Console.WriteLine("Use either --max-ratio or --max-mean-ratio, not both.");
                exitCode = 1;
                return true;
            }

            var maxAllowedRatio = OafBenchmarkRunner.DefaultMaxAllowedMeanRatio;
            var selectedRatioValue = maxRatioValue ?? maxMeanRatioValue;
            if (selectedRatioValue is not null &&
                !double.TryParse(selectedRatioValue, NumberStyles.Float, CultureInfo.InvariantCulture, out maxAllowedRatio))
            {
                var optionName = maxRatioValue is not null ? "--max-ratio" : "--max-mean-ratio";
                Console.WriteLine($"Invalid value '{selectedRatioValue}' for {optionName}.");
                exitCode = 1;
                return true;
            }

            if (maxAllowedRatio <= 0)
            {
                Console.WriteLine("Max ratio must be greater than zero.");
                exitCode = 1;
                return true;
            }

            if (!TryParsePerBenchmarkRatioOverrides(args, out var perBenchmarkRatios, out var perBenchmarkRatioError))
            {
                Console.WriteLine(perBenchmarkRatioError);
                exitCode = 1;
                return true;
            }

            var results = OafBenchmarkRunner.RunAll(iterations);
            OafBenchmarkRunner.PrintReport(Console.Out, results, comparisonStatistic);
            var regressions = OafBenchmarkRunner.AnalyzeAgainstBaselines(
                results,
                maxAllowedRatio,
                comparisonStatistic,
                perBenchmarkRatios);
            OafBenchmarkRunner.PrintRegressionReport(Console.Out, regressions);

            if (args.Contains("--fail-on-regression", StringComparer.Ordinal) && regressions.Count > 0)
            {
                Console.WriteLine($"Benchmark regression gate failed using {comparisonStatistic.ToString().ToLowerInvariant()}.");
                exitCode = 1;
                return true;
            }

            exitCode = 0;
            return true;
        }

        case "--benchmark-kernels":
        {
            var iterations = 5;
            var iterationsRaw = TryGetOptionValue(args, "--iterations");
            if (iterationsRaw is not null && !int.TryParse(iterationsRaw, out iterations))
            {
                Console.WriteLine($"Invalid value '{iterationsRaw}' for --iterations.");
                exitCode = 1;
                return true;
            }

            long sumN = 5_000_000;
            var sumRaw = TryGetOptionValue(args, "--sum-n");
            if (sumRaw is not null && !long.TryParse(sumRaw, out sumN))
            {
                Console.WriteLine($"Invalid value '{sumRaw}' for --sum-n.");
                exitCode = 1;
                return true;
            }

            var primeN = 30_000;
            var primeRaw = TryGetOptionValue(args, "--prime-n");
            if (primeRaw is not null && !int.TryParse(primeRaw, out primeN))
            {
                Console.WriteLine($"Invalid value '{primeRaw}' for --prime-n.");
                exitCode = 1;
                return true;
            }

            var matrixN = 48;
            var matrixRaw = TryGetOptionValue(args, "--matrix-n");
            if (matrixRaw is not null && !int.TryParse(matrixRaw, out matrixN))
            {
                Console.WriteLine($"Invalid value '{matrixRaw}' for --matrix-n.");
                exitCode = 1;
                return true;
            }

            if (iterations <= 0 || sumN <= 0 || primeN <= 1 || matrixN <= 0)
            {
                Console.WriteLine("Benchmark kernel options must be positive. --prime-n must be greater than 1.");
                exitCode = 1;
                return true;
            }

            var compilationTargetRaw = TryGetOptionValue(args, "--compilation-target");
            if (!TryNormalizeCompilationTarget(compilationTargetRaw ?? "bytecode", out var compilationTarget, out var compilationTargetError))
            {
                Console.WriteLine(compilationTargetError);
                exitCode = 1;
                return true;
            }

            var executionMode = args.Contains("--native", StringComparer.Ordinal)
                ? OafKernelExecutionMode.NativeBinary
                : args.Contains("--tiered", StringComparer.Ordinal)
                    ? OafKernelExecutionMode.BytecodeVmTieredNative
                    : OafKernelExecutionMode.BytecodeVm;

            if (executionMode == OafKernelExecutionMode.NativeBinary && !OafNativeKernelExecutor.IsNativeCompilerAvailable())
            {
                Console.WriteLine("No C compiler found for --native kernel execution. Install a compiler or unset --native.");
                exitCode = 1;
                return true;
            }

            var results = OafKernelBenchmarkRunner.Run(iterations, sumN, primeN, matrixN, executionMode, compilationTarget);
            OafKernelBenchmarkRunner.PrintCsv(Console.Out, results);
            exitCode = 0;
            return true;
        }

        default:
            return false;
    }
}

static string[] NormalizePackageCommandAliases(string[] args)
{
    if (args.Length == 0)
    {
        return args;
    }

    var command = args[0];
    switch (command)
    {
        case "add":
            return BuildPackageAliasArgs("--pkg-add", args, supportsOptionalPackageKeyword: true);
        case "remove":
            return BuildPackageAliasArgs("--pkg-remove", args, supportsOptionalPackageKeyword: true);
        case "restore":
            return BuildPackageAliasArgs("--pkg-install", args, supportsOptionalPackageKeyword: false);
        case "verify":
            return BuildPackageAliasArgs("--pkg-verify", args, supportsOptionalPackageKeyword: false);
        default:
            return args;
    }
}

static string[] BuildPackageAliasArgs(string targetCommand, string[] args, bool supportsOptionalPackageKeyword)
{
    if (args.Length == 1)
    {
        return [targetCommand];
    }

    var argumentStart = 1;
    if (supportsOptionalPackageKeyword && string.Equals(args[1], "package", StringComparison.Ordinal))
    {
        argumentStart = 2;
    }

    var extraArguments = args.Length - argumentStart;
    var mappedArgs = new string[Math.Max(extraArguments + 1, 1)];
    mappedArgs[0] = targetCommand;
    if (extraArguments <= 0)
    {
        return mappedArgs;
    }

    Array.Copy(args, argumentStart, mappedArgs, 1, extraArguments);
    return mappedArgs;
}

static string? TryGetOptionValue(string[] args, string optionName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.Ordinal))
        {
            return args[i + 1];
        }
    }

    return null;
}

static bool TryParseBenchmarkStatistic(string value, out BenchmarkStatistic statistic)
{
    statistic = value.ToLowerInvariant() switch
    {
        "mean" => BenchmarkStatistic.Mean,
        "median" => BenchmarkStatistic.Median,
        "p95" => BenchmarkStatistic.P95,
        _ => BenchmarkStatistic.Mean
    };

    return value.Equals("mean", StringComparison.OrdinalIgnoreCase)
        || value.Equals("median", StringComparison.OrdinalIgnoreCase)
        || value.Equals("p95", StringComparison.OrdinalIgnoreCase);
}

static bool TryParsePerBenchmarkRatioOverrides(
    string[] args,
    out IReadOnlyDictionary<string, double>? overrides,
    out string? error)
{
    var values = GetOptionValues(args, "--max-ratio-for");
    if (values.Count == 0)
    {
        overrides = null;
        error = null;
        return true;
    }

    var parsed = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (var value in values)
    {
        var separatorIndex = value.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            overrides = null;
            error = $"Invalid --max-ratio-for value '{value}'. Expected <benchmark>=<value>.";
            return false;
        }

        var benchmark = value[..separatorIndex].Trim();
        var thresholdText = value[(separatorIndex + 1)..].Trim();
        if (benchmark.Length == 0)
        {
            overrides = null;
            error = $"Invalid --max-ratio-for value '{value}'. Benchmark name cannot be empty.";
            return false;
        }

        if (!double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) || threshold <= 0)
        {
            overrides = null;
            error = $"Invalid --max-ratio-for value '{value}'. Threshold must be a positive number.";
            return false;
        }

        parsed[benchmark] = threshold;
    }

    overrides = parsed;
    error = null;
    return true;
}

static IReadOnlyList<string> GetOptionValues(string[] args, string optionName)
{
    var values = new List<string>();
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.Ordinal))
        {
            values.Add(args[i + 1]);
        }
    }

    return values;
}

enum SdkRuntimeMode
{
    Vm,
    Native
}

sealed class SdkCommandOptions
{
    public string? Input { get; set; }

    public string? OutputPath { get; set; }

    public string Runtime { get; set; } = "vm";

    public bool RuntimeProvided { get; set; }

    public string CompilationTarget { get; set; } = "bytecode";

    public bool ShowAst { get; set; }

    public bool ShowIr { get; set; }

    public bool ShowBytecode { get; set; }

    public bool ShowMlir { get; set; }
}
