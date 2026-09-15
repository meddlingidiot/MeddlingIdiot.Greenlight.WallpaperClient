using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Greenlight.WallpaperClient;

/// <summary>
/// The mascot in the notification area, and the menu hanging off him: the only part of this toy
/// a person can click.
/// </summary>
/// <remarks>
/// <para>
/// There is no window at all here — the wallpaper is the UI — so the tray is not merely the
/// convenient place for the settings, it is the only place. Every setting in
/// <see cref="WallpaperConfig"/> that can be changed while this is running is reachable from
/// here, and each change is written straight back to the file, so the menu and the JSON are
/// always the same settings.
/// </para>
/// <para>
/// Avalonia's own <see cref="TrayIcon"/> rather than a tray library, because the sample is meant
/// to be readable — and because a sample that drags in a dependency to draw one icon is making a
/// point nobody asked for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WallpaperTray : IDisposable
{
    private static readonly Uri IconUri = new("avares://Greenlight.WallpaperClient/Assets/MeddlingIdiot.ico");

    private readonly WallpaperConfig _config;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _running;
    private readonly NativeMenuItem _giveBack;
    private readonly NativeMenuItem _startup;

    public WallpaperTray(WallpaperConfig config)
    {
        _config = config;

        _status = new NativeMenuItem { Header = "Waiting for Greenlight…", IsEnabled = false };

        _running = new NativeMenuItem
        {
            Header = "Greenlight on the desktop",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = true,
        };
        _running.Click += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);

        // Greyed out when there is nothing to give back, which is the usual state of it: either
        // this has not borrowed the desktop yet, or it already handed it back. An item that
        // stayed clickable and did nothing would be the menu implying the wallpaper is in here
        // somewhere when it is not.
        _giveBack = new NativeMenuItem { Header = "Give me my wallpaper back" };
        _giveBack.Click += (_, _) => OnGiveItBack?.Invoke();

        // Read from the registry rather than from a setting of ours, every time it is shown: the
        // user can turn this off in Task Manager's Startup tab, and a tick remembering what we
        // last wrote would then be telling them the opposite of the truth.
        _startup = Check("Start with Windows", WindowsStartup.IsEnabled, value => WindowsStartup.Set(value));

        var menu = BuildMenu();

        // The top-level items are not inside a submenu, so nothing else re-ticks them.
        menu.Opening += (_, _) =>
        {
            _startup.IsChecked = WindowsStartup.IsEnabled();
            _giveBack.IsEnabled = HasSomethingToGiveBack?.Invoke() ?? false;
        };

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "Greenlight wallpaper",
            Menu = menu,
            IsVisible = true,
        };

        // The one thing a left click can mean here. There is no main window to open, and a tray
        // icon that does nothing at all when clicked reads as a hung one.
        _tray.Clicked += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);
    }

    /// <summary>Whether the desktop is currently following Greenlight.</summary>
    public Func<bool>? IsRunning { get; set; }

    /// <summary>Whether there is a wallpaper of the user's own waiting to be handed back.</summary>
    public Func<bool>? HasSomethingToGiveBack { get; set; }

    /// <summary>Start following Greenlight, or stop and hand the desktop back.</summary>
    public Action<bool>? OnSetRunning { get; set; }

    /// <summary>A setting changed that wants the desktop looking at again.</summary>
    public Action? OnConfigChanged { get; set; }

    /// <summary>Re-read the file, for paths changed by hand.</summary>
    public Action? OnReloadConfig { get; set; }

    /// <summary>Hand the wallpaper back now, without stopping.</summary>
    public Action? OnGiveItBack { get; set; }

    /// <summary>Write the four Greenlight pictures back over whatever is in the folder.</summary>
    public Action? OnRestorePictures { get; set; }

    public Action? OnQuit { get; set; }

    /// <summary>Say what the desktop is doing, in the tooltip and at the top of the menu.</summary>
    /// <param name="state">What Greenlight last said.</param>
    /// <param name="hanging">Whether the desktop is actually wearing that answer.</param>
    public void ShowState(WallpaperState state, bool hanging)
    {
        var running = IsRunning?.Invoke() ?? true;

        _status.Header = state switch
        {
            WallpaperState.Green => "Greenlight: green — all passing",
            WallpaperState.Amber => "Greenlight: yellow — a pull request wants you",
            WallpaperState.Red => "Greenlight: red — a pipeline is broken",
            _ => "Greenlight not running — nothing claimed",
        };

        // Said out loud rather than left to be inferred from a desktop that has not changed.
        // "Greenlight is red and your wallpaper is not" has two possible causes and the user
        // can only do something about one of them.
        if (running && !hanging) _status.Header += " (desktop left alone)";

        _running.IsChecked = running;

        _tray.ToolTipText = running
            ? $"Greenlight wallpaper — {Short(state)}"
            : "Greenlight wallpaper — off";
    }

    private static string Short(WallpaperState state) => state switch
    {
        WallpaperState.Green => "green",
        WallpaperState.Amber => "yellow",
        WallpaperState.Red => "red",
        _ => "not connected",
    };

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }

    private void SetRunning(bool running)
    {
        OnSetRunning?.Invoke(running);
        _running.IsChecked = running;
        _tray.ToolTipText = running ? "Greenlight wallpaper" : "Greenlight wallpaper — off";
    }

    private NativeMenu BuildMenu() =>
    [
        _status,
        new NativeMenuItemSeparator(),
        _running,
        new NativeMenuItemSeparator(),
        Submenu("How it is laid out",
            Laid("Fill the screen", WallpaperFit.Fill),
            Laid("Fit the whole picture", WallpaperFit.Fit),
            Laid("Stretch it", WallpaperFit.Stretch),
            Laid("Centre it", WallpaperFit.Centre),
            Laid("Tile it", WallpaperFit.Tile),
            Laid("Span every monitor", WallpaperFit.Span)),
        Submenu("How long a colour has to hold",
            Settle("Change at once", 0),
            Settle("After 2 seconds", 2),
            Settle("After 10 seconds", 10),
            Settle("After a minute", 60)),
        Check("Grey wallpaper when Greenlight is away",
            () => _config.ChangeWhenOff,
            value =>
            {
                _config.ChangeWhenOff = value;
                Persist();
                OnConfigChanged?.Invoke();
            }),
        Check("Put my wallpaper back when this quits",
            () => _config.RestoreOnExit,
            value =>
            {
                _config.RestoreOnExit = value;
                Persist();
            }),
        _startup,
        new NativeMenuItemSeparator(),
        _giveBack,
        Item("Open the pictures folder", OpenPictures),
        Item("Put the Greenlight pictures back", () => OnRestorePictures?.Invoke()),
        new NativeMenuItemSeparator(),
        Item("Edit the file…", EditConfig),
        Item("Reload the file", () => OnReloadConfig?.Invoke()),
        new NativeMenuItemSeparator(),
        Item("Quit", () => OnQuit?.Invoke()),
    ];

    // ── the settings ──────────────────────────────────────────────────────────
    // Deliberately not here: the four pictures. A file picker per colour is four dialogs and a
    // lot of window for a toy with no windows, so the menu's job there is to open the folder and
    // to make the file findable.

    private NativeMenuItem Laid(string header, WallpaperFit fit) =>
        Choice(header, () => _config.Fit == fit, () =>
        {
            _config.Fit = fit;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Settle(string header, double seconds) =>
        Choice(header, () => Math.Abs(_config.SettleSeconds - seconds) < 0.001, () =>
        {
            _config.SettleSeconds = seconds;
            Persist();
        });

    // ── Menu plumbing ─────────────────────────────────────────────────────────
    // Each option asks the config what it should look like when the menu opens rather than being
    // ticked once at startup: the file is editable by hand and reloadable from this very menu, so
    // anything remembering its own state would start lying the moment it was.

    private static NativeMenuItem Check(string header, Func<bool> isOn, Action<bool> set)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isOn(),
        };

        item.Click += (_, _) =>
        {
            set(!isOn());
            item.IsChecked = isOn();
        };

        return item;
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Submenu(string header, params NativeMenuItem[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items) menu.Add(item);

        void Retick()
        {
            foreach (var item in items)
                if (item.CommandParameter is Func<bool> isChosen)
                    item.IsChecked = isChosen();
        }

        // Twice, because neither moment is reliable on its own: picking an option has to move
        // the tick off the old one straight away, and opening the menu has to account for the
        // file having been edited by hand behind its back.
        foreach (var item in items) item.Click += (_, _) => Retick();
        menu.Opening += (_, _) => Retick();

        return new NativeMenuItem { Header = header, Menu = menu };
    }

    private static NativeMenuItem Choice(string header, Func<bool> isChosen, Action choose)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChosen(),

            // Parked here rather than in a dictionary: the menu owns its items, and a second
            // collection to keep in step with it is a second thing to get wrong.
            CommandParameter = isChosen,
        };

        item.Click += (_, _) => choose();
        return item;
    }

    private void Persist() => _config.Save();

    /// <summary>
    /// Open the folder the four pictures live in. The shortest route to "use my own picture":
    /// drop it in here and write its name in the file.
    /// </summary>
    private void OpenPictures() => Show(WallpaperConfig.PicturesDirectory);

    /// <summary>Open <c>wallpaper.json</c> in whatever the machine opens JSON with.</summary>
    private void EditConfig()
    {
        // It is written out on first run, but a deleted file should still open something rather
        // than nothing.
        if (!File.Exists(WallpaperConfig.DefaultPath)) _config.Save();

        Show(WallpaperConfig.DefaultPath);
    }

    private static void Show(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // No editor associated with .json, or the shell refused. A desk toy does not get to
            // interrupt anyone over it.
        }
    }
}
