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
            ("can_disable_compilation_cache", CanDisableCompilationCache)
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
}
