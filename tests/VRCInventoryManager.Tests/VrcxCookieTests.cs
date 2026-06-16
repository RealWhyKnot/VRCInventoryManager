using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class VrcxCookieTests
{
    public static Task ParseCookiePayloadAsync()
    {
        string encoded = EncodeCookieJson([
            new CookieShape("auth", "auth_cookie"),
            new CookieShape("twoFactorAuth", "two_factor_cookie")
        ]);

        VrcxAuthCookies cookies = VrcxCookieProvider.ParseCookies(encoded);
        TestAssert.Equal("auth_cookie", cookies.Auth, "auth cookie");
        TestAssert.Equal("two_factor_cookie", cookies.TwoFactorAuth, "two factor cookie");

        string electronShape = EncodeElectronCookieJson();
        VrcxAuthCookies electronCookies = VrcxCookieProvider.ParseCookies(electronShape);
        TestAssert.Equal("api_auth_cookie", electronCookies.Auth, "lower-case auth cookie");
        TestAssert.Equal("api_two_factor_cookie", electronCookies.TwoFactorAuth, "lower-case two factor cookie");

        string missing = EncodeCookieJson([new CookieShape("auth", "do_not_leak")]);
        try
        {
            _ = VrcxCookieProvider.ParseCookies(missing);
            throw new InvalidOperationException("Expected missing twoFactorAuth to fail.");
        }
        catch (InvalidOperationException ex)
        {
            TestAssert.False(ex.Message.Contains("do_not_leak", StringComparison.Ordinal), "secret not in exception");
        }

        return Task.CompletedTask;
    }

    public static Task LoadCookiesFromSqliteCopyAsync()
    {
        string root = TestFiles.CreateTempDirectory();
        string databasePath = Path.Combine(root, "VRCX.sqlite3");
        string versionPath = Path.Combine(root, "Version");
        try
        {
            string encoded = EncodeCookieJson([
                new CookieShape("auth", "auth_from_db"),
                new CookieShape("twoFactorAuth", "two_factor_from_db")
            ]);

            using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                connection.Open();
                using SqliteCommand create = connection.CreateCommand();
                create.CommandText = "create table cookies(key text primary key, value text);";
                create.ExecuteNonQuery();

                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "insert into cookies(key, value) values ($key, $value);";
                insert.Parameters.AddWithValue("$key", "default");
                insert.Parameters.AddWithValue("$value", encoded);
                insert.ExecuteNonQuery();
            }

            File.WriteAllText(versionPath, "TestVRCX/1");
            SqliteConnection.ClearAllPools();
            VrcxCookieProvider provider = new(databasePath, versionPath);
            VrcxAuthSession session = provider.LoadDefaultSession();
            TestAssert.Equal("auth_from_db", session.Cookies.Auth, "db auth cookie");
            TestAssert.Equal("two_factor_from_db", session.Cookies.TwoFactorAuth, "db two factor cookie");
            TestAssert.Equal("TestVRCX/1", session.UserAgent, "user agent");
            return Task.CompletedTask;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string EncodeCookieJson(IEnumerable<CookieShape> cookies)
    {
        object[] shaped = cookies
            .Select(cookie => new
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = "api.vrchat.cloud",
                Path = "/",
                HttpOnly = true,
                Secure = true
            })
            .ToArray();
        string json = JsonSerializer.Serialize(shaped);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static string EncodeElectronCookieJson()
    {
        object[] shaped =
        [
            new
            {
                name = "auth",
                value = "wrong_auth_cookie",
                domain = "example.com"
            },
            new
            {
                name = "auth",
                value = "api_auth_cookie",
                domain = "api.vrchat.cloud"
            },
            new
            {
                name = "twoFactorAuth",
                value = "api_two_factor_cookie",
                domain = ".vrchat.cloud"
            }
        ];
        string json = JsonSerializer.Serialize(shaped);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private sealed record CookieShape(string Name, string Value);
}
