namespace APPvista.Desktop.Services;

public sealed class AutoStartRegistrationService
{
    private const string TrayLaunchArgument = "--tray";
    private readonly string _taskName;
    private readonly string _taskCommand;

    public AutoStartRegistrationService(string appName, string executablePath)
    {
        _taskName = $@"\{appName}";
        _taskCommand = $"\\\"{executablePath}\\\" {TrayLaunchArgument}";
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            ExecuteSchTasks($"/Create /SC ONLOGON /TN \"{_taskName}\" /TR \"{_taskCommand}\" /RL HIGHEST /IT /F");
            return;
        }

        ExecuteSchTasks($"/Delete /TN \"{_taskName}\" /F", ignoreExitCode: true);
    }

    public bool IsEnabled()
    {
        return ExecuteSchTasks($"/Query /TN \"{_taskName}\"", ignoreExitCode: true) == 0;
    }

    private static int ExecuteSchTasks(string arguments, bool ignoreExitCode = false)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return -1;
        }

        process.WaitForExit();
        if (!ignoreExitCode && process.ExitCode != 0)
        {
            var errorOutput = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var message = string.IsNullOrWhiteSpace(errorOutput) ? standardOutput : errorOutput;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? $"Failed to execute schtasks.exe. Exit code: {process.ExitCode}."
                : message.Trim());
        }

        return process.ExitCode;
    }
}
