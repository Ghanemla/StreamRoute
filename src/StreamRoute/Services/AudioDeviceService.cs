using NAudio.CoreAudioApi;
using StreamRoute.Models;

namespace StreamRoute.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceItem> GetRenderDevices(string? rememberedId = null, string? rememberedName = null)
    {
        var list = new List<AudioDeviceItem>();
        string defaultName = "current device";

        using var enumerator = new MMDeviceEnumerator();

        try
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultName = defaultDevice.FriendlyName;
        }
        catch
        {
            // Windows may briefly have no default endpoint during device changes.
        }

        list.Add(new AudioDeviceItem
        {
            Id = string.Empty,
            DisplayName = $"Windows Default ({defaultName})",
            IsAvailable = true
        });

        try
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    list.Add(new AudioDeviceItem
                    {
                        Id = device.ID,
                        DisplayName = device.FriendlyName,
                        IsAvailable = true
                    });
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // Return at least Windows Default.
        }

        if (!string.IsNullOrWhiteSpace(rememberedId) &&
            list.All(x => !string.Equals(x.Id, rememberedId, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new AudioDeviceItem
            {
                Id = rememberedId,
                DisplayName = string.IsNullOrWhiteSpace(rememberedName) ? "Previously selected device" : rememberedName,
                IsAvailable = false
            });
        }

        return list
            .Take(1)
            .Concat(list.Skip(1).OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    public string? GetDefaultRenderDeviceId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    public string GetDefaultRenderDeviceName()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.FriendlyName;
        }
        catch
        {
            return "Windows Default";
        }
    }
}
