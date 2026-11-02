using System.Globalization;

namespace VRCInventoryManager.Core;

internal static class UpdateScript
{
    public static string RunSetup(int processId, string setupPath, string stagingDirectory, string installDirectory, string exePath, string logPath)
    {
        using Stream stream = typeof(UpdateScript).Assembly.GetManifestResourceStream("VRCInventoryManager.Core.ApplyUpdate.ps1")
            ?? throw new InvalidOperationException("Update script was not found.");
        using StreamReader reader = new(stream);
        Dictionary<string, string> substitutions = new(StringComparer.Ordinal)
        {
            ["[int] $ProcessId = 0"] = "[int] $ProcessId = " + processId.ToString(CultureInfo.InvariantCulture),
            ["'__SETUP_PATH__'"] = Quote(setupPath),
            ["'__SETUP_ARGUMENTS__'"] = Quote("/S /D=" + installDirectory),
            ["'__STAGING_DIRECTORY__'"] = Quote(stagingDirectory),
            ["'__INSTALL_DIRECTORY__'"] = Quote(installDirectory),
            ["'__EXECUTABLE_PATH__'"] = Quote(exePath),
            ["'__LOG_PATH__'"] = Quote(logPath),
        };
        string[] lines = reader.ReadToEnd().Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            foreach ((string placeholder, string value) in substitutions)
            {
                if (lines[index].Contains(placeholder, StringComparison.Ordinal))
                {
                    lines[index] = lines[index].Replace(placeholder, value, StringComparison.Ordinal);
                    break;
                }
            }
        }

        return string.Join('\n', lines);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
