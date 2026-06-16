using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace VRCInventoryManager.Core;

public sealed class VrcxCookieProvider
{
    private readonly string databasePath;
    private readonly string versionPath;

    public VrcxCookieProvider(string? databasePath = null, string? versionPath = null)
    {
        KnownFolderPaths folders = KnownFolders.Resolve();
        this.databasePath = string.IsNullOrWhiteSpace(databasePath) ? folders.VrcxDatabasePath : databasePath;
        this.versionPath = string.IsNullOrWhiteSpace(versionPath) ? folders.VrcxVersionPath : versionPath;
    }

    public VrcxAuthSession LoadDefaultSession()
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("VRCX cookie database was not found.", databasePath);
        }

        string encodedCookies = ReadEncodedCookies();
        VrcxAuthCookies cookies = ParseCookies(encodedCookies);
        string userAgent = LoadUserAgent();
        return new VrcxAuthSession(cookies, userAgent);
    }

    public static VrcxAuthCookies ParseCookies(string encodedCookies)
    {
        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCookies));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("VRCX cookie payload was not valid base64.", ex);
        }

        using JsonDocument document = JsonDocument.Parse(json);
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

        if (!values.TryGetValue("auth", out string? auth) || string.IsNullOrWhiteSpace(auth) ||
            !values.TryGetValue("twoFactorAuth", out string? twoFactorAuth) || string.IsNullOrWhiteSpace(twoFactorAuth))
        {
            throw new InvalidOperationException("VRCX cookie store did not contain usable VRChat auth cookies.");
        }

        return new VrcxAuthCookies(auth, twoFactorAuth);
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
        string tempPath = Path.Combine(Path.GetTempPath(), $"vrcx-cookies-{Guid.NewGuid():N}.sqlite3");
        try
        {
            File.Copy(databasePath, tempPath, overwrite: true);
            return ReadEncodedCookiesFromDatabase(tempPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string ReadEncodedCookiesFromDatabase(string path)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        };

        using SqliteConnection connection = new(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select value from cookies where key = $key limit 1";
        command.Parameters.AddWithValue("$key", "default");
        object? value = command.ExecuteScalar();
        string? encodedCookies = value as string;
        SqliteConnection.ClearAllPools();
        return encodedCookies ?? throw new InvalidOperationException("VRCX default cookie row was not found.");
    }

    private string LoadUserAgent()
    {
        if (!File.Exists(versionPath))
        {
            return "VRCX";
        }

        string version = File.ReadAllText(versionPath).Trim();
        return string.IsNullOrWhiteSpace(version) ? "VRCX" : version;
    }
}
