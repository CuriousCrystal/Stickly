using Microsoft.Win32;

namespace StickApp.Services;

public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StickApp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                             ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    key.SetValue(ValueName, $"\"{processPath}\"");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Suppress registry permission or policy restrictions
        }
    }
}
