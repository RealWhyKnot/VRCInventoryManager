using System.Reflection;
using System.Text;

namespace VRCInventoryManager.Core;

public sealed class DebugLog : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter? writer;
    private bool disposed;

    private DebugLog(string? filePath, StreamWriter? writer)
    {
        FilePath = filePath;
        this.writer = writer;
    }

    public string? FilePath { get; }

    public static DebugLog Disabled { get; } = new(null, null);

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

    public static DebugLog CreateNearExecutable()
    {
        string primaryPath = GetExecutableLogPath();
        DebugLog? log = TryCreate(primaryPath, reset: true);
        if (log is not null)
        {
            return log;
        }

        string fallbackPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VRCInventoryManager",
            "VRCInventoryManager.debug.log");
        return TryCreate(fallbackPath, reset: true) ?? Disabled;
    }

    public static DebugLog? TryCreate(string filePath, bool reset)
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
                AutoFlush = true
            };

            DebugLog log = new(filePath, streamWriter);
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

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message);
        if (exception is not null)
        {
            Write("ERROR", exception.ToString());
        }
    }

    public void Write(string level, string message)
    {
        if (writer is null || disposed)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            writer?.Dispose();
        }
    }

    private static string GetProductVersion()
    {
        Assembly assembly = typeof(DebugLog).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
