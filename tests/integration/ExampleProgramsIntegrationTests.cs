using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Integration;

public static class ExampleProgramsIntegrationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("all_example_programs_parse_and_typecheck", AllExampleProgramsParseAndTypecheck),
            ("all_example_programs_execute_in_bytecode_vm", AllExampleProgramsExecuteInBytecodeVm)
        ];
    }

    private static void AllExampleProgramsParseAndTypecheck()
    {
        var driver = new CompilerDriver(enableCompilationCache: false);
        foreach (var file in EnumerateExamplePrograms())
        {
            var source = File.ReadAllText(file);
            var result = driver.CompileSource(source);
            TestAssertions.True(result.Success, $"Expected example '{file}' to compile successfully.");
        }
    }

    private static void AllExampleProgramsExecuteInBytecodeVm()
    {
        var driver = new CompilerDriver(enableCompilationCache: false);
        var vm = new BytecodeVirtualMachine();

        foreach (var file in EnumerateExamplePrograms())
        {
            var source = File.ReadAllText(file);
            var result = driver.CompileSource(source);
            TestAssertions.True(result.Success, $"Expected example '{file}' to compile before execution.");

            var execution = vm.Execute(result.BytecodeProgram);
            TestAssertions.True(execution.Success, $"Expected example '{file}' to execute successfully. {execution.ErrorMessage}");
        }
    }

    private static IReadOnlyList<string> EnumerateExamplePrograms()
    {
        var root = FindRepositoryRoot();
        var examplesRoot = Path.Combine(root, "examples");
        return Directory.GetFiles(examplesRoot, "*.oaf", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oaf.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
