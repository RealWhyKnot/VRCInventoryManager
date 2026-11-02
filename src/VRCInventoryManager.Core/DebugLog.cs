using System.Reflection;
using System.Text;

namespace VRCInventoryManager.Core;

internal sealed class DebugLog : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly StreamWriter? _writer;
    private bool _isDisposed;

    private DebugLog(string? filePath, StreamWriter? writer, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        FilePath = filePath;
        _writer = writer;
    }

    public string? FilePath { get; }

    public static DebugLog Disabled { get; } = new(filePath: null, writer: null, TimeProvider.System);

    public static string GetExecutableLogPath(string? processPath = null, string? fallbackDirectory = null)
    {
        string? executablePath = string.IsNullOrWhiteSpace(processPath)
            ? Environment.ProcessPath
            : processPath;

        string? directory = !string.IsNullOrWhiteSpace(executablePath)
            ? Path.GetDirectoryName(executablePath)
            : null;

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = string.IsNullOrWhiteSpace(fallbackDirectory)
                ? AppContext.BaseDirectory
                : fallbackDirectory;
        }

        return Path.Combine(directory, "VRCInventoryManager.debug.log");
    }

    public static DebugLog CreateNearExecutable(TimeProvider timeProvider)
    {
        string primaryPath = GetExecutableLogPath();
        string fallbackPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VRCInventoryManager",
            "VRCInventoryManager.debug.log");
        return TryCreate(primaryPath, reset: true, timeProvider)
            ?? TryCreate(fallbackPath, reset: true, timeProvider)
            ?? Disabled;
    }

    public static DebugLog? TryCreate(string filePath, bool reset, TimeProvider timeProvider)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FileMode mode = reset ? FileMode.Create : FileMode.Append;
            FileStream stream = new(filePath, mode, FileAccess.Write, FileShare.ReadWrite);
            StreamWriter streamWriter = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };

            DebugLog log = new(filePath, streamWriter, timeProvider);
            log.Info($"Log opened for VRCInventoryManager {GetProductVersion()}.");
            log.Info($"Process path: {Environment.ProcessPath ?? "unknown"}");
            return log;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception}");

    public void Write(string level, string message)
    {
        if (_writer is null || _isDisposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _writer.WriteLine($"{_timeProvider.GetLocalNow():O} [{EscapeNewlines(level)}] {EscapeNewlines(message)}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _writer?.Dispose();
        }
    }

    private static string EscapeNewlines(string text) =>
        text.Replace("\r", @"\r", StringComparison.Ordinal).Replace("\n", @"\n", StringComparison.Ordinal);

    private static string GetProductVersion()
    {
        Assembly assembly = typeof(DebugLog).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
