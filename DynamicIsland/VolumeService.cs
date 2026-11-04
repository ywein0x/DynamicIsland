using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIsland;

/// <summary>
/// Raises <see cref="Changed"/> (level 0..1, muted) on the UI thread when the system volume changes.
/// Follows the default output device, so switching to headphones keeps working.
/// </summary>
public sealed class VolumeService : IDisposable, IMMNotificationClient
{
    readonly MMDeviceEnumerator _enumerator = new();
    MMDevice? _device;

    public event Action<float, bool>? Changed;

    public void Start()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
        AttachDefaultDevice();
    }

    void AttachDefaultDevice()
    {
        DetachDevice();
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
        catch
        {
            _device = null; // no output device right now
        }
    }

    void DetachDevice()
    {
        if (_device == null)
            return;
        _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device.Dispose();
        _device = null;
    }

    /// <summary>The default output's master volume (0..1).</summary>
    public float Level => _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;

    /// <summary>How loud a program is playing right now (0..1), e.g. other people talking in Discord.</summary>
    public float SessionPeak(string processPrefix)
    {
        try
        {
            var manager = _device?.AudioSessionManager;
            if (manager == null)
                return 0;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            float peak = 0;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                var pid = (int)session.GetProcessID;
                if (!_processNames.TryGetValue(pid, out var name))
                {
                    try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
                    catch { name = ""; }
                    _processNames[pid] = name;
                }
                if (name.StartsWith(processPrefix, StringComparison.OrdinalIgnoreCase))
                    peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
            }
            return peak;
        }
        catch
        {
            return 0;
        }
    }

    readonly Dictionary<int, string> _processNames = [];

    /// <summary>Sets the default output's master volume (0..1).</summary>
    public void SetLevel(float level)
    {
        if (_device != null)
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0, 1);
    }

    void OnVolumeNotification(AudioVolumeNotificationData data) =>
        Application.Current.Dispatcher.InvokeAsync(() => Changed?.Invoke(data.MasterVolume, data.Muted));

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
            Application.Current.Dispatcher.InvokeAsync(AttachDefaultDevice);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { /* never registered */ }
        DetachDevice();
        _enumerator.Dispose();
    }
}
