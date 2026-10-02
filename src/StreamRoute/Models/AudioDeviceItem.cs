namespace StreamRoute.Models;

public sealed class AudioDeviceItem
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsAvailable { get; init; } = true;
    public bool IsWindowsDefault => string.IsNullOrEmpty(Id);

    public string Detail => IsWindowsDefault
        ? "Follow the current Windows default output"
        : IsAvailable
            ? "Available"
            : "Unavailable / unplugged";

    public override string ToString() => DisplayName;
}
