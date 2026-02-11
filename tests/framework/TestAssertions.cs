namespace Oaf.Tests.Framework;

public static class TestAssertions
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
        }
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ?? $"Expected '{expected}' but found '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();

        if (expectedArray.Length != actualArray.Length)
        {
            throw new InvalidOperationException(message ?? $"Expected sequence length {expectedArray.Length} but found {actualArray.Length}.");
        }

        for (var i = 0; i < expectedArray.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expectedArray[i], actualArray[i]))
            {
                throw new InvalidOperationException(
                    message ?? $"Expected sequence element at index {i} to be '{expectedArray[i]}' but found '{actualArray[i]}'.");
            }
        }
    }
}
