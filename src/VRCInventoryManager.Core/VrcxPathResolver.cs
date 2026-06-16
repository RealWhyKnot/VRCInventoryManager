using System.Text.Json;

namespace VRCInventoryManager.Core;

public static class VrcxPathResolver
{
    private const string VrcxDataFolderName = "VRCX";
    private const string VrcxDatabaseFileName = "VRCX.sqlite3";
    private const string VrcxConfigFileName = "VRCX.json";
    private const string VrchatFolderName = "VRChat";
    private const string EmojiFolderName = "Emoji";
    private const string StickersFolderName = "Stickers";
    private const string PrintsFolderName = "Prints";
    private const string VrchatPictureOutputFolderKey = "picture_output_folder";
    private const string VrcxDatabaseLocationKey = "VRCX_DatabaseLocation";

    public static KnownFolderPaths Resolve(VrcxPathResolverOptions? options = null)
    {
        options ??= new VrcxPathResolverOptions();

        string userProfilePath = GetUserProfilePath(options);
        string appDataPath = GetPath(
            options.AppDataPath,
            Environment.SpecialFolder.ApplicationData,
            Path.Combine(userProfilePath, "AppData", "Roaming"));
        string localApplicationDataPath = GetPath(
            options.LocalApplicationDataPath,
            Environment.SpecialFolder.LocalApplicationData,
            Path.Combine(userProfilePath, "AppData", "Local"));
        string myPicturesPath = GetPath(
            options.MyPicturesPath,
            Environment.SpecialFolder.MyPictures,
            Path.Combine(userProfilePath, "Pictures"));
        string programFilesPath = GetPath(
            options.ProgramFilesPath,
            Environment.SpecialFolder.ProgramFiles,
            Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty);
        string programFilesX86Path = GetPath(
            options.ProgramFilesX86Path,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty);

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppData"] = appDataPath,
            ["LocalAppData"] = localApplicationDataPath,
            ["UserProfile"] = userProfilePath,
            ["ProgramFiles"] = programFilesPath,
            ["ProgramFiles(x86)"] = programFilesX86Path
        };

        string vrcxDataFolder = NormalizePath(Path.Combine(appDataPath, VrcxDataFolderName));
        string vrcxConfigPath = NormalizePath(Path.Combine(vrcxDataFolder, VrcxConfigFileName));
        string vrchatConfigPath = GetVrchatConfigPath(appDataPath);
        string photosFolder = ResolvePhotosFolder(vrchatConfigPath, myPicturesPath, variables, options);
        string databasePath = ResolveDatabasePath(vrcxConfigPath, vrcxDataFolder, variables, options);
        string versionPath = ResolveVersionPath(programFilesPath, programFilesX86Path, localApplicationDataPath, options);

        return new KnownFolderPaths(
            photosFolder,
            NormalizePath(Path.Combine(photosFolder, EmojiFolderName)),
            NormalizePath(Path.Combine(photosFolder, StickersFolderName)),
            NormalizePath(Path.Combine(photosFolder, PrintsFolderName)),
            vrcxDataFolder,
            databasePath,
            versionPath,
            vrcxConfigPath,
            vrchatConfigPath);
    }

    private static string ResolvePhotosFolder(
        string vrchatConfigPath,
        string myPicturesPath,
        IReadOnlyDictionary<string, string> variables,
        VrcxPathResolverOptions options)
    {
        string? configuredPath = ReadJsonString(vrchatConfigPath, VrchatPictureOutputFolderKey, options);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return NormalizeConfiguredPath(configuredPath, variables);
        }

        return NormalizePath(Path.Combine(myPicturesPath, VrchatFolderName));
    }

    private static string ResolveDatabasePath(
        string vrcxConfigPath,
        string vrcxDataFolder,
        IReadOnlyDictionary<string, string> variables,
        VrcxPathResolverOptions options)
    {
        string defaultPath = NormalizePath(Path.Combine(vrcxDataFolder, VrcxDatabaseFileName));
        string? configuredLocation = ReadJsonString(vrcxConfigPath, VrcxDatabaseLocationKey, options);
        if (string.IsNullOrWhiteSpace(configuredLocation))
        {
            return defaultPath;
        }

        string normalizedLocation = NormalizeConfiguredPath(configuredLocation, variables);
        Func<string, bool> fileExists = options.FileExists ?? File.Exists;
        Func<string, bool> directoryExists = options.DirectoryExists ?? Directory.Exists;
        if (fileExists(normalizedLocation) || LooksLikeDatabaseFile(normalizedLocation))
        {
            return normalizedLocation;
        }

        string nestedDatabasePath = NormalizePath(Path.Combine(normalizedLocation, VrcxDatabaseFileName));
        if (directoryExists(normalizedLocation) || !Path.HasExtension(normalizedLocation) || fileExists(nestedDatabasePath))
        {
            return nestedDatabasePath;
        }

        return normalizedLocation;
    }

    private static string ResolveVersionPath(
        string programFilesPath,
        string programFilesX86Path,
        string localApplicationDataPath,
        VrcxPathResolverOptions options)
    {
        string[] candidates =
        [
            NormalizePath(Path.Combine(programFilesPath, VrcxDataFolderName, "Version")),
            NormalizePath(Path.Combine(programFilesX86Path, VrcxDataFolderName, "Version")),
            NormalizePath(Path.Combine(localApplicationDataPath, "Programs", VrcxDataFolderName, "Version"))
        ];

        Func<string, bool> fileExists = options.FileExists ?? File.Exists;
        foreach (string candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate));
    }

    private static string? ReadJsonString(string path, string propertyName, VrcxPathResolverOptions options)
    {
        Func<string, bool> fileExists = options.FileExists ?? File.Exists;
        Func<string, string> readAllText = options.ReadAllText ?? File.ReadAllText;
        if (!fileExists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(readAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string GetVrchatConfigPath(string appDataPath)
    {
        string localLowPath = NormalizePath(Path.Combine(appDataPath, "..", "LocalLow"));
        return NormalizePath(Path.Combine(localLowPath, VrchatFolderName, VrchatFolderName, "config.json"));
    }

    private static string GetUserProfilePath(VrcxPathResolverOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.UserProfilePath))
        {
            return NormalizePath(options.UserProfilePath);
        }

        string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return NormalizePath(folderPath);
        }

        string? environmentPath = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return NormalizePath(environmentPath);
        }

        return NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Personal));
    }

    private static string GetPath(string? overridePath, Environment.SpecialFolder specialFolder, string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return NormalizePath(overridePath);
        }

        string folderPath = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return NormalizePath(folderPath);
        }

        return NormalizePath(fallbackPath);
    }

    private static string NormalizeConfiguredPath(string path, IReadOnlyDictionary<string, string> variables) =>
        NormalizePath(ExpandEnvironmentVariables(path.Trim(), variables));

    private static string ExpandEnvironmentVariables(string path, IReadOnlyDictionary<string, string> variables)
    {
        string expanded = path;
        foreach ((string key, string value) in variables.OrderByDescending(variable => variable.Key.Length))
        {
            expanded = expanded.Replace($"%{key}%", value, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.Trim();
        if (trimmed.Contains('%', StringComparison.Ordinal) && !Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
        catch (NotSupportedException)
        {
            return trimmed;
        }
        catch (PathTooLongException)
        {
            return trimmed;
        }
    }

    private static bool LooksLikeDatabaseFile(string path) =>
        string.Equals(Path.GetExtension(path), ".sqlite3", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".sqlite", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase);
}
