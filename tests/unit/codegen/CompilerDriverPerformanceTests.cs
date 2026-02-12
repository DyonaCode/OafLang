using Oaf.Frontend.Compiler.Driver;
using Oaf.Tests.Framework;

namespace Oaf.Tests.Unit.CodeGen;

public static class CompilerDriverPerformanceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("reuses_cached_compilation_for_identical_source", ReusesCachedCompilationForIdenticalSource),
            ("can_disable_compilation_cache", CanDisableCompilationCache),
            ("cache_is_partitioned_by_compilation_target", CacheIsPartitionedByCompilationTarget)
        ];
    }

    private static void ReusesCachedCompilationForIdenticalSource()
    {
        var driver = new CompilerDriver(enableCompilationCache: true, cacheCapacity: 8);
        const string source = "flux value = 1 + 2; return value;";

        var first = driver.CompileSource(source);
        var second = driver.CompileSource(source);

        TestAssertions.True(ReferenceEquals(first, second), "Expected identical source to return cached compilation result.");
        TestAssertions.Equal(1, driver.CacheHits);
        TestAssertions.Equal(1, driver.CacheMisses);
    }

    private static void CanDisableCompilationCache()
    {
        var driver = new CompilerDriver(enableCompilationCache: false, cacheCapacity: 8);
        const string source = "flux value = 1 + 2; return value;";

        var first = driver.CompileSource(source);
        var second = driver.CompileSource(source);

        TestAssertions.False(ReferenceEquals(first, second), "Expected cache-disabled compiler to produce fresh result instances.");
        TestAssertions.Equal(0, driver.CacheHits);
        TestAssertions.Equal(2, driver.CacheMisses);
    }

    private static void CacheIsPartitionedByCompilationTarget()
    {
        var driver = new CompilerDriver(enableCompilationCache: true, cacheCapacity: 8);
        const string source = "flux value = 1 + 2; return value;";

        var bytecode = driver.CompileSource(source, CompilerCompilationTarget.Bytecode);
        var mlir = driver.CompileSource(source, CompilerCompilationTarget.Mlir);
        var bytecodeCached = driver.CompileSource(source, CompilerCompilationTarget.Bytecode);
        var mlirCached = driver.CompileSource(source, CompilerCompilationTarget.Mlir);

        TestAssertions.False(ReferenceEquals(bytecode, mlir), "Expected different compilation targets to use different cache entries.");
        TestAssertions.True(ReferenceEquals(bytecode, bytecodeCached), "Expected bytecode target to hit cache.");
        TestAssertions.True(ReferenceEquals(mlir, mlirCached), "Expected MLIR target to hit cache.");
        TestAssertions.Equal(2, driver.CacheHits);
        TestAssertions.Equal(2, driver.CacheMisses);
    }
}
