using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.Bytecode;

public static class BytecodeTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("generates_branching_bytecode_for_if", GeneratesBranchingBytecodeForIf),
            ("emits_integer_specialized_binary_instructions", EmitsIntegerSpecializedBinaryInstructions),
            ("emits_integer_const_right_binary_instruction", EmitsIntegerConstRightBinaryInstruction),
            ("fuses_integer_condition_jumps", FusesIntegerConditionJumps),
            ("executes_arithmetic_return", ExecutesArithmeticReturn),
            ("executes_loop_countdown", ExecutesLoopCountdown),
            ("executes_if_with_comma_separated_conditions", ExecutesIfWithCommaSeparatedConditions),
            ("executes_module_scoped_field_access", ExecutesModuleScopedFieldAccess),
            ("executes_match_statement", ExecutesMatchStatement),
            ("executes_gc_statement", ExecutesGcStatement),
            ("executes_counted_paralloop_with_indexed_writes", ExecutesCountedParalloopWithIndexedWrites),
            ("executes_counted_paralloop_plus_equals_reduction", ExecutesCountedParalloopPlusEqualsReduction),
            ("throw_statement_returns_failed_execution", ThrowStatementReturnsFailedExecution),
            ("executes_explicit_cast_with_truncation", ExecutesExplicitCastWithTruncation),
            ("executes_jot_statement_with_console_output", ExecutesJotStatementWithConsoleOutput),
            ("executes_http_get_intrinsic_with_mock_transport", ExecutesHttpGetIntrinsicWithMockTransport),
            ("executes_http_send_intrinsic_with_mock_transport", ExecutesHttpSendIntrinsicWithMockTransport),
            ("captures_http_response_metadata_intrinsics", CapturesHttpResponseMetadataIntrinsics),
            ("executes_http_header_and_query_helpers", ExecutesHttpHeaderAndQueryHelpers),
            ("executes_http_client_session_intrinsics_with_mock_transport", ExecutesHttpClientSessionIntrinsicsWithMockTransport),
            ("executes_http_client_retry_and_counters_with_mock_transport", ExecutesHttpClientRetryAndCountersWithMockTransport),
            ("reports_http_client_configure_proxy_validation", ReportsHttpClientConfigureProxyValidation),
            ("reports_http_client_send_for_missing_client", ReportsHttpClientSendForMissingClient),
            ("reports_invalid_http_send_headers_format", ReportsInvalidHttpSendHeadersFormat),
            ("executes_array_index_assignment", ExecutesArrayIndexAssignment),
            ("returns_boolean_from_fast_path", ReturnsBooleanFromFastPath),
            ("returns_char_from_fast_path", ReturnsCharFromFastPath)
        ];
    }

    private static void GeneratesBranchingBytecodeForIf()
    {
        const string source = "flux flag = 1; loop flag > 0 => flag -= 1; if flag > 0 => return 1; -> return 2;";
        var result = Compile(source);

        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode is BytecodeOpCode.JumpIfTrue
                or BytecodeOpCode.JumpIfBinaryIntTrue
                or BytecodeOpCode.JumpIfBinaryIntConstRightTrue),
            "Expected conditional jump instruction.");
        TestAssertions.True(instructions.Any(i => i.OpCode == BytecodeOpCode.Jump), "Expected jump instruction.");
    }

    private static void EmitsIntegerSpecializedBinaryInstructions()
    {
        const string source = "flux x = 3; flux y = 2; loop x > 0 => { y += x; x -= 1; } return y * x;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.BinaryInt),
            "Expected integer-specialized binary instruction.");
    }

    private static void EmitsIntegerConstRightBinaryInstruction()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; x += 2; return x;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.BinaryIntConstRight),
            "Expected integer const-right binary instruction.");
    }

    private static void FusesIntegerConditionJumps()
    {
        const string source = "flux i = 10; loop i > 0 => i -= 1; return i;";
        var result = Compile(source);
        TestAssertions.False(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        var instructions = result.BytecodeProgram.Functions[0].Instructions;
        TestAssertions.True(
            instructions.Any(i => i.OpCode == BytecodeOpCode.JumpIfBinaryIntTrue
                || i.OpCode == BytecodeOpCode.JumpIfBinaryIntConstRightTrue),
            "Expected fused integer condition jump instruction.");
    }

    private static void ExecutesArithmeticReturn()
    {
        const string source = "return 1 + 2 * 3;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(7L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesLoopCountdown()
    {
        const string source = "flux x = 3; loop x > 0 => x -= 1; return x;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(0L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesIfWithCommaSeparatedConditions()
    {
        const string source = "flux a = 3; flux b = 1; if a > 2, b < 2 => return 1; -> return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesModuleScopedFieldAccess()
    {
        const string source = "module app.core; struct Point [int x, int y]; p = Point[3, 4]; return p.x + p.y;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(7L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesMatchStatement()
    {
        const string source = "flux value = 2; value match => 1 -> value = 10; 2 -> value = 20; -> value = 0;;; return value;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(20L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesGcStatement()
    {
        const string source = "flux total = 1; gc => { total += 4; } return total;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(5L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesCountedParalloopWithIndexedWrites()
    {
        const string source = """
            flux values = [0, 0, 0, 0, 0, 0, 0, 0];
            paralloop 8, i => {
                values[i] = (i + 1) * (i + 1);
            }

            flux idx = 0;
            flux sum = 0;
            loop idx < 8 => {
                sum += values[idx];
                idx += 1;
            }

            return sum;
            """;
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(204L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesCountedParalloopPlusEqualsReduction()
    {
        const string source = """
            flux total = 0;
            paralloop 8, i => {
                total += i + 1;
            }

            return total;
            """;
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(36L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ThrowStatementReturnsFailedExecution()
    {
        const string source = "throw \"OperationFailed\", \"Division by zero\"; return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.False(execution.Success, "Expected throw statement to fail execution.");
        var error = execution.ErrorMessage ?? string.Empty;
        TestAssertions.True(error.Contains("OperationFailed", StringComparison.Ordinal), "Expected thrown error name in runtime message.");
        TestAssertions.True(error.Contains("Division by zero", StringComparison.Ordinal), "Expected thrown error detail in runtime message.");
    }

    private static void ExecutesExplicitCastWithTruncation()
    {
        const string source = "float f = 3.9; int i = (int)f; return i;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(3L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesJotStatementWithConsoleOutput()
    {
        const string source = "Jot(42); Jot(\"ok\"); return 0;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            var execution = vm.Execute(result.BytecodeProgram);
            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(0L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        TestAssertions.True(output.Contains("42\n", StringComparison.Ordinal), "Expected Jot to print integer value.");
        TestAssertions.True(output.Contains("ok\n", StringComparison.Ordinal), "Expected Jot to print string value.");
    }

    private static void ExecutesHttpGetIntrinsicWithMockTransport()
    {
        const string mockVariable = "OAF_HTTP_MOCK_GET";
        Environment.SetEnvironmentVariable(mockVariable, "200|ok|");

        try
        {
            const string source = """
            flux body = HttpGet["https://nominatim.openstreetmap.org/search?addressdetails=1&q=bakery+in+berlin+wedding&format=jsonv2&limit=1"];
            flux status = HttpLastStatus[];
            flux err = HttpLastError[];
            if status == 200, body == "ok", err == "" => return 1;
            return 0;
            """;

            var result = Compile(source);
            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);

            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(mockVariable, null);
        }
    }

    private static void ExecutesHttpSendIntrinsicWithMockTransport()
    {
        const string mockVariable = "OAF_HTTP_MOCK_SEND";
        Environment.SetEnvironmentVariable(mockVariable, "201|created|");

        try
        {
            const string source = """
            enum Method => Get, Post;
            flux body = HttpSend["https://nominatim.openstreetmap.org/search?format=jsonv2", Method.Post, "name=oaf", 8000, "Accept: application/json\nX-Trace: mock"];
            flux status = HttpLastStatus[];
            flux err = HttpLastError[];
            if status == 201, body == "created", err == "" => return 1;
            return 0;
            """;

            var result = Compile(source);
            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);

            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(mockVariable, null);
        }
    }

    private static void CapturesHttpResponseMetadataIntrinsics()
    {
        const string mockVariable = "OAF_HTTP_MOCK_SEND";
        Environment.SetEnvironmentVariable(
            mockVariable,
            "202|accepted||Accepted|application/json|Server: mock\\nContent-Type: application/json\\nX-Trace: abc123");

        try
        {
            const string source = """
            enum Method => Get, Post;
            flux body = HttpSend["https://nominatim.openstreetmap.org/search?format=jsonv2", Method.Get, "", 8000, "Accept: application/json"];
            flux status = HttpLastStatus[];
            flux err = HttpLastError[];
            flux reason = HttpLastReason[];
            flux contentType = HttpLastContentType[];
            flux headers = HttpLastHeaders[];
            flux server = HttpLastHeader["Server"];
            flux trace = HttpLastHeader["X-Trace"];
            flux body2 = HttpLastBody[];

            if status == 202, err == "", reason == "Accepted", contentType == "application/json", body == "accepted", body2 == "accepted", server == "mock", trace == "abc123" => return 1;
            return 0;
            """;

            var result = Compile(source);
            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);

            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(mockVariable, null);
        }
    }

    private static void ExecutesHttpHeaderAndQueryHelpers()
    {
        const string source = """
            flux headers = "";
            headers = HttpHeader[headers, "Accept", "application/json"];
            headers = HttpHeader[headers, "X-Trace", "abc123"];

            flux endpoint = "https://example.com/search";
            endpoint = HttpQuery[endpoint, "q", "bakery in berlin"];
            endpoint = HttpQuery[endpoint, "city", "berlin"];
            flux encoded = HttpUrlEncode["a+b c"];

            if headers == "Accept: application/json\nX-Trace: abc123",
               endpoint == "https://example.com/search?q=bakery%20in%20berlin&city=berlin",
               encoded == "a%2Bb%20c" => return 1;
            return 0;
            """;

        var result = Compile(source);
        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesHttpClientSessionIntrinsicsWithMockTransport()
    {
        const string mockVariable = "OAF_HTTP_MOCK_SEND";
        Environment.SetEnvironmentVariable(
            mockVariable,
            "202|accepted||Accepted|application/json|Server: mock\\nContent-Type: application/json\\nX-Trace: abc123");

        try
        {
            const string source = """
                enum Method => Get, Post;
                flux client = HttpClientOpen["https://api.example.com"];
                flux cfg = HttpClientConfigure[client, 9000, true, 6, "oaf-http-test/1.0"];
                flux defs = HttpClientDefaultHeaders[client, "Authorization: Bearer token\\nCookie: test=1"];
                flux body = HttpClientSend[client, "/search?format=jsonv2", Method.Get, "", "Accept: application/json"];
                flux status = HttpLastStatus[];
                flux reason = HttpLastReason[];
                flux server = HttpLastHeader["Server"];
                flux trace = HttpLastHeader["X-Trace"];
                flux close = HttpClientClose[client];

                if cfg == 1, defs == 1, close == 1, status == 202, reason == "Accepted", server == "mock", trace == "abc123", body == "accepted" => return 1;
                return 0;
                """;

            var result = Compile(source);
            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);

            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(mockVariable, null);
        }
    }

    private static void ExecutesHttpClientRetryAndCountersWithMockTransport()
    {
        const string mockVariable = "OAF_HTTP_MOCK_SEND";
        Environment.SetEnvironmentVariable(
            mockVariable,
            "503|unavailable||Service Unavailable|text/plain|Server: mock");

        try
        {
            const string source = """
                enum Method => Get, Post;
                flux client = HttpClientOpen["https://api.example.com"];
                flux retryCfg = HttpClientConfigureRetry[client, 2, 0];
                flux proxyCfg = HttpClientConfigureProxy[client, ""];
                flux body = HttpClientSend[client, "/search", Method.Get, "", ""];
                flux status = HttpLastStatus[];
                flux sentCount = HttpClientRequestsSent[client];
                flux retryCount = HttpClientRetriesUsed[client];
                flux close = HttpClientClose[client];

                if retryCfg == 1, proxyCfg == 1, status == 503, sentCount == 3, retryCount == 2, close == 1, body == "unavailable" => return 1;
                return 0;
                """;

            var result = Compile(source);
            var vm = new BytecodeVirtualMachine();
            var execution = vm.Execute(result.BytecodeProgram);

            TestAssertions.True(execution.Success, execution.ErrorMessage);
            TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(mockVariable, null);
        }
    }

    private static void ReportsHttpClientConfigureProxyValidation()
    {
        const string source = """
            flux client = HttpClientOpen["https://api.example.com"];
            flux bad = HttpClientConfigureProxy[client, "not-a-valid-proxy"];
            flux sent = HttpClientRequestsSent[client];
            flux retries = HttpClientRetriesUsed[client];
            flux close = HttpClientClose[client];
            if bad == 0, sent == 0, retries == 0, close == 1 => return 1;
            return 0;
            """;

        var result = Compile(source);
        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ReportsHttpClientSendForMissingClient()
    {
        const string source = """
            enum Method => Get, Post;
            flux body = HttpClientSend[9999, "/search", Method.Get, "", ""];
            flux status = HttpLastStatus[];
            flux error = HttpLastError[];
            if status == 0, body == "", error == "HttpClientSend client is not open." => return 1;
            return 0;
            """;

        var result = Compile(source);
        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ReportsInvalidHttpSendHeadersFormat()
    {
        const string source = """
            enum Method => Get, Post;
            flux body = HttpSend["https://example.com", Method.Get, "", 1000, "BadHeader"];
            flux status = HttpLastStatus[];
            flux err = HttpLastError[];
            if status == 0, err == "HttpSend headers must use 'Name: Value' per line.", body == "" => return 1;
            return 0;
            """;

        var result = Compile(source);
        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(1L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ExecutesArrayIndexAssignment()
    {
        const string source = "flux values = [1, 2, 3]; values[1] = 9; return values[1];";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.Equal(9L, execution.ReturnValue as long? ?? Convert.ToInt64(execution.ReturnValue));
    }

    private static void ReturnsBooleanFromFastPath()
    {
        const string source = "return 2 < 3;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.True(execution.ReturnValue is bool, "Expected bool return value.");
        TestAssertions.Equal(true, (bool)execution.ReturnValue!);
    }

    private static void ReturnsCharFromFastPath()
    {
        const string source = "char c = (char)65; return c;";
        var result = Compile(source);

        var vm = new BytecodeVirtualMachine();
        var execution = vm.Execute(result.BytecodeProgram);

        TestAssertions.True(execution.Success, execution.ErrorMessage);
        TestAssertions.True(execution.ReturnValue is char, "Expected char return value.");
        TestAssertions.Equal('A', (char)execution.ReturnValue!);
    }

    private static CompilationResult Compile(string source)
    {
        var driver = new CompilerDriver();
        return driver.CompileSource(source);
    }
}
