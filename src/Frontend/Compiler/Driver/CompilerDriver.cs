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

    public CompilationResult CompileSource(string source)
    {
        source ??= string.Empty;

        if (_enableCompilationCache && _compilationCache.TryGetValue(source, out var cached))
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
        optimizationPipeline.AddPass(new DeadTemporaryEliminationPass());
        optimizationPipeline.Run(irModule);

        var bytecodeGenerator = new BytecodeGenerator();
        var bytecodeProgram = bytecodeGenerator.Generate(irModule);

        var result = new CompilationResult(syntaxTree, diagnostics.Diagnostics.ToList(), typeChecker.Symbols, irModule, bytecodeProgram);
        if (_enableCompilationCache && _cacheCapacity > 0)
        {
            AddToCompilationCache(source, result);
        }

        return result;
    }

    public string PrintAst(string source)
    {
        var parser = new Parser.Parser(source);
        var syntaxTree = parser.ParseCompilationUnit();
        return AstPrinter.Print(syntaxTree);
    }

    private void AddToCompilationCache(string source, CompilationResult result)
    {
        if (_compilationCache.ContainsKey(source))
        {
            _compilationCache[source] = result;
            return;
        }

        if (_compilationCache.Count >= _cacheCapacity)
        {
            EvictOldestCacheEntry();
        }

        _compilationCache[source] = result;
        _cacheInsertionOrder.Enqueue(source);
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
}
