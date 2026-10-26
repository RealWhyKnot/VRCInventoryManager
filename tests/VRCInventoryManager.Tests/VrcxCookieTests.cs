using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

internal static class VrcxCookieTests
{
    public static Task BundledSqliteHasTheAggregateFixAsync()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select sqlite_version()";
        string version = (string)command.ExecuteScalar()!;
        TestAssert.True(Version.Parse(version) >= new Version(3, 50, 2), $"bundled SQLite {version} predates 3.50.2 (CVE-2025-6965)");
        return Task.CompletedTask;
    }

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

    public static Task LoadCookiesFromSqliteAsync()
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

    public static Task ReadWalCookiesWhileVrcxHasDatabaseOpenAsync()
    {
        return WithVrcxDatabaseAsync((databasePath, tempDirectory) =>
        {
            using SqliteConnection vrcx = OpenVrcxConnection(databasePath);
            Execute(vrcx, "pragma journal_mode=wal; pragma wal_autocheckpoint=0;");
            WriteCookieRow(vrcx, "auth_in_wal");

            VrcxAuthSession session = new VrcxCookieProvider(databasePath, Path.Combine(tempDirectory, "Version")).LoadDefaultSession();
            TestAssert.Equal("auth_in_wal", session.Cookies.Auth, "cookie committed to the WAL");
            TestAssert.Equal(0, Directory.GetFileSystemEntries(tempDirectory).Length, "temp entries after read");
        });
    }

    public static Task ReadCommittedCookiesDuringVrcxWriteAsync()
    {
        return WithVrcxDatabaseAsync((databasePath, tempDirectory) =>
        {
            using SqliteConnection vrcx = OpenVrcxConnection(databasePath);
            WriteCookieRow(vrcx, "auth_committed");
            using SqliteTransaction pending = vrcx.BeginTransaction(deferred: false);
            using SqliteCommand update = vrcx.CreateCommand();
            update.CommandText = "update cookies set value = 'not committed' where key = 'default';";
            update.ExecuteNonQuery();

            VrcxAuthSession session = new VrcxCookieProvider(databasePath, Path.Combine(tempDirectory, "Version")).LoadDefaultSession();
            TestAssert.Equal("auth_committed", session.Cookies.Auth, "committed cookie during a write");
            TestAssert.Equal(0, Directory.GetFileSystemEntries(tempDirectory).Length, "temp entries after read");
            pending.Rollback();
        });
    }

    public static Task FallBackToTemporaryCopyWhenDatabaseIsLockedAsync()
    {
        return WithVrcxDatabaseAsync((databasePath, tempDirectory) =>
        {
            using SqliteConnection vrcx = OpenVrcxConnection(databasePath);
            WriteCookieRow(vrcx, "auth_from_copy");
            Execute(vrcx, "pragma locking_mode=exclusive; create table hold(id integer);");

            VrcxAuthSession session = new VrcxCookieProvider(databasePath, Path.Combine(tempDirectory, "Version")).LoadDefaultSession();
            TestAssert.Equal("auth_from_copy", session.Cookies.Auth, "cookie from the fallback copy");
            TestAssert.Equal(0, Directory.GetFileSystemEntries(tempDirectory).Length, "temp entries after fallback");
        });
    }

    private static Task WithVrcxDatabaseAsync(Action<string, string> test)
    {
        string root = TestFiles.CreateTempDirectory();
        string databasePath = Path.Combine(root, "VRCX.sqlite3");
        string tempDirectory = Path.Combine(root, "temp");
        Directory.CreateDirectory(tempDirectory);
        string? previousTmp = Environment.GetEnvironmentVariable("TMP");
        try
        {
            Environment.SetEnvironmentVariable("TMP", tempDirectory);
            test(databasePath, tempDirectory);
            return Task.CompletedTask;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SqliteConnection OpenVrcxConnection(string databasePath)
    {
        SqliteConnection connection = new($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void WriteCookieRow(SqliteConnection connection, string auth)
    {
        Execute(connection, "create table cookies(key text primary key, value text);");
        using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "insert into cookies(key, value) values ($key, $value);";
        insert.Parameters.AddWithValue("$key", "default");
        insert.Parameters.AddWithValue("$value", EncodeCookieJson([
            new CookieShape("auth", auth),
            new CookieShape("twoFactorAuth", "two_factor")
        ]));
        insert.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
