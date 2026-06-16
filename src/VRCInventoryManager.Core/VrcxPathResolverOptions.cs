namespace VRCInventoryManager.Core;

public sealed class VrcxPathResolverOptions
{
    public string? AppDataPath { get; init; }

    public string? LocalApplicationDataPath { get; init; }

    public string? UserProfilePath { get; init; }

    public string? MyPicturesPath { get; init; }

    public string? ProgramFilesPath { get; init; }

    public string? ProgramFilesX86Path { get; init; }

    public Func<string, bool>? FileExists { get; init; }

    public Func<string, bool>? DirectoryExists { get; init; }

    public Func<string, string>? ReadAllText { get; init; }
}
