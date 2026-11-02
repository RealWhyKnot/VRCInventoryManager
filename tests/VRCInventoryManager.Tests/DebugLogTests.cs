using System.IO;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

public sealed class DebugLogTests
{
    [Fact]
    public void ErrorWithMultilineExceptionAddsExactlyOneLine()
    {
        string root = TestFiles.CreateTempDirectory();
        string path = Path.Combine(root, "VRCInventoryManager.debug.log");
        try
        {
            using DebugLog log = DebugLog.TryCreate(path, reset: true, new FixedTimeProvider())
                ?? throw new InvalidOperationException("Could not create debug log.");
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(ThrowMultilineException);
            Assert.NotNull(exception.StackTrace);
            int previousLines = ReadLogLines(path).Length;
            log.Error("operation failed\r\nretry later", exception);
            string[] lines = ReadLogLines(path);
            Assert.Equal(previousLines + 1, lines.Length);
            Assert.StartsWith("1970-01-01T00:00:00.0000000+00:00 [ERROR] ", lines[^1], StringComparison.Ordinal);
            Assert.Contains(@"operation failed\r\nretry later", lines[^1], StringComparison.Ordinal);
            Assert.Contains("System.InvalidOperationException", lines[^1], StringComparison.Ordinal);
            Assert.Contains(@"first\r\nsecond\nthird\rfourth", lines[^1], StringComparison.Ordinal);
            Assert.Contains(nameof(ThrowMultilineException), lines[^1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteEscapesEveryCarriageReturnAndLineFeed()
    {
        string root = TestFiles.CreateTempDirectory();
        string path = Path.Combine(root, "VRCInventoryManager.debug.log");
        try
        {
            using DebugLog log = DebugLog.TryCreate(path, reset: true, new FixedTimeProvider())
                ?? throw new InvalidOperationException("Could not create debug log.");
            int previousLines = ReadLogLines(path).Length;
            log.Write("INFO", "first\r\nsecond\nthird\rfourth");
            string[] lines = ReadLogLines(path);
            Assert.Equal(previousLines + 1, lines.Length);
            Assert.Equal(@"1970-01-01T00:00:00.0000000+00:00 [INFO] first\r\nsecond\nthird\rfourth", lines[^1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] ReadLogLines(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);
        List<string> lines = [];
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }

    private static void ThrowMultilineException() => throw new InvalidOperationException("first\r\nsecond\nthird\rfourth");
}
