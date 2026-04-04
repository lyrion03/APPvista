using Microsoft.Win32;

namespace WinFormsApp1.Desktop.Services;

public sealed class AutoStartRegistrationService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _appName;
    private readonly string _command;

    public AutoStartRegistrationService(string appName, string executablePath)
    {
        _appName = appName;
        _command = $"\"{executablePath}\"";
    }

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryPath);
        if (runKey is null)
        {
            return;
        }

        if (enabled)
        {
            runKey.SetValue(_appName, _command);
            return;
        }

        runKey.DeleteValue(_appName, false);
    }

    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
        var currentValue = runKey?.GetValue(_appName) as string;
        return string.Equals(currentValue, _command, StringComparison.OrdinalIgnoreCase);
    }
}
