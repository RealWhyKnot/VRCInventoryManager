using System.Diagnostics;
using VRCInventoryManager.Core;

namespace VRCInventoryManager.Tests;

public sealed class UpdateScriptTests
{
    private static readonly string[] ExpectedSteps = ["relaunch", "cleanup"];

    private const string ParseCommand = """
        $source = [Console]::In.ReadToEnd()
        $tokens = $null
        $errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref] $tokens, [ref] $errors)
        if ($errors.Count -gt 0) {
            [Console]::Error.WriteLine(($errors | Out-String))
            exit 1
        }
        exit 0
        """;

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    public async Task RenderedPathsWithSpacesAndApostrophesParseAsync(string shell)
    {
        string script = RenderScript();
        Assert.DoesNotContain("__", script, StringComparison.Ordinal);
        PowerShellResult result = await RunPowerShellAsync(shell, ParseCommand, script, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.Error);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 1)]
    public async Task RelaunchAndCleanupRunEvenForUnexpectedFailuresAsync(bool failSetup, bool failRelaunch, int expectedExitCode)
    {
        string command = """
            function Wait-Process { param($Id, $Timeout, $ErrorAction) }
            function Start-Process {
                param($FilePath, $ArgumentList, [switch] $Wait, [switch] $PassThru, $WorkingDirectory, $WindowStyle)
                if ($Wait) {
                    if ($WindowStyle) { throw [FormatException]::new('Unexpected window style') }
                    if ($failSetup) { throw [FormatException]::new('Setup failed') }
                    return [pscustomobject] @{ ExitCode = 0 }
                }
                [Console]::Out.WriteLine('relaunch')
                if ($failRelaunch) { throw [FormatException]::new('Relaunch failed') }
            }
            function Remove-Item {
                param($LiteralPath, [switch] $Recurse, [switch] $Force, $ErrorAction)
                [Console]::Out.WriteLine('cleanup')
            }
            & ([scriptblock]::Create([Console]::In.ReadToEnd()))
            """;
        command = "$failSetup = $" + failSetup + "; $failRelaunch = $" + failRelaunch + "; " + command;
        PowerShellResult result = await RunPowerShellAsync("pwsh", command, RenderScript(), CancellationToken.None);
        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal(ExpectedSteps, result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RenderScript() => UpdateScript.RunSetup(4242,
        @"C:\Fixture\O'Neil Folder\update\Setup.exe", @"C:\Fixture\O'Neil Folder\update",
        @"C:\Fixture\O'Neil Folder\app", @"C:\Fixture\O'Neil Folder\app\VRCInventoryManager.exe",
        @"C:\Fixture\O'Neil Folder\update.log");

    private static async Task<PowerShellResult> RunPowerShellAsync(string shell, string command, string input, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ProcessStartInfo start = new(shell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return new PowerShellResult(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
