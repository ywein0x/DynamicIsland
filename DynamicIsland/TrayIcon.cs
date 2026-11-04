using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

/// <summary>
/// Icon in the notification area (it lands in the hidden icons overflow by default).
/// Left click opens Settings; right click has the main actions, including quitting.
/// </summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _autostart;
    readonly IntPtr _iconHandle;

    public TrayIcon(Action openHome, Action openSettings, Action openClipboard, Action openShelf, Action checkUpdates, Action exit)
    {
        _iconHandle = DrawIcon();
        _autostart = new Forms.ToolStripMenuItem("Windows ile başlat") { CheckOnClick = true };
        _autostart.Click += (_, _) => Autostart.IsEnabled = _autostart.Checked;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ana menü", null, (_, _) => openHome());
        menu.Items.Add("Ayarlar", null, (_, _) => openSettings());
        menu.Items.Add("Pano geçmişi", null, (_, _) => openClipboard());
        menu.Items.Add("Dosya rafı", null, (_, _) => openShelf());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_autostart);
        menu.Items.Add("Güncellemeleri denetle", null, (_, _) => checkUpdates());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => exit());
        menu.Opening += (_, _) => _autostart.Checked = Autostart.IsEnabled;

        _icon = new Forms.NotifyIcon
        {
            Text = "Dynamic Island",
            Icon = Icon.FromHandle(_iconHandle),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                openSettings();
        };
    }

    /// <summary>A small island: black pill with a light rim so it reads on light and dark taskbars.</summary>
    static IntPtr DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pill = new GraphicsPath();
            const float x = 3, y = 10, w = 26, h = 12;
            pill.AddArc(x, y, h, h, 90, 180);
            pill.AddArc(x + w - h, y, h, h, 270, 180);
            pill.CloseFigure();
            g.FillPath(Brushes.Black, pill);
            using var rim = new Pen(Color.FromArgb(220, 255, 255, 255), 1.6f);
            g.DrawPath(rim, pill);
            using var dot = new SolidBrush(Color.FromArgb(48, 209, 88));
            g.FillEllipse(dot, 20, 13.5f, 5, 5);
        }
        return bitmap.GetHicon();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
