using System.Text;
using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.Parser;

namespace Oaf.Tooling.Documentation;

public static class OafDocumentationGenerator
{
    public static string GenerateMarkdown(string source, string moduleName)
    {
        var parser = new Parser(source);
        var unit = parser.ParseCompilationUnit();

        var builder = new StringBuilder();
        builder.AppendLine($"# {moduleName}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine($"- Statements: {unit.Statements.Count}");
        builder.AppendLine($"- Diagnostics: {parser.Diagnostics.Diagnostics.Count}");
        builder.AppendLine();

        var typeDeclarations = unit.Statements.OfType<TypeDeclarationStatementSyntax>().ToArray();
        if (typeDeclarations.Length > 0)
        {
            builder.AppendLine("## Types");
            builder.AppendLine();

            foreach (var typeDeclaration in typeDeclarations)
            {
                RenderTypeDeclaration(builder, typeDeclaration);
            }
        }

        var globals = unit.Statements.OfType<VariableDeclarationStatementSyntax>().ToArray();
        if (globals.Length > 0)
        {
            builder.AppendLine("## Global Variables");
            builder.AppendLine();
            foreach (var global in globals)
            {
                var declaredType = global.DeclaredType is null
                    ? "inferred"
                    : FormatTypeReference(global.DeclaredType);
                builder.AppendLine($"- `{global.Identifier}`: `{declaredType}`");
            }
            builder.AppendLine();
        }

        if (parser.Diagnostics.Diagnostics.Count > 0)
        {
            builder.AppendLine("## Diagnostics");
            builder.AppendLine();
            foreach (var diagnostic in parser.Diagnostics.Diagnostics)
            {
                builder.AppendLine($"- {diagnostic}");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static bool GenerateFromPath(string inputPath, string? outputPath, out string message)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            message = "Input path is required.";
            return false;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullInputPath))
        {
            return GenerateForFile(fullInputPath, outputPath, out message);
        }

        if (Directory.Exists(fullInputPath))
        {
            return GenerateForDirectory(fullInputPath, outputPath, out message);
        }

        message = $"Input path '{fullInputPath}' does not exist.";
        return false;
    }

    private static bool GenerateForFile(string fullInputPath, string? outputPath, out string message)
    {
        var source = File.ReadAllText(fullInputPath);
        var moduleName = Path.GetFileNameWithoutExtension(fullInputPath);
        var markdown = GenerateMarkdown(source, moduleName);

        var targetPath = outputPath is null
            ? Path.ChangeExtension(fullInputPath, ".md")
            : Path.GetFullPath(outputPath);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(targetPath, markdown);
        message = $"Generated documentation at '{targetPath}'.";
        return true;
    }

    private static bool GenerateForDirectory(string fullInputPath, string? outputPath, out string message)
    {
        var files = Directory.GetFiles(fullInputPath, "*.oaf", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var targetDirectory = outputPath is null
            ? Path.Combine(fullInputPath, "docs")
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(targetDirectory);

        var indexBuilder = new StringBuilder();
        indexBuilder.AppendLine("# Oaf Documentation Index");
        indexBuilder.AppendLine();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var relativeInputPath = Path.GetRelativePath(fullInputPath, file);
            var moduleName = Path.GetFileNameWithoutExtension(file);
            var markdown = GenerateMarkdown(source, moduleName);
            var relativeOutputPath = Path.ChangeExtension(relativeInputPath, ".md");
            var outputFilePath = Path.Combine(targetDirectory, relativeOutputPath);
            var outputDirectory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            File.WriteAllText(outputFilePath, markdown);

            var indexPath = relativeOutputPath.Replace('\\', '/');
            indexBuilder.AppendLine($"- [{moduleName}]({indexPath})");
        }

        File.WriteAllText(Path.Combine(targetDirectory, "index.md"), indexBuilder.ToString());
        message = $"Generated {files.Length} documentation file(s) in '{targetDirectory}'.";
        return true;
    }

    private static void RenderTypeDeclaration(StringBuilder builder, TypeDeclarationStatementSyntax declaration)
    {
        var typeKind = declaration switch
        {
            StructDeclarationStatementSyntax => "Struct",
            ClassDeclarationStatementSyntax => "Class",
            EnumDeclarationStatementSyntax => "Enum",
            _ => "Type"
        };

        var typeParameters = declaration.TypeParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", declaration.TypeParameters)}>";

        builder.AppendLine($"### {typeKind} `{declaration.Name}{typeParameters}`");
        builder.AppendLine();

        switch (declaration)
        {
            case StructDeclarationStatementSyntax structDeclaration:
                if (structDeclaration.Fields.Count == 0)
                {
                    builder.AppendLine("No fields.");
                }
                else
                {
                    foreach (var field in structDeclaration.Fields)
                    {
                        builder.AppendLine($"- `{field.Name}`: `{FormatTypeReference(field.Type)}`");
                    }
                }
                break;

            case ClassDeclarationStatementSyntax classDeclaration:
                if (classDeclaration.Fields.Count == 0)
                {
                    builder.AppendLine("No fields.");
                }
                else
                {
                    foreach (var field in classDeclaration.Fields)
                    {
                        builder.AppendLine($"- `{field.Name}`: `{FormatTypeReference(field.Type)}`");
                    }
                }
                break;

            case EnumDeclarationStatementSyntax enumDeclaration:
                if (enumDeclaration.Variants.Count == 0)
                {
                    builder.AppendLine("No variants.");
                }
                else
                {
                    foreach (var variant in enumDeclaration.Variants)
                    {
                        if (variant.PayloadType is null)
                        {
                            builder.AppendLine($"- `{variant.Name}`");
                        }
                        else
                        {
                            builder.AppendLine($"- `{variant.Name}({FormatTypeReference(variant.PayloadType)})`");
                        }
                    }
                }
                break;
        }

        builder.AppendLine();
    }

    private static string FormatTypeReference(TypeReferenceSyntax typeReference)
    {
        if (typeReference.TypeArguments.Count == 0)
        {
            return typeReference.Name;
        }

        var arguments = string.Join(", ", typeReference.TypeArguments.Select(FormatTypeReference));
        return $"{typeReference.Name}<{arguments}>";
    }
}
