using System.Diagnostics;
namespace APPvista.Infrastructure.Services;
internal static class ProcessMonitoringFilter
{
    private static readonly string[] ExcludedProcessNames =
    [
        "Idle",
        "System",
        "svchost",
        "services",
        "lsass",
        "wininit",
        "fontdrvhost",
        "ctfmon",
        "dllhost",
        "taskhostw",
        "sihost",
        "dwm"
    ];

    public static bool ShouldMonitor(Process process, int interactiveSessionId)
    {
        try
        {
            if (process.Id <= 0)
            {
                return false;
            }
            if (process.SessionId != interactiveSessionId)
            {
                return false;
            }
            var processName = process.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            if (ExcludedProcessNames.Any(item => string.Equals(item, processName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
