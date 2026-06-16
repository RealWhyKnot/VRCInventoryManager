namespace VRCInventoryManager.Tests;

internal static class TestAssert
{
    public static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
        }
    }

    public static void True(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }
    }

    public static void False(bool condition, string label) => True(!condition, label);
}
