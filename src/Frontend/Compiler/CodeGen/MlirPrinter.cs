using System.Globalization;
using System.Text;

namespace Oaf.Frontend.Compiler.CodeGen;

public static class MlirPrinter
{
    public static string Print(IrModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var emitter = new MlirEmitter();
        return emitter.EmitModule(module);
    }

    private sealed class MlirEmitter
    {
        private readonly StringBuilder _builder = new();
        private readonly Dictionary<string, string> _temporaries = new(StringComparer.Ordinal);
        private int _valueCounter;

        public string EmitModule(IrModule module)
        {
            _builder.AppendLine("module {");
            foreach (var function in module.Functions)
            {
                EmitFunction(function);
            }

            _builder.AppendLine("}");
            return _builder.ToString();
        }

        private void EmitFunction(IrFunction function)
        {
            _temporaries.Clear();
            _valueCounter = 0;

            _builder.Append("  func.func @");
            _builder.Append(SanitizeSymbol(function.Name));
            _builder.AppendLine("() {");

            foreach (var block in function.Blocks)
            {
                _builder.Append("  ^");
                _builder.Append(SanitizeBlockLabel(block.Label));
                _builder.AppendLine(":");

                foreach (var instruction in block.Instructions)
                {
                    EmitInstruction(instruction);
                }
            }

            _builder.AppendLine("  }");
        }

        private void EmitInstruction(IrInstruction instruction)
        {
            switch (instruction)
            {
                case IrAssignInstruction assign:
                    EmitAssign(assign);
                    return;
                case IrUnaryInstruction unary:
                    EmitUnary(unary);
                    return;
                case IrBinaryInstruction binary:
                    EmitBinary(binary);
                    return;
                case IrCastInstruction cast:
                    EmitCast(cast);
                    return;
                case IrBranchInstruction branch:
                    EmitBranch(branch);
                    return;
                case IrJumpInstruction jump:
                    EmitJump(jump);
                    return;
                case IrReturnInstruction ret:
                    EmitReturn(ret);
                    return;
                default:
                    EmitComment($"unsupported instruction: {instruction.ToDisplayString()}");
                    return;
            }
        }

        private void EmitAssign(IrAssignInstruction assign)
        {
            var sourceOperand = MaterializeValue(assign.Source);
            if (assign.Destination is IrTemporaryValue destinationTemporary)
            {
                var destination = EnsureTemporarySsa(destinationTemporary.Name);
                _builder.Append("    ");
                _builder.Append(destination);
                _builder.Append(" = \"oaf.copy\"(");
                _builder.Append(sourceOperand);
                _builder.Append(") : (");
                _builder.Append(RenderType(assign.Source.Type));
                _builder.Append(") -> ");
                _builder.Append(RenderType(destinationTemporary.Type));
                _builder.AppendLine();
                return;
            }

            if (assign.Destination is IrVariableValue destinationVariable)
            {
                _builder.Append("    \"oaf.store\"(");
                _builder.Append(sourceOperand);
                _builder.Append(") {name = \"");
                _builder.Append(EscapeAttributeString(destinationVariable.Name));
                _builder.Append("\"} : (");
                _builder.Append(RenderType(assign.Source.Type));
                _builder.AppendLine(") -> ()");
                return;
            }

            EmitComment($"unsupported assign destination: {assign.Destination.DisplayText}");
        }

        private void EmitUnary(IrUnaryInstruction unary)
        {
            var destination = EnsureTemporarySsa(unary.Destination.Name);
            var operand = MaterializeValue(unary.Operand);
            _builder.Append("    ");
            _builder.Append(destination);
            _builder.Append(" = \"oaf.unary\"(");
            _builder.Append(operand);
            _builder.Append(") {op = \"");
            _builder.Append(unary.Operation.ToString().ToLowerInvariant());
            _builder.Append("\"} : (");
            _builder.Append(RenderType(unary.Operand.Type));
            _builder.Append(") -> ");
            _builder.Append(RenderType(unary.Destination.Type));
            _builder.AppendLine();
        }

        private void EmitBinary(IrBinaryInstruction binary)
        {
            var destination = EnsureTemporarySsa(binary.Destination.Name);
            var left = MaterializeValue(binary.Left);
            var right = MaterializeValue(binary.Right);
            _builder.Append("    ");
            _builder.Append(destination);
            _builder.Append(" = \"oaf.binary\"(");
            _builder.Append(left);
            _builder.Append(", ");
            _builder.Append(right);
            _builder.Append(") {op = \"");
            _builder.Append(binary.Operation.ToString().ToLowerInvariant());
            _builder.Append("\"} : (");
            _builder.Append(RenderType(binary.Left.Type));
            _builder.Append(", ");
            _builder.Append(RenderType(binary.Right.Type));
            _builder.Append(") -> ");
            _builder.Append(RenderType(binary.Destination.Type));
            _builder.AppendLine();
        }

        private void EmitCast(IrCastInstruction cast)
        {
            var destination = EnsureTemporarySsa(cast.Destination.Name);
            var source = MaterializeValue(cast.Source);
            _builder.Append("    ");
            _builder.Append(destination);
            _builder.Append(" = \"oaf.cast\"(");
            _builder.Append(source);
            _builder.Append(") : (");
            _builder.Append(RenderType(cast.Source.Type));
            _builder.Append(") -> ");
            _builder.Append(RenderType(cast.TargetType));
            _builder.AppendLine();
        }

        private void EmitBranch(IrBranchInstruction branch)
        {
            var condition = MaterializeConditionValue(branch.Condition);
            _builder.Append("    \"oaf.cond_br\"(");
            _builder.Append(condition);
            _builder.Append(") {true = \"");
            _builder.Append(EscapeAttributeString(SanitizeBlockLabel(branch.TrueLabel)));
            _builder.Append("\", false = \"");
            _builder.Append(EscapeAttributeString(SanitizeBlockLabel(branch.FalseLabel)));
            _builder.Append("\"} : (i1) -> ()");
            _builder.AppendLine();
        }

        private void EmitJump(IrJumpInstruction jump)
        {
            _builder.Append("    \"oaf.br\"() {target = \"");
            _builder.Append(EscapeAttributeString(SanitizeBlockLabel(jump.TargetLabel)));
            _builder.AppendLine("\"} : () -> ()");
        }

        private void EmitReturn(IrReturnInstruction ret)
        {
            if (ret.Value is null)
            {
                _builder.AppendLine("    \"oaf.return\"() : () -> ()");
                return;
            }

            var value = MaterializeValue(ret.Value);
            _builder.Append("    \"oaf.return\"(");
            _builder.Append(value);
            _builder.Append(") : (");
            _builder.Append(RenderType(ret.Value.Type));
            _builder.AppendLine(") -> ()");
        }

        private string MaterializeConditionValue(IrValue value)
        {
            var condition = MaterializeValue(value);
            if (value.Type.Kind == IrTypeKind.Bool)
            {
                return condition;
            }

            var truthyResult = AllocateTemporarySsa("truthy");
            _builder.Append("    ");
            _builder.Append(truthyResult);
            _builder.Append(" = \"oaf.truthy\"(");
            _builder.Append(condition);
            _builder.Append(") : (");
            _builder.Append(RenderType(value.Type));
            _builder.AppendLine(") -> i1");
            return truthyResult;
        }

        private string MaterializeValue(IrValue value)
        {
            switch (value)
            {
                case IrTemporaryValue temporary:
                    return EnsureTemporarySsa(temporary.Name);
                case IrVariableValue variable:
                    return EmitVariableLoad(variable);
                case IrConstantValue constant:
                    return EmitConstant(constant);
                default:
                    return EmitUnknownValue(value);
            }
        }

        private string EmitVariableLoad(IrVariableValue variable)
        {
            var result = AllocateTemporarySsa(SanitizeSymbol(variable.Name));
            _builder.Append("    ");
            _builder.Append(result);
            _builder.Append(" = \"oaf.load\"() {name = \"");
            _builder.Append(EscapeAttributeString(variable.Name));
            _builder.Append("\"} : () -> ");
            _builder.Append(RenderType(variable.Type));
            _builder.AppendLine();
            return result;
        }

        private string EmitConstant(IrConstantValue constant)
        {
            var result = AllocateTemporarySsa("const");
            _builder.Append("    ");
            _builder.Append(result);
            _builder.Append(" = \"oaf.constant\"() {value = ");
            _builder.Append(RenderConstantAttributeValue(constant.Value));
            _builder.Append("} : () -> ");
            _builder.Append(RenderType(constant.Type));
            _builder.AppendLine();
            return result;
        }

        private string EmitUnknownValue(IrValue value)
        {
            var result = AllocateTemporarySsa("unknown");
            _builder.Append("    ");
            _builder.Append(result);
            _builder.Append(" = \"oaf.unknown\"() {display = \"");
            _builder.Append(EscapeAttributeString(value.DisplayText));
            _builder.Append("\"} : () -> ");
            _builder.Append(RenderType(value.Type));
            _builder.AppendLine();
            return result;
        }

        private string EnsureTemporarySsa(string name)
        {
            if (_temporaries.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var created = AllocateTemporarySsa(name);
            _temporaries.Add(name, created);
            return created;
        }

        private string AllocateTemporarySsa(string seed)
        {
            var sanitized = SanitizeSymbol(seed);
            var ssaName = $"%{sanitized}_{_valueCounter}";
            _valueCounter++;
            return ssaName;
        }

        private static string RenderType(IrType type)
        {
            return type.Kind switch
            {
                IrTypeKind.Void => "none",
                IrTypeKind.Int => "i64",
                IrTypeKind.Float => "f64",
                IrTypeKind.Bool => "i1",
                IrTypeKind.Char => "i32",
                IrTypeKind.String => "!oaf.string",
                _ => "!oaf.unknown"
            };
        }

        private static string RenderConstantAttributeValue(object? value)
        {
            if (value is null)
            {
                return "unit";
            }

            return value switch
            {
                bool boolean => boolean ? "true" : "false",
                string text => $"\"{EscapeAttributeString(text)}\"",
                char ch => ((int)ch).ToString(CultureInfo.InvariantCulture),
                float single => single.ToString("G17", CultureInfo.InvariantCulture),
                double dbl => dbl.ToString("G17", CultureInfo.InvariantCulture),
                decimal dec => dec.ToString(CultureInfo.InvariantCulture),
                sbyte or byte or short or ushort or int or uint or long or ulong
                    => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
                _ => $"\"{EscapeAttributeString(value.ToString() ?? string.Empty)}\""
            };
        }

        private static string SanitizeBlockLabel(string label)
        {
            return SanitizeSymbol(label);
        }

        private static string SanitizeSymbol(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "value";
            }

            var builder = new StringBuilder(value.Length);
            var first = value[0];
            if (char.IsLetter(first) || first == '_')
            {
                builder.Append(first);
            }
            else
            {
                builder.Append('_');
                if (char.IsLetterOrDigit(first))
                {
                    builder.Append(first);
                }
            }

            for (var i = 1; i < value.Length; i++)
            {
                var ch = value[i];
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            return builder.ToString();
        }

        private static string EscapeAttributeString(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }

        private void EmitComment(string message)
        {
            _builder.Append("    // ");
            _builder.AppendLine(message);
        }
    }
}
