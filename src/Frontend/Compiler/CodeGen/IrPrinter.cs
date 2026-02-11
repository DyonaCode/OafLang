using System.Text;

namespace Oaf.Frontend.Compiler.CodeGen;

public static class IrPrinter
{
    public static string Print(IrModule module)
    {
        var builder = new StringBuilder();

        foreach (var function in module.Functions)
        {
            builder.Append("function ");
            builder.Append(function.Name);
            builder.AppendLine(":");

            foreach (var block in function.Blocks)
            {
                builder.Append("  ");
                builder.Append(block.Label);
                builder.AppendLine(":");

                foreach (var instruction in block.Instructions)
                {
                    builder.Append("    ");
                    builder.AppendLine(instruction.ToDisplayString());
                }
            }
        }

        return builder.ToString();
    }
}
