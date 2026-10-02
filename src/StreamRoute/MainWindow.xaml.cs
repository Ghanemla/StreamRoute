using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StreamRoute.Models;
using StreamRoute.Services;

namespace StreamRoute;

public partial class MainWindow : Window
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x36, 0xC9, 0x8F));
    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0x6B, 0x6B));
    private static readonly Brush NormalDotBrush = new SolidColorBrush(Color.FromRgb(0x75, 0x82, 0x90));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0x8A, 0x8A));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x71, 0xD8, 0xAE));

    private readonly SettingsService _settingsService = new();
    private readonly RunningAppService _runningAppService = new();
    private readonly AudioDeviceService _audioDeviceService = new();
    private readonly AudioRoutingService _audioRoutingService = new();

    private AppSettings _settings;
    private bool _initializing;
    private bool _streamMode;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _streamMode = _settings.LastModeWasStream;
        Loaded += (_, _) => RefreshEverything();
    }

    private ApplicationItem? SelectedApplication => ApplicationCombo.SelectedItem as ApplicationItem;
    private AudioDeviceItem? SelectedStreamDevice => StreamOutputCombo.SelectedItem as AudioDeviceItem;
    private AudioDeviceItem? SelectedNormalDevice => NormalOutputCombo.SelectedItem as AudioDeviceItem;

    private void RefreshEverything()
    {
        _initializing = true;
        try
        {
            RefreshApplications();
            RefreshDevices();
        }
        finally
        {
            _initializing = false;
        }

        SaveSettings();
        UpdateStatus();
    }

    private void RefreshApplications()
    {
        var applications = _runningAppService.GetApplications().ToList();
        var selected = FindRememberedApplication(applications);

        if (selected is null && !string.IsNullOrWhiteSpace(_settings.SelectedProcessName))
        {
            selected = new ApplicationItem
            {
                DisplayName = _settings.SelectedAppDisplayName ?? _settings.SelectedProcessName,
                ProcessName = _settings.SelectedProcessName,
                ExecutablePath = _settings.SelectedExecutablePath,
                IsAvailable = false,
                ProcessCount = 0,
                HasWindow = true
            };
            applications.Insert(0, selected);
        }

        ApplicationCombo.ItemsSource = applications;
        ApplicationCombo.SelectedItem = selected ?? applications.FirstOrDefault();

        if (ApplicationCombo.SelectedItem is ApplicationItem app)
        {
            RememberApplication(app);
        }
    }

    private ApplicationItem? FindRememberedApplication(IEnumerable<ApplicationItem> applications)
    {
        if (!string.IsNullOrWhiteSpace(_settings.SelectedExecutablePath))
        {
            var byPath = applications.FirstOrDefault(x =>
                string.Equals(x.ExecutablePath, _settings.SelectedExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return byPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.SelectedProcessName))
        {
            return applications.FirstOrDefault(x =>
                string.Equals(x.ProcessName, _settings.SelectedProcessName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void RefreshDevices()
    {
        var streamDevices = _audioDeviceService
            .GetRenderDevices(_settings.StreamDeviceId, _settings.StreamDeviceName)
            .ToList();
        var normalDevices = _audioDeviceService
            .GetRenderDevices(_settings.NormalDeviceId, _settings.NormalDeviceName)
            .ToList();

        StreamOutputCombo.ItemsSource = streamDevices;
        NormalOutputCombo.ItemsSource = normalDevices;

        var streamSelection = FindDevice(streamDevices, _settings.StreamDeviceId);
        if (streamSelection is null && string.IsNullOrWhiteSpace(_settings.StreamDeviceId))
        {
            // Helpful first-run choice for the most common streaming setup, while remaining fully editable.
            streamSelection = streamDevices.FirstOrDefault(x =>
                x.IsAvailable &&
                (x.DisplayName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
                 x.DisplayName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)))
                ?? streamDevices.FirstOrDefault();
        }

        var normalSelection = FindDevice(normalDevices, _settings.NormalDeviceId)
                              ?? normalDevices.FirstOrDefault(x => x.IsWindowsDefault)
                              ?? normalDevices.FirstOrDefault();

        StreamOutputCombo.SelectedItem = streamSelection;
        NormalOutputCombo.SelectedItem = normalSelection;

        if (streamSelection is not null)
        {
            RememberStreamDevice(streamSelection);
        }

        if (normalSelection is not null)
        {
            RememberNormalDevice(normalSelection);
        }
    }

    private static AudioDeviceItem? FindDevice(IEnumerable<AudioDeviceItem> devices, string? id)
    {
        if (id is null)
        {
            return null;
        }

        return devices.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateStatus()
    {
        FeedbackText.Text = string.Empty;

        var app = SelectedApplication;
        if (app is null)
        {
            CurrentRouteText.Text = "Choose an application";
            StatusSubText.Text = "Pick the program whose audio you want to control.";
            StatusDot.Fill = NormalDotBrush;
            StreamToggleButton.IsEnabled = false;
            UpdateToggleVisual();
            return;
        }

        var pids = _runningAppService.GetMatchingProcessIds(app);
        if (pids.Count == 0)
        {
            CurrentRouteText.Text = $"{app.DisplayName} is not running";
            StatusSubText.Text = "Start it, then press Refresh. Your saved choices are still remembered.";
            StatusDot.Fill = DangerBrush;
            StreamToggleButton.IsEnabled = false;
            UpdateDeviceWarning();
            UpdateToggleVisual();
            return;
        }

        try
        {
            var currentDefaultId = _audioDeviceService.GetDefaultRenderDeviceId();
            var routeKeys = pids
                .Select(pid => _audioRoutingService.GetPersistedEndpointId(pid) ?? string.Empty)
                .Select(id => NormalizeRouteForStatus(
                    id,
                    currentDefaultId,
                    SelectedNormalDevice?.IsWindowsDefault == true))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (routeKeys.Count == 1)
            {
                var actualId = routeKeys[0];
                var routeName = ResolveRouteName(actualId);
                CurrentRouteText.Text = $"{app.DisplayName}  →  {routeName}";

                if (SelectedStreamDevice is not null && RouteMatches(actualId, SelectedStreamDevice, currentDefaultId))
                {
                    _streamMode = true;
                    StatusDot.Fill = AccentBrush;
                    StatusSubText.Text = "Stream mode is active. You can close StreamRoute; Windows keeps this route.";
                }
                else if (SelectedNormalDevice is not null && RouteMatches(actualId, SelectedNormalDevice, currentDefaultId))
                {
                    _streamMode = false;
                    StatusDot.Fill = NormalDotBrush;
                    StatusSubText.Text = "Normal mode is active.";
                }
                else
                {
                    _streamMode = false;
                    StatusDot.Fill = NormalDotBrush;
                    StatusSubText.Text = "This app is currently routed somewhere other than your two selected outputs.";
                }
            }
            else
            {
                // Multi-process applications (Spotify is a common example) can contain helper
                // processes that Windows refuses to route because they do not own an audio
                // session. Those helpers stay on Windows Default while the real audio process
                // is correctly routed. Do NOT use that harmless mixed state to flip the toggle
                // back to STREAM ON; keep the last mode the user explicitly selected.
                CurrentRouteText.Text = $"{app.DisplayName}  →  mixed routes";

                if (_streamMode)
                {
                    StatusSubText.Text = "Stream mode is active. Some helper processes remain on Windows Default, which is normal.";
                    StatusDot.Fill = AccentBrush;
                }
                else
                {
                    StatusSubText.Text = "Normal mode is active. Some helper processes still have saved routing entries.";
                    StatusDot.Fill = NormalDotBrush;
                }
            }

            StreamToggleButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            CurrentRouteText.Text = $"{app.DisplayName}  →  routing unavailable";
            StatusSubText.Text = "Windows' per-app audio interface could not be read.";
            StatusDot.Fill = DangerBrush;
            StreamToggleButton.IsEnabled = true;
            SetFeedback(ex.Message, true);
        }

        UpdateDeviceWarning();
        UpdateToggleVisual();
    }

    private void UpdateDeviceWarning()
    {
        if (SelectedStreamDevice is { IsAvailable: false })
        {
            SetFeedback("Your Stream Output is unavailable or unplugged. Pick another device or reconnect it.", true);
            return;
        }

        if (SelectedNormalDevice is { IsAvailable: false })
        {
            SetFeedback("Your Normal Output is unavailable or unplugged. Pick another device or reconnect it.", true);
        }
    }

    private string ResolveRouteName(string actualId)
    {
        if (string.IsNullOrWhiteSpace(actualId))
        {
            return $"Windows Default ({_audioDeviceService.GetDefaultRenderDeviceName()})";
        }

        var allDevices = (StreamOutputCombo.ItemsSource as IEnumerable<AudioDeviceItem>)
            ?.Concat(NormalOutputCombo.ItemsSource as IEnumerable<AudioDeviceItem> ?? Array.Empty<AudioDeviceItem>());

        var match = allDevices?.FirstOrDefault(x =>
            string.Equals(x.Id, actualId, StringComparison.OrdinalIgnoreCase));

        return match?.DisplayName ?? "Unknown / disconnected device";
    }

    private static string NormalizeRouteForStatus(
        string actualId,
        string? currentDefaultId,
        bool normalOutputUsesWindowsDefault)
    {
        if (string.IsNullOrWhiteSpace(actualId))
        {
            return string.Empty;
        }

        // STREAM OFF first routes to the concrete current Windows default endpoint so the
        // audio moves immediately. Treat that endpoint as normal/default in the UI even if
        // Windows refuses to remove the persisted override from one helper process.
        if (normalOutputUsesWindowsDefault &&
            !string.IsNullOrWhiteSpace(currentDefaultId) &&
            string.Equals(actualId, currentDefaultId, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return actualId;
    }

    private static bool RouteMatches(string? actualId, AudioDeviceItem target, string? currentDefaultId)
    {
        if (target.IsWindowsDefault)
        {
            return string.IsNullOrWhiteSpace(actualId) ||
                   (!string.IsNullOrWhiteSpace(currentDefaultId) &&
                    string.Equals(actualId, currentDefaultId, StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(actualId, target.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateToggleVisual()
    {
        if (_streamMode)
        {
            StreamToggleButton.Content = "STREAM OFF";
            StreamToggleButton.Background = DangerBrush;
            StreamToggleButton.Foreground = Brushes.White;
        }
        else
        {
            StreamToggleButton.Content = "STREAM ON";
            StreamToggleButton.Background = AccentBrush;
            StreamToggleButton.Foreground = new SolidColorBrush(Color.FromRgb(0x07, 0x11, 0x0D));
        }
    }

    private void StreamToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var app = SelectedApplication;
        var streamDevice = SelectedStreamDevice;
        var normalDevice = SelectedNormalDevice;

        if (app is null || streamDevice is null || normalDevice is null)
        {
            SetFeedback("Choose an application, Stream Output, and Normal Output first.", true);
            return;
        }

        if (string.Equals(streamDevice.Id, normalDevice.Id, StringComparison.OrdinalIgnoreCase))
        {
            SetFeedback("Stream Output and Normal Output are the same device. Choose two different outputs.", true);
            return;
        }

        var target = _streamMode ? normalDevice : streamDevice;
        if (!target.IsAvailable)
        {
            SetFeedback($"{target.DisplayName} is unavailable. Reconnect it or choose another output.", true);
            return;
        }

        var pids = _runningAppService.GetMatchingProcessIds(app);
        if (pids.Count == 0)
        {
            SetFeedback("That application is not running. Start it and press Refresh.", true);
            return;
        }

        try
        {
            var turningStreamOff = _streamMode;
            var targetEndpointId = target.Id;

            // Windows can reject a direct "clear to default" call for helper processes in
            // multi-process apps (Spotify is a common example). For Windows Default we first
            // route to the concrete current default endpoint, which moves the live audio
            // immediately, then clear persisted routing on a best-effort basis.
            if (target.IsWindowsDefault)
            {
                targetEndpointId = _audioDeviceService.GetDefaultRenderDeviceId();
                if (string.IsNullOrWhiteSpace(targetEndpointId))
                {
                    throw new InvalidOperationException("Windows has no active default output device.");
                }
            }

            _audioRoutingService.RouteProcesses(pids, targetEndpointId);

            if (turningStreamOff && target.IsWindowsDefault)
            {
                _audioRoutingService.TryClearProcesses(pids);
            }

            _streamMode = !turningStreamOff;
            _settings.LastModeWasStream = _streamMode;
            SaveSettings();
            UpdateStatus();
            SetFeedback(_streamMode ? "Stream mode applied." : "Normal audio restored.", false);
        }
        catch (Exception ex)
        {
            UpdateStatus();
            SetFeedback($"Routing failed: {ex.Message}", true);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var app = SelectedApplication;
        if (app is null)
        {
            SetFeedback("Choose an application first.", true);
            return;
        }

        var pids = _runningAppService.GetMatchingProcessIds(app);
        if (pids.Count == 0)
        {
            SetFeedback("That application is not running. Start it and press Refresh before resetting it.", true);
            return;
        }

        try
        {
            var defaultEndpointId = _audioDeviceService.GetDefaultRenderDeviceId();
            if (string.IsNullOrWhiteSpace(defaultEndpointId))
            {
                throw new InvalidOperationException("Windows has no active default output device.");
            }

            // Move the live audio first so Reset is reliable, then remove persisted routing
            // where Windows allows it. This avoids Spotify being stranded on VB-CABLE.
            _audioRoutingService.RouteProcesses(pids, defaultEndpointId);
            _audioRoutingService.TryClearProcesses(pids);

            _streamMode = false;
            _settings.LastModeWasStream = false;
            SaveSettings();
            UpdateStatus();
            SetFeedback("Selected app returned to Windows Default.", false);
        }
        catch (Exception ex)
        {
            UpdateStatus();
            SetFeedback($"Reset failed: {ex.Message}", true);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshEverything();
        SetFeedback("Applications and audio devices refreshed.", false);
    }

    private void ApplicationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ApplicationCombo.SelectedItem is not ApplicationItem app)
        {
            return;
        }

        RememberApplication(app);
        SaveSettings();
        UpdateStatus();
    }

    private void StreamOutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || StreamOutputCombo.SelectedItem is not AudioDeviceItem device)
        {
            return;
        }

        RememberStreamDevice(device);
        SaveSettings();
        UpdateStatus();
    }

    private void NormalOutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || NormalOutputCombo.SelectedItem is not AudioDeviceItem device)
        {
            return;
        }

        RememberNormalDevice(device);
        SaveSettings();
        UpdateStatus();
    }

    private void RememberApplication(ApplicationItem app)
    {
        _settings.SelectedProcessName = app.ProcessName;
        _settings.SelectedExecutablePath = app.ExecutablePath;
        _settings.SelectedAppDisplayName = app.DisplayName;
    }

    private void RememberStreamDevice(AudioDeviceItem device)
    {
        _settings.StreamDeviceId = device.Id;
        _settings.StreamDeviceName = device.DisplayName;
    }

    private void RememberNormalDevice(AudioDeviceItem device)
    {
        _settings.NormalDeviceId = device.Id;
        _settings.NormalDeviceName = device.DisplayName;
    }

    private void SaveSettings() => _settingsService.Save(_settings);

    private void SetFeedback(string message, bool isError)
    {
        FeedbackText.Text = message;
        FeedbackText.Foreground = isError ? ErrorBrush : SuccessBrush;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        _audioRoutingService.Dispose();
    }
}
