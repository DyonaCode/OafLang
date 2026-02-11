using Oaf.Tests.Framework;
using Oaf.Tooling.Documentation;

namespace Oaf.Tests.Unit.Tooling;

public static class DocumentationGeneratorTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("generates_markdown_for_type_declarations", GeneratesMarkdownForTypeDeclarations),
            ("generates_documentation_file_from_source_path", GeneratesDocumentationFileFromSourcePath),
            ("generates_directory_docs_with_unique_relative_paths", GeneratesDirectoryDocsWithUniqueRelativePaths)
        ];
    }

    private static void GeneratesMarkdownForTypeDeclarations()
    {
        const string source = "struct Pair<T> [T left, T right]; class Person [string name]; enum Option<T> => Some(T), None; flux count = 1;";
        var markdown = OafDocumentationGenerator.GenerateMarkdown(source, "Example");

        TestAssertions.True(markdown.Contains("# Example", StringComparison.Ordinal));
        TestAssertions.True(markdown.Contains("### Struct `Pair<T>`", StringComparison.Ordinal));
        TestAssertions.True(markdown.Contains("### Class `Person`", StringComparison.Ordinal));
        TestAssertions.True(markdown.Contains("### Enum `Option<T>`", StringComparison.Ordinal));
        TestAssertions.True(markdown.Contains("`count`", StringComparison.Ordinal));
    }

    private static void GeneratesDocumentationFileFromSourcePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_docs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "sample.oaf");
            File.WriteAllText(sourcePath, "struct User [string name];");

            var outputPath = Path.Combine(root, "sample.md");
            TestAssertions.True(OafDocumentationGenerator.GenerateFromPath(sourcePath, outputPath, out _));
            TestAssertions.True(File.Exists(outputPath));

            var markdown = File.ReadAllText(outputPath);
            TestAssertions.True(markdown.Contains("### Struct `User`", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void GeneratesDirectoryDocsWithUniqueRelativePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_docs_dir_{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "src");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            var firstDirectory = Path.Combine(sourceRoot, "domain");
            var secondDirectory = Path.Combine(sourceRoot, "models");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);

            File.WriteAllText(Path.Combine(firstDirectory, "item.oaf"), "struct DomainItem [int id];");
            File.WriteAllText(Path.Combine(secondDirectory, "item.oaf"), "struct ModelItem [int id];");

            var outputDirectory = Path.Combine(root, "docs");
            TestAssertions.True(OafDocumentationGenerator.GenerateFromPath(sourceRoot, outputDirectory, out _));

            var firstOutput = Path.Combine(outputDirectory, "domain", "item.md");
            var secondOutput = Path.Combine(outputDirectory, "models", "item.md");
            TestAssertions.True(File.Exists(firstOutput), "Expected domain/item.md to be generated.");
            TestAssertions.True(File.Exists(secondOutput), "Expected models/item.md to be generated.");

            var firstMarkdown = File.ReadAllText(firstOutput);
            var secondMarkdown = File.ReadAllText(secondOutput);
            TestAssertions.True(firstMarkdown.Contains("DomainItem", StringComparison.Ordinal));
            TestAssertions.True(secondMarkdown.Contains("ModelItem", StringComparison.Ordinal));

            var indexPath = Path.Combine(outputDirectory, "index.md");
            TestAssertions.True(File.Exists(indexPath), "Expected index.md to be generated.");
            var indexMarkdown = File.ReadAllText(indexPath);
            TestAssertions.True(indexMarkdown.Contains("(domain/item.md)", StringComparison.Ordinal));
            TestAssertions.True(indexMarkdown.Contains("(models/item.md)", StringComparison.Ordinal));
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
