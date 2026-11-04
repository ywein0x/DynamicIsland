using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;


namespace DynamicIsland;

// Home menu's weather and calendar cards, the month view, and reminders for upcoming events.
public partial class MainWindow
{
    static readonly TimeSpan WeatherMaxAge = TimeSpan.FromMinutes(15);
    static readonly TimeSpan RemindBefore = TimeSpan.FromMinutes(10);
    const int CalendarEventsShown = 3;

    readonly CalendarStore _calendar = new();
    readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly HashSet<string> _reminded = [];

    HomeWeather? _weather;
    string? _weatherCity;
    DateTime _weatherAt;
    bool _weatherLoading;
    List<CalendarEvent> _upcoming = [];
    DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateTime _calendarDay = DateTime.Today;

    void InitCalendar()
    {
        _reminderTimer.Tick += async (_, _) =>
        {
            await LoadUpcomingAsync();
            RemindUpcoming();
        };
        Deactivated += (_, _) =>
        {
            if (_expand == ExpandKind.Calendar)
                Expand(ExpandKind.None);
        };
    }

    async void StartCalendar()
    {
        _reminderTimer.Start();
        await LoadUpcomingAsync();
    }

    // ---------- Weather card ----------

    async void RefreshWeatherCard()
    {
        var city = Settings.WeatherCity;
        var fresh = _weather != null && _weatherCity == city && DateTime.Now - _weatherAt < WeatherMaxAge;
        if (!fresh && !_weatherLoading)
        {
            _weatherLoading = true;
            var weather = await WeatherService.CurrentAsync(city);
            _weatherLoading = false;
            if (weather != null)
            {
                _weather = weather;
                _weatherCity = city;
                _weatherAt = DateTime.Now;
            }
        }

        HomeWeatherCity.Text = _weather?.City ?? city;
        HomeWeatherTemp.Text = _weather != null ? $"{_weather.Now}°" : "–";
        HomeWeatherSky.Text = _weather?.Sky ?? "Bağlantı yok";
        HomeWeatherRange.Text = _weather != null ? $"↑ {_weather.Max}°\n↓ {_weather.Min}°" : "";
    }

    void HomeWeather_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFromHome(ExpandKind.Forecast);
        _ = BuildForecastAsync();
    }

    async Task BuildForecastAsync()
    {
        const int Days = 5;
        var city = _weather?.City ?? Settings.WeatherCity;
        ForecastTitle.Text = $"{city} · {Days} günlük";
        ForecastRows.Children.Clear();
        FitForecast();

        var days = await WeatherService.ForecastAsync(Settings.WeatherCity, Days);
        if (_expand != ExpandKind.Forecast)
            return;
        ForecastRows.Children.Clear();
        if (days == null)
        {
            var failed = new TextBlock { Text = "Hava durumuna şu an ulaşılamıyor", FontSize = 12.5, Margin = new Thickness(2, 0, 0, 6) };
            failed.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            ForecastRows.Children.Add(failed);
        }
        else
        {
            foreach (var day in days)
                ForecastRows.Children.Add(ForecastRow(day));
        }
        FitForecast();
    }

    void FitForecast()
    {
        ForecastContent.Measure(new Size(ForecastPanel.Width, double.PositiveInfinity));
        ForecastPanel.Height = Math.Ceiling(ForecastContent.DesiredSize.Height);
        Refresh();
    }

    FrameworkElement ForecastRow(ForecastDay day)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = day.Date == DateTime.Today ? "Bugün" : day.Date == DateTime.Today.AddDays(1) ? "Yarın" : day.Date.ToString("dddd", HomeCulture);
        var label = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        grid.Children.Add(label);

        var sky = new TextBlock { Text = day.RainPercent >= 40 ? $"{day.Sky}, %{day.RainPercent}" : day.Sky, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
        sky.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        Grid.SetColumn(sky, 1);
        grid.Children.Add(sky);

        var range = new TextBlock { Text = $"{day.Max}° / {day.Min}°", FontSize = 13, Margin = new Thickness(10, 0, 0, 0) };
        range.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        Grid.SetColumn(range, 2);
        grid.Children.Add(range);
        return grid;
    }

    /// <summary>
    /// The island normally never takes focus; while the calendar's text box is open it has to,
    /// or typing would go to the app underneath.
    /// </summary>
    void AcceptKeyboard(bool accept, IInputElement? focus = null)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, accept ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE);
        if (!accept)
            return;
        Activate();
        SetForegroundWindow(hwnd);
        Dispatcher.BeginInvoke(() => Keyboard.Focus(focus), DispatcherPriority.Input);
    }

    /// <summary>The voice assistant is gone: remove its speech models and what it learned.</summary>
    static void RemoveOldAssistantFiles()
    {
        Settings.RemoveOldAssistantData();
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        try
        {
            foreach (var folder in new[] { "speech", "models" })
            {
                var path = Path.Combine(data, folder);
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            File.Delete(Path.Combine(data, "dynamicllm.json"));
        }
        catch
        {
            // Not important.
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---------- Calendar card ----------

    async Task LoadUpcomingAsync()
    {
        _upcoming = await _calendar.GetAsync(DateTime.Today, DateTime.Today.AddDays(30));
        UpdateCalendarCard();
    }

    void UpdateCalendarCard()
    {
        var now = DateTime.Now;
        var next = _upcoming.FirstOrDefault(e => e.AllDay ? e.Start.Date >= now.Date : e.Start >= now);
        HomeCalendarDay.Text = now.ToString("d MMMM, dddd", HomeCulture);
        if (next == null)
        {
            HomeCalendarTime.Text = "Boş";
            HomeCalendarTitle.Text = "Yaklaşan etkinlik yok";
            return;
        }
        var day = next.Start.Date == now.Date ? "" : next.Start.Date == now.Date.AddDays(1) ? "Yarın " : next.Start.ToString("d MMM ", HomeCulture);
        HomeCalendarTime.Text = next.AllDay ? (day.Length > 0 ? day.Trim() : "Bugün") : day + next.Start.ToString("HH:mm");
        HomeCalendarTitle.Text = next.Title;
    }

    void HomeCalendar_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _calendarDay = DateTime.Today;
        OpenFromHome(ExpandKind.Calendar);
        AcceptKeyboard(true, CalendarInput);
        _ = BuildCalendarAsync();
    }

    // ---------- Reminders ----------

    void RemindUpcoming()
    {
        var now = DateTime.Now;
        foreach (var item in _upcoming.Where(e => !e.AllDay && e.Start > now && e.Start - now <= RemindBefore))
        {
            if (!_reminded.Add(item.Id))
                continue;
            var minutes = Math.Max(1, (int)Math.Round((item.Start - now).TotalMinutes));
            ShowTransient(Transient.Calendar, TimeSpan.FromSeconds(7),
                InfoContent(item.Title, $"{minutes} dk sonra · {item.Start:HH:mm}", "", (Color)FindResource("AccentColor")));
        }
    }

    // ---------- Month view ----------

    async Task BuildCalendarAsync()
    {
        var first = _calendarMonth;
        // Weeks start on Monday in Turkey.
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var events = await _calendar.GetAsync(start, start.AddDays(42));
        if (first != _calendarMonth || _expand != ExpandKind.Calendar)
            return;

        CalendarMonth.Text = HomeCulture.TextInfo.ToTitleCase(first.ToString("MMMM yyyy", HomeCulture));
        CalendarWeekdays.Children.Clear();
        CalendarDays.Children.Clear();
        foreach (var name in new[] { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" })
        {
            var header = new TextBlock { Text = name, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            CalendarWeekdays.Children.Add(header);
        }

        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var day = new Button { Style = (Style)FindResource("CalendarDay"), Content = date.Day, Margin = new Thickness(0, 1, 0, 1) };
            if (date == DateTime.Today)
            {
                day.SetResourceReference(BackgroundProperty, "Accent");
                day.Foreground = Brushes.Black;
            }
            else if (date.Month != first.Month)
            {
                day.SetResourceReference(ForegroundProperty, "SecondaryText");
                day.Opacity = 0.55;
            }
            if (date == _calendarDay)
                day.SetResourceReference(BorderBrushProperty, "Fg");
            if (date != DateTime.Today && events.Any(e => e.Start.Date == date))
                day.SetResourceReference(BorderBrushProperty, date == _calendarDay ? "Fg" : "Accent");
            day.Click += (_, _) =>
            {
                _calendarDay = date;
                if (date.Month != _calendarMonth.Month)
                    _calendarMonth = new DateTime(date.Year, date.Month, 1);
                _ = BuildCalendarAsync();
            };
            CalendarDays.Children.Add(day);
        }

        CalendarDayTitle.Text = _calendarDay == DateTime.Today ? "Bugün" : _calendarDay.ToString("d MMMM, dddd", HomeCulture);
        CalendarEvents.Children.Clear();
        var today = events.Where(e => e.Start.Date == _calendarDay).ToList();
        foreach (var item in today.Take(CalendarEventsShown))
            CalendarEvents.Children.Add(EventRow(item));
        if (today.Count > CalendarEventsShown)
        {
            var more = new TextBlock { Text = $"+{today.Count - CalendarEventsShown} etkinlik daha", FontSize = 11.5, Margin = new Thickness(4, 0, 0, 4) };
            more.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            CalendarEvents.Children.Add(more);
        }
        CalendarHint.Text = "14:30 Toplantı yaz, Enter'a bas";

        CalendarContent.Measure(new Size(CalendarPanel.Width, double.PositiveInfinity));
        CalendarPanel.Height = Math.Ceiling(CalendarContent.DesiredSize.Height);
        Refresh();
    }

    FrameworkElement EventRow(CalendarEvent item)
    {
        var grid = new Grid { Margin = new Thickness(4, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = new TextBlock { Text = item.AllDay ? "Tüm gün" : item.Start.ToString("HH:mm"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        grid.Children.Add(time);

        var title = new TextBlock { Text = item.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        // Windows calendar events are read-only here.
        if (item.Local)
        {
            var remove = new Button { Style = (Style)FindResource("LinkButton"), Content = "Sil", FontSize = 11, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            remove.Click += async (_, _) =>
            {
                _calendar.Remove(item.Id);
                await LoadUpcomingAsync();
                await BuildCalendarAsync();
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
        }
        return grid;
    }

    async void CalendarInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Expand(ExpandKind.None);
            return;
        }
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;

        var text = CalendarInput.Text.Trim();
        if (text.Length == 0)
            return;
        var timed = Regex.Match(text, @"^(\d{1,2})[:.](\d{2})\s+(.+)$");
        if (timed.Success && int.Parse(timed.Groups[1].Value) < 24 && int.Parse(timed.Groups[2].Value) < 60)
            _calendar.Add(timed.Groups[3].Value, _calendarDay.AddHours(int.Parse(timed.Groups[1].Value)).AddMinutes(int.Parse(timed.Groups[2].Value)), allDay: false);
        else
            _calendar.Add(text, _calendarDay, allDay: true);

        CalendarInput.Clear();
        await LoadUpcomingAsync();
        await BuildCalendarAsync();
    }

    void CalendarInput_TextChanged(object sender, TextChangedEventArgs e) =>
        CalendarHint.Visibility = CalendarInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void CalendarPrev_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(-1);
        _ = BuildCalendarAsync();
    }

    void CalendarNext_Click(object sender, RoutedEventArgs e)
    {
        _calendarMonth = _calendarMonth.AddMonths(1);
        _ = BuildCalendarAsync();
    }
}
