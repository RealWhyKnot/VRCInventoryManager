namespace VRCInventoryManager.Tests;

internal static class TestRunner
{
    public static async Task<int> RunAsync(IEnumerable<TestCase> tests)
    {
        int failures = 0;
        foreach (TestCase test in tests)
        {
            try
            {
                await test.RunAsync();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return failures;
    }
}
