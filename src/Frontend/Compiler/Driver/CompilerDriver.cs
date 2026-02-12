using Oaf.Frontend.Compiler.AST;
using Oaf.Frontend.Compiler.CodeGen;
using Oaf.Frontend.Compiler.CodeGen.Bytecode;
using Oaf.Frontend.Compiler.CodeGen.Passes;
using Oaf.Frontend.Compiler.Diagnostics;
using Oaf.Frontend.Compiler.Ownership;
using Oaf.Frontend.Compiler.Parser;
using Oaf.Frontend.Compiler.TypeChecker;

namespace Oaf.Frontend.Compiler.Driver;

public sealed class CompilerDriver
{
    private readonly bool _enableCompilationCache;
    private readonly int _cacheCapacity;
    private readonly Dictionary<string, CompilationResult> _compilationCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheInsertionOrder = new();

    public CompilerDriver(bool enableCompilationCache = true, int cacheCapacity = 64)
    {
        _enableCompilationCache = enableCompilationCache;
        _cacheCapacity = Math.Max(0, cacheCapacity);
    }

    public int CacheHits { get; private set; }

    public int CacheMisses { get; private set; }

    public void ClearCache()
    {
        _compilationCache.Clear();
        _cacheInsertionOrder.Clear();
    }

    public CompilationResult CompileSource(
        string source,
        CompilerCompilationTarget compilationTarget = CompilerCompilationTarget.Bytecode)
    {
        source ??= string.Empty;
        var cacheKey = BuildCompilationCacheKey(source, compilationTarget);

        if (_enableCompilationCache && _compilationCache.TryGetValue(cacheKey, out var cached))
        {
            CacheHits++;
            return cached;
        }

        CacheMisses++;

        var parser = new Parser.Parser(source);
        var syntaxTree = parser.ParseCompilationUnit();

        var diagnostics = new DiagnosticBag();
        diagnostics.AddRange(parser.Diagnostics.Diagnostics);

        var typeChecker = new TypeChecker.TypeChecker(diagnostics);
        typeChecker.Check(syntaxTree);

        var ownershipAnalyzer = new OwnershipAnalyzer(diagnostics);
        ownershipAnalyzer.Analyze(syntaxTree);

        var lowerer = new IrLowerer();
        var irModule = lowerer.Lower(syntaxTree);

        var optimizationPipeline = new IrOptimizationPipeline();
        optimizationPipeline.AddPass(new ConstantFoldingPass());
        optimizationPipeline.AddPass(new CopyPropagationPass());
        optimizationPipeline.AddPass(new DeadStoreEliminationPass());
        optimizationPipeline.AddPass(new DeadTemporaryEliminationPass());
        for (var i = 0; i < 4; i++)
        {
            if (!optimizationPipeline.Run(irModule))
            {
                break;
            }
        }

        if (compilationTarget == CompilerCompilationTarget.Mlir)
        {
            // MLIR target is currently an internal lowering stage that still emits runnable bytecode.
            _ = MlirPrinter.Print(irModule);
        }

        var bytecodeGenerator = new BytecodeGenerator();
        var bytecodeProgram = bytecodeGenerator.Generate(irModule);

        var result = new CompilationResult(syntaxTree, diagnostics.Diagnostics.ToList(), typeChecker.Symbols, irModule, bytecodeProgram);
        if (_enableCompilationCache && _cacheCapacity > 0)
        {
            AddToCompilationCache(cacheKey, result);
        }

        return result;
    }

    public string PrintAst(string source)
    {
        var parser = new Parser.Parser(source);
        var syntaxTree = parser.ParseCompilationUnit();
        return AstPrinter.Print(syntaxTree);
    }

    private void AddToCompilationCache(string cacheKey, CompilationResult result)
    {
        if (_compilationCache.ContainsKey(cacheKey))
        {
            _compilationCache[cacheKey] = result;
            return;
        }

        if (_compilationCache.Count >= _cacheCapacity)
        {
            EvictOldestCacheEntry();
        }

        _compilationCache[cacheKey] = result;
        _cacheInsertionOrder.Enqueue(cacheKey);
    }

    private void EvictOldestCacheEntry()
    {
        while (_cacheInsertionOrder.Count > 0)
        {
            var candidate = _cacheInsertionOrder.Dequeue();
            if (_compilationCache.Remove(candidate))
            {
                return;
            }
        }
    }

    private static string BuildCompilationCacheKey(string source, CompilerCompilationTarget target)
    {
        return $"{(int)target}:{source}";
    }
}
