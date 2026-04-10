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

    public static bool ShouldMonitor(int processId, int sessionId, string processName, int interactiveSessionId)
    {
        if (processId <= 0)
        {
            return false;
        }

        if (sessionId != interactiveSessionId)
        {
            return false;
        }

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
}
