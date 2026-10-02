namespace StreamRoute.Models;

public sealed class ApplicationItem
{
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string? MainWindowTitle { get; init; }
    public bool IsAvailable { get; init; } = true;
    public int ProcessCount { get; init; } = 1;
    public bool HasWindow { get; init; }

    public string Detail
    {
        get
        {
            if (!IsAvailable)
            {
                return "Not running — press Refresh after starting it";
            }

            var processInfo = ProcessCount > 1 ? $"{ProcessName}.exe · {ProcessCount} processes" : $"{ProcessName}.exe";
            return string.IsNullOrWhiteSpace(MainWindowTitle)
                ? processInfo
                : $"{processInfo} · {MainWindowTitle}";
        }
    }

    public override string ToString() => DisplayName;
}
