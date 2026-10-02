namespace StreamRoute.Models;

public sealed class AppSettings
{
    public string? SelectedProcessName { get; set; }
    public string? SelectedExecutablePath { get; set; }
    public string? SelectedAppDisplayName { get; set; }

    public string? StreamDeviceId { get; set; }
    public string? StreamDeviceName { get; set; }

    public string? NormalDeviceId { get; set; }
    public string? NormalDeviceName { get; set; }

    public bool LastModeWasStream { get; set; }
}
