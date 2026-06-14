using System.Text.Json;

namespace VRCInventoryManager.Core;

public sealed class AppSettingsStore
{
    private readonly string settingsPath;

    public AppSettingsStore(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VRCInventoryManager",
            "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        string tempPath = settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(settingsPath))
        {
            File.Replace(tempPath, settingsPath, null);
        }
        else
        {
            File.Move(tempPath, settingsPath);
        }
    }
}
