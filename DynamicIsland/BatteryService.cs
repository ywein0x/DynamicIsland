using System.Windows;
using Windows.Devices.Power;
using Windows.System.Power;

namespace DynamicIsland;

/// <summary>
/// Raises <see cref="ChargingStarted"/> when a charger is plugged in and <see cref="LowBattery"/>
/// at 20% and 10% while on battery. Both carry the battery percent and fire on the UI thread.
/// </summary>
public sealed class BatteryService
{
    bool _charging;
    int _warnedLevel = 100;   // lowest threshold already warned about since the last charge

    public event Action<int>? ChargingStarted;
    public event Action<int>? LowBattery;

    /// <summary>Current charge, or null on machines without a battery.</summary>
    public int? CurrentPercent => Read(Battery.AggregateBattery.GetReport()).Percent;

    public void Start()
    {
        var battery = Battery.AggregateBattery;
        var (charging, percent) = Read(battery.GetReport());
        _charging = charging;
        // Don't warn on startup about a level the user already knows about.
        if (percent is { } p)
            _warnedLevel = Threshold(p);
        battery.ReportUpdated += (sender, _) => Application.Current.Dispatcher.InvokeAsync(() => Check(sender));
    }

    void Check(Battery battery)
    {
        var (charging, percent) = Read(battery.GetReport());
        if (charging && !_charging && percent is { } p)
            ChargingStarted?.Invoke(p);
        _charging = charging;

        if (charging)
        {
            _warnedLevel = 100;
        }
        else if (percent is { } level && Threshold(level) < _warnedLevel)
        {
            _warnedLevel = Threshold(level);
            LowBattery?.Invoke(level);
        }
    }

    static int Threshold(int percent) => percent <= 10 ? 10 : percent <= 20 ? 20 : 100;

    static (bool Charging, int? Percent) Read(BatteryReport report)
    {
        if (report.Status == BatteryStatus.NotPresent)
            return (false, null);

        int? percent = report.FullChargeCapacityInMilliwattHours is int full && full > 0
                       && report.RemainingCapacityInMilliwattHours is int remaining
            ? (int)Math.Round(remaining * 100.0 / full)
            : null;
        return (report.Status == BatteryStatus.Charging, percent);
    }
}
