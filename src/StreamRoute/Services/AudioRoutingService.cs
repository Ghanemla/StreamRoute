using System.Runtime.InteropServices;
using StreamRoute.Interop;

namespace StreamRoute.Services;

public sealed class AudioRoutingService : IDisposable
{
    private const string MmdevapiToken = @"\\?\SWD#MMDEVAPI#";
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const int EInvalidArg = unchecked((int)0x80070057);

    private static readonly string[] CandidateClassNames =
    {
        "Windows.Media.Internal.AudioPolicyConfig",
        "Windows.Media.AudioPolicyConfig",
    };

    private static readonly Guid Win11FactoryIid = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid Win10FactoryIid = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    private readonly bool _isWin11 = Environment.OSVersion.Version.Build >= 22000;
    private readonly bool _winRtInitialized;
    private readonly object _factoryLock = new();
    private IAudioPolicyConfigFactoryWin11? _factoryWin11;
    private IAudioPolicyConfigFactoryWin10? _factoryWin10;
    private bool _factoryProbed;
    private bool _disposed;

    public AudioRoutingService()
    {
        _winRtInitialized = WinRtFactory.InitializeForCurrentThread();
    }

    /// <summary>
    /// Routes every matching process/role that Windows will accept. Multi-process apps such as
    /// Spotify often contain helper processes with no audio session; Windows returns E_INVALIDARG
    /// for those. That is an expected no-op, not a reason to abort routing for the real audio PID.
    /// </summary>
    public void RouteProcesses(IEnumerable<int> processIds, string endpointId)
    {
        var ids = processIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            throw new InvalidOperationException("The selected application is not running.");
        }

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            throw new ArgumentException("A real audio endpoint is required for this routing operation.", nameof(endpointId));
        }

        var persistedId = BuildPersistedId(endpointId);
        var successes = 0;
        Exception? firstUnexpectedFailure = null;

        foreach (var pid in ids)
        {
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                try
                {
                    if (SetPersistedEndpoint((uint)pid, role, persistedId))
                    {
                        successes++;
                    }
                }
                catch (Exception ex)
                {
                    // Do not let one helper process prevent the actual audio process from routing.
                    firstUnexpectedFailure ??= ex;
                }
            }
        }

        if (successes > 0)
        {
            return;
        }

        if (firstUnexpectedFailure is not null)
        {
            throw new InvalidOperationException(
                "Windows could not apply the audio route to this application.",
                firstUnexpectedFailure);
        }

        throw new InvalidOperationException(
            "Windows did not find an active audio process to route. Start playback and try again.");
    }

    /// <summary>
    /// Best-effort removal of persisted per-app routing. Some helper PIDs return E_INVALIDARG when
    /// they do not own an audio session. A successful clear for any real audio PID is enough.
    /// </summary>
    public bool TryClearProcesses(IEnumerable<int> processIds)
    {
        var ids = processIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return false;
        }

        var successes = 0;

        foreach (var pid in ids)
        {
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                try
                {
                    if (SetPersistedEndpoint((uint)pid, role, string.Empty))
                    {
                        successes++;
                    }
                }
                catch
                {
                    // Clearing is deliberately best-effort. The caller already moved the app to
                    // the real Windows default endpoint first, so audio is never left stranded.
                }
            }
        }

        return successes > 0;
    }

    public string? GetPersistedEndpointId(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        // Multimedia is the role most normal desktop audio uses. If it is empty, Windows Default is active.
        var raw = GetPersistedEndpoint((uint)processId, ERole.Multimedia);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return ExtractEndpointId(raw);
    }

    private bool SetPersistedEndpoint(uint processId, ERole role, string persistedId)
    {
        var applied = false;

        Invoke((win11, win10) =>
        {
            var handle = HString.Create(persistedId);
            try
            {
                var hr = win11 is not null
                    ? win11.SetPersistedDefaultAudioEndpoint(processId, EDataFlow.Render, role, handle)
                    : win10!.SetPersistedDefaultAudioEndpoint(processId, EDataFlow.Render, role, handle);

                if (hr >= 0)
                {
                    applied = true;
                    return;
                }

                // Windows uses E_INVALIDARG for helper processes that have no audio session.
                // Multi-process apps commonly contain several of these. Ignore this one PID/role
                // and keep trying the rest instead of reporting the whole operation as failed.
                if (hr == EInvalidArg)
                {
                    applied = false;
                    return;
                }

                Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                HString.Delete(handle);
            }
        });

        return applied;
    }

    private string GetPersistedEndpoint(uint processId, ERole role)
    {
        string result = string.Empty;

        Invoke((win11, win10) =>
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var hr = win11 is not null
                    ? win11.GetPersistedDefaultAudioEndpoint(processId, EDataFlow.Render, role, out handle)
                    : win10!.GetPersistedDefaultAudioEndpoint(processId, EDataFlow.Render, role, out handle);

                if (hr >= 0)
                {
                    result = HString.Read(handle);
                }
            }
            finally
            {
                HString.Delete(handle);
            }
        });

        return result;
    }

    private void Invoke(Action<IAudioPolicyConfigFactoryWin11?, IAudioPolicyConfigFactoryWin10?> action)
    {
        EnsureFactory();

        if (_factoryWin11 is not null)
        {
            action(_factoryWin11, null);
            return;
        }

        if (_factoryWin10 is not null)
        {
            action(null, _factoryWin10);
            return;
        }

        throw new InvalidOperationException(
            "Windows per-app audio routing could not be initialized on this PC.");
    }

    private void EnsureFactory()
    {
        if (_factoryProbed)
        {
            return;
        }

        lock (_factoryLock)
        {
            if (_factoryProbed)
            {
                return;
            }

            Exception? lastError = null;

            foreach (var className in CandidateClassNames)
            {
                if (_isWin11)
                {
                    try
                    {
                        var iid = Win11FactoryIid;
                        if (WinRtFactory.GetFactory(className, iid) is IAudioPolicyConfigFactoryWin11 win11)
                        {
                            _factoryWin11 = win11;
                            _factoryProbed = true;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                try
                {
                    var iid = Win10FactoryIid;
                    if (WinRtFactory.GetFactory(className, iid) is IAudioPolicyConfigFactoryWin10 win10)
                    {
                        _factoryWin10 = win10;
                        _factoryProbed = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            _factoryProbed = true;
            throw new InvalidOperationException(
                "Windows did not expose the per-app audio policy interface.",
                lastError);
        }
    }

    private static string BuildPersistedId(string endpointId) => MmdevapiToken + endpointId + RenderSuffix;

    private static string ExtractEndpointId(string persistedId)
    {
        var value = persistedId;
        if (value.StartsWith(MmdevapiToken, StringComparison.OrdinalIgnoreCase))
        {
            value = value[MmdevapiToken.Length..];
        }

        var suffixIndex = value.IndexOf(RenderSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIndex >= 0)
        {
            value = value[..suffixIndex];
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_factoryLock)
        {
            try { if (_factoryWin11 is not null) Marshal.FinalReleaseComObject(_factoryWin11); } catch { }
            try { if (_factoryWin10 is not null) Marshal.FinalReleaseComObject(_factoryWin10); } catch { }
            _factoryWin11 = null;
            _factoryWin10 = null;
        }

        if (_winRtInitialized)
        {
            try { WinRtFactory.UninitializeForCurrentThread(); } catch { }
        }
    }
}
