namespace WinFormsApp1.Infrastructure.Services;

internal static class ApplicationIdentityResolver
{
    public static string Resolve(string processName, string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            return processName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return Path.GetFileNameWithoutExtension(executablePath)?.Trim() ?? "Unknown";
        }

        return "Unknown";
    }
}
