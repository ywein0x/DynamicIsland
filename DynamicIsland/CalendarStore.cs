using System.IO;
using System.Text.Json;
using Windows.ApplicationModel.Appointments;

namespace DynamicIsland;

public sealed record CalendarEvent(string Id, string Title, DateTime Start, bool AllDay, bool Local);

/// <summary>
/// Events shown on the island: the Windows calendar (read-only) merged with events added
/// on the island itself, which are kept in a file on this PC.
/// </summary>
public sealed class CalendarStore
{
    AppointmentStore? _windows;
    bool _windowsTried;
    List<CalendarEvent>? _local;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "calendar.json");

    public async Task<List<CalendarEvent>> GetAsync(DateTime from, DateTime to)
    {
        var events = Local().Where(e => e.Start >= from && e.Start < to).ToList();
        events.AddRange(await WindowsEventsAsync(from, to));
        return [.. events.OrderBy(e => e.Start)];
    }

    public void Add(string title, DateTime start, bool allDay)
    {
        Local().Add(new CalendarEvent(Guid.NewGuid().ToString("N"), title, start, allDay, true));
        Save();
    }

    public void Remove(string id)
    {
        Local().RemoveAll(e => e.Id == id);
        Save();
    }

    async Task<IEnumerable<CalendarEvent>> WindowsEventsAsync(DateTime from, DateTime to)
    {
        try
        {
            if (!_windowsTried)
            {
                _windowsTried = true;
                _windows = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            }
            if (_windows == null)
                return [];

            var options = new FindAppointmentsOptions();
            options.FetchProperties.Add(AppointmentProperties.Subject);
            options.FetchProperties.Add(AppointmentProperties.StartTime);
            options.FetchProperties.Add(AppointmentProperties.AllDay);
            var found = await _windows.FindAppointmentsAsync(from, to - from, options);
            return found.Select(a => new CalendarEvent(a.LocalId + a.StartTime.Ticks, a.Subject, a.StartTime.LocalDateTime, a.AllDay, false));
        }
        catch
        {
            return []; // no access on this PC: island events still work
        }
    }

    List<CalendarEvent> Local()
    {
        if (_local != null)
            return _local;
        try
        {
            _local = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<CalendarEvent>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch
        {
            _local = [];
        }
        return _local;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_local));
        }
        catch
        {
            // Nothing else to do; the event stays until the app closes.
        }
    }
}
