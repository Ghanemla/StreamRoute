using System.Diagnostics;
using StreamRoute.Models;

namespace StreamRoute.Services;

public sealed class RunningAppService
{
    public IReadOnlyList<ApplicationItem> GetApplications()
    {
        var currentPid = Environment.ProcessId;
        var groups = new Dictionary<string, MutableApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == currentPid || process.HasExited)
                    {
                        continue;
                    }

                    var processName = process.ProcessName;
                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        continue;
                    }

                    var path = TryGetPath(process);
                    var title = TryGetWindowTitle(process);
                    var hasWindow = process.MainWindowHandle != IntPtr.Zero || !string.IsNullOrWhiteSpace(title);
                    var displayName = TryGetFriendlyName(path) ?? processName;
                    var key = !string.IsNullOrWhiteSpace(path) ? path : $"name:{processName}";

                    if (!groups.TryGetValue(key, out var app))
                    {
                        app = new MutableApp
                        {
                            DisplayName = displayName,
                            ProcessName = processName,
                            ExecutablePath = path,
                            MainWindowTitle = title,
                            HasWindow = hasWindow,
                            Count = 0
                        };
                        groups[key] = app;
                    }

                    app.Count++;
                    app.HasWindow |= hasWindow;
                    if (string.IsNullOrWhiteSpace(app.MainWindowTitle) && !string.IsNullOrWhiteSpace(title))
                    {
                        app.MainWindowTitle = title;
                    }
                }
                catch
                {
                    // Processes can disappear while being enumerated. Ignore and continue.
                }
            }
        }

        return groups.Values
            .Select(x => new ApplicationItem
            {
                DisplayName = x.DisplayName,
                ProcessName = x.ProcessName,
                ExecutablePath = x.ExecutablePath,
                MainWindowTitle = x.MainWindowTitle,
                HasWindow = x.HasWindow,
                ProcessCount = Math.Max(1, x.Count),
                IsAvailable = true
            })
            .OrderByDescending(x => x.HasWindow)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<int> GetMatchingProcessIds(ApplicationItem app)
    {
        var result = new List<int>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    var nameMatches = string.Equals(process.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase);
                    if (!nameMatches)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(app.ExecutablePath))
                    {
                        var path = TryGetPath(process);
                        if (!string.IsNullOrWhiteSpace(path) &&
                            !string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    result.Add(process.Id);
                }
                catch
                {
                    // Ignore processes that exit or deny access mid-scan.
                }
            }
        }

        return result.Distinct().ToList();
    }

    private static string? TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static string? TryGetWindowTitle(Process process)
    {
        try { return process.MainWindowTitle; }
        catch { return null; }
    }

    private static string? TryGetFriendlyName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription.Trim();
            }
        }
        catch
        {
            // Fall back to process name.
        }

        return null;
    }

    private sealed class MutableApp
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }
        public string? MainWindowTitle { get; set; }
        public bool HasWindow { get; set; }
        public int Count { get; set; }
    }
}
