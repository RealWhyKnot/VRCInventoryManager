using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace VRCInventoryManager.Core;

internal sealed class VrcxCookieProvider
{
    private readonly string _databasePath;
    private readonly string _versionPath;
    private readonly string _temporaryDirectory;

    public VrcxCookieProvider(string? databasePath = null, string? versionPath = null, string? temporaryDirectory = null)
    {
        _temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
        KnownFolderPaths? folders = string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(versionPath)
            ? KnownFolders.Resolve() : null;
        _databasePath = string.IsNullOrWhiteSpace(databasePath) ? folders!.VrcxDatabasePath : databasePath;
        _versionPath = string.IsNullOrWhiteSpace(versionPath) ? folders!.VrcxVersionPath : versionPath;
    }

    public VrcxAuthSession LoadDefaultSession()
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("VRCX cookie database was not found.", _databasePath);
        }

        string encodedCookies = ReadEncodedCookies();
        VrcxAuthCookies cookies = ParseCookies(encodedCookies);
        string userAgent = LoadUserAgent();
        return new VrcxAuthSession(cookies, userAgent);
    }

    public static VrcxAuthCookies ParseCookies(string encodedCookies)
    {
        byte[] decoded = new byte[encodedCookies.Length];
        if (!Convert.TryFromBase64String(encodedCookies, decoded, out int bytesWritten))
        {
            throw new InvalidOperationException("VRCX cookie payload was not valid base64.");
        }

        string json = Encoding.UTF8.GetString(decoded, 0, bytesWritten);

        using var document = JsonDocument.Parse(json);
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                string? domain = ReadStringProperty(element, "domain");
                if (!IsVrchatApiCookieDomain(domain))
                {
                    continue;
                }

                string? name = ReadStringProperty(element, "name");
                string? value = ReadStringProperty(element, "value");

                if (!string.IsNullOrWhiteSpace(name) && value is not null)
                {
                    values[name] = value;
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    values[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return !values.TryGetValue("auth", out string? auth) || string.IsNullOrWhiteSpace(auth) ||
            !values.TryGetValue("twoFactorAuth", out string? twoFactorAuth) || string.IsNullOrWhiteSpace(twoFactorAuth)
            ? throw new InvalidOperationException("VRCX cookie store did not contain usable VRChat auth cookies.")
            : new VrcxAuthCookies(auth, twoFactorAuth);
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsVrchatApiCookieDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return true;
        }

        string normalized = domain.Trim().TrimStart('.');
        return string.Equals(normalized, "api.vrchat.cloud", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "vrchat.cloud", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".vrchat.cloud", StringComparison.OrdinalIgnoreCase);
    }

    private string ReadEncodedCookies()
    {
        try
        {
            return ReadEncodedCookiesFromDatabase(_databasePath);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is raw.SQLITE_BUSY or raw.SQLITE_LOCKED or raw.SQLITE_READONLY or raw.SQLITE_CANTOPEN)
        {
            return ReadEncodedCookiesFromCopy();
        }
    }

    private string ReadEncodedCookiesFromCopy()
    {
        string tempPath = Path.Combine(_temporaryDirectory, $"vrcx-cookies-{Guid.NewGuid():N}.sqlite3");
        try
        {
            File.Copy(_databasePath, tempPath, overwrite: true);
            return ReadEncodedCookiesFromDatabase(tempPath);
        }
        finally
        {
            foreach (string path in new[] { tempPath, tempPath + "-journal", tempPath + "-wal", tempPath + "-shm" })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    continue;
                }
            }
        }
    }

    private static string ReadEncodedCookiesFromDatabase(string path)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5,
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select value from cookies where key = $key limit 1";
        command.Parameters.AddWithValue("$key", "default");
        object? value = command.ExecuteScalar();
        string? encodedCookies = value as string;
        return encodedCookies ?? throw new InvalidOperationException("VRCX default cookie row was not found.");
    }

    private string LoadUserAgent()
    {
        if (!File.Exists(_versionPath))
        {
            return "VRCX";
        }

        string version = File.ReadAllText(_versionPath).Trim();
        return string.IsNullOrWhiteSpace(version) ? "VRCX" : version;
    }
}
