using FifineControl.Core.Startup;

namespace FifineControl.Core.Tests;

public sealed class WindowsStartupRegistrationServiceTests
{
    [Fact]
    public void SetEnabled_WritesQuotedExecutablePath()
    {
        var registry = new MemoryStartupRegistry();
        var service = new WindowsStartupRegistrationService(registry);
        var executable = Path.Combine(Path.GetTempPath(), "Fifine App", "FifineControl.exe");

        service.SetEnabled(true, executable);

        Assert.Equal($"\"{Path.GetFullPath(executable)}\"", registry.Values[WindowsStartupRegistrationService.ValueName]);
        Assert.True(service.IsEnabled(executable));
    }

    [Fact]
    public void SetEnabledFalse_DeletesOnlyOwnedValue()
    {
        var registry = new MemoryStartupRegistry();
        registry.Values[WindowsStartupRegistrationService.ValueName] = "old-value";
        registry.Values["AnotherApplication"] = "keep-me";
        var service = new WindowsStartupRegistrationService(registry);

        service.SetEnabled(false, string.Empty);

        Assert.False(registry.Values.ContainsKey(WindowsStartupRegistrationService.ValueName));
        Assert.Equal("keep-me", registry.Values["AnotherApplication"]);
    }

    [Fact]
    public void Construction_DoesNotEnableStartup()
    {
        var registry = new MemoryStartupRegistry();

        _ = new WindowsStartupRegistrationService(registry);

        Assert.Empty(registry.Values);
    }

    [Fact]
    public void IsEnabled_ReportsExistingOwnedValueSoItCanBeRemoved()
    {
        var registry = new MemoryStartupRegistry();
        registry.Values[WindowsStartupRegistrationService.ValueName] = "legacy-command";
        var service = new WindowsStartupRegistrationService(registry);

        Assert.True(service.IsEnabled(@"C:\Apps\FifineControl.exe"));
    }

    private sealed class MemoryStartupRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetString(string valueName) =>
            Values.GetValueOrDefault(valueName);

        public void SetString(string valueName, string value) =>
            Values[valueName] = value;

        public void DeleteValue(string valueName) =>
            Values.Remove(valueName);
    }
}
