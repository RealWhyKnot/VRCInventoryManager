namespace VRCInventoryManager.Tests;

internal sealed record TestCase(string Name, Func<Task> RunAsync);
