using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.WallpaperClient;

/// <summary>
/// The whole of the Greenlight integration, which is the point of the sample: attach, translate
/// the colour, and never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// <para>
/// The tray icon and the Win32 that hangs a picture are ordinary Windows and have nothing to do
/// with Greenlight — the integration is still the twenty-odd lines in
/// <see cref="StartWatchingGreenlight"/>.
/// </para>
/// <para>
/// One thing this client deliberately does not use: <see cref="GreenlightSnapshot.IsBuilding"/>.
/// Every other client does something with it — the halo breathes, the lamp bubbles — because
/// they draw a small thing in a corner. This one repaints the whole screen, and a wallpaper that
/// changed every time somebody pushed would not be a status light, it would be a strobe.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    private GreenlightClient? _greenlight;
    private WallpaperTray? _tray;
    private WallpaperConfig _config = new();
    private DesktopWallpaper? _desktop;
    private DispatcherTimer? _settling;

    /// <summary>
    /// The last thing Greenlight said. Held here rather than only on the desktop because the
    /// desktop comes and goes — switched off, handed back, reloaded — and a wallpaper that came
    /// back green after a restart would be the toy lying.
    /// </summary>
    private WallpaperState _state = WallpaperState.Off;

    private bool _running = true;

    /// <summary>
    /// Whether the user's own wallpaper was safely written down. When it was not, this client
    /// does nothing at all — see <see cref="Borrow"/>.
    /// </summary>
    private bool _mayBorrow;

    /// <summary>Whether the desktop is actually wearing the answer. What the tray reports.</summary>
    private bool _hanging;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // There is no window in this app at all — the wallpaper is the window. On the
            // default setting Avalonia would shut down the moment it noticed.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = WallpaperConfig.Load();

            // If Windows is set to start this, make sure it is still pointed at the right
            // executable. An update moves the versioned copy out from under an older
            // registration, and the symptom is the desktop silently not following Greenlight one
            // morning — weeks after anybody touched the setting.
            WindowsStartup.Refresh();

            WallpaperPictures.Unpack(WallpaperConfig.PicturesDirectory);

            _desktop = new DesktopWallpaper(WallpaperConfig.HomeDirectory, WallpaperConfig.PicturesDirectory);
            _mayBorrow = _desktop.RememberTheirs();

            _tray = new WallpaperTray(_config)
            {
                IsRunning = () => _running,
                HasSomethingToGiveBack = () => _desktop?.HasSomethingToGiveBack ?? false,
                OnSetRunning = SetRunning,
                OnConfigChanged = () => Settle(now: true),
                OnReloadConfig = ReloadConfig,
                OnGiveItBack = GiveItBack,
                OnRestorePictures = RestorePictures,
                OnQuit = () => desktop.Shutdown(),
            };

            Settle(now: true);
            StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                // Before the tray goes, and before the SDK: this is the only promise this client
                // makes about something the user owns, and an exit path that skipped it because
                // a pipe was slow to close would be the one bug worth caring about here.
                if (_config.RestoreOnExit) _desktop?.GiveItBack();

                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both of these arrive on a background thread — the SDK says so, loudly, and this is what
        // it means in practice. Hanging a wallpaper from the pipe's thread would be a Win32 call
        // racing the settle timer on the UI thread.
        _greenlight.Changed += (_, e) => Apply(Translate(e.Snapshot.Status));
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            // Anything other than Connected means we have nothing to show, and going grey is
            // more honest than leaving a green desktop up on stale data.
            if (e.Availability != GreenlightAvailability.Connected) Apply(WallpaperState.Off);
        };

        // Deliberately not awaited and deliberately not guarded: StartAsync returns as soon as the
        // background loop is running, and an absent Greenlight is not an error. The desktop sits
        // grey until one turns up, then takes its colour on its own.
        _ = _greenlight.StartAsync();
    }

    private static WallpaperState Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => WallpaperState.Green,
        GreenlightStatus.Yellow => WallpaperState.Amber,
        GreenlightStatus.Red => WallpaperState.Red,
        _ => WallpaperState.Off,
    };

    private void Apply(WallpaperState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_state == state) return;

            _state = state;
            Settle(now: false);
        });

    /// <summary>
    /// Wait for the colour to hold, then hang it. A setting changed from the menu goes up at
    /// once — the user is looking at the menu and has just asked for it.
    /// </summary>
    private void Settle(bool now, bool force = false)
    {
        _settling?.Stop();
        _settling = null;

        var wait = now ? 0 : _config.SettleSeconds;

        if (wait <= 0)
        {
            Borrow(force);
            return;
        }

        _settling = new DispatcherTimer { Interval = TimeSpan.FromSeconds(wait) };
        _settling.Tick += (_, _) =>
        {
            _settling?.Stop();
            _settling = null;
            Borrow(force);
        };
        _settling.Start();

        // The menu says what Greenlight is saying straight away, even while the desktop is still
        // catching up — but it does not guess at the second half of that. Whether the desktop is
        // wearing the answer stays whatever it last actually was until Borrow finds out.
        _tray?.ShowState(_state, _hanging);
    }

    /// <summary>Put the picture for the current colour on the desktop, if there is one and it is wanted.</summary>
    private void Borrow(bool force = false)
    {
        _hanging = false;

        // Every one of these is a reason to leave the desktop exactly as it is, and the tray
        // says so rather than leaving the user to wonder why red did not arrive.
        if (_running && _desktop is not null && _mayBorrow && (_state != WallpaperState.Off || _config.ChangeWhenOff))
        {
            var picture = WallpaperSet.Resolve(
                _config, _state, WallpaperConfig.PicturesDirectory, File.Exists);

            if (picture is not null)
            {
                _desktop.Hang(picture, _config.Fit, force);
                _hanging = true;
            }
        }

        _tray?.ShowState(_state, _hanging);
    }

    private void SetRunning(bool running)
    {
        _running = running;

        if (running)
        {
            // Back on: the desktop is the user's own again, so this is a fresh borrow and the
            // note has to be written afresh.
            _mayBorrow = _desktop?.RememberTheirs() ?? false;
            Settle(now: true);
            return;
        }

        _settling?.Stop();
        _settling = null;

        // Switched off means switched off — not "stops updating and keeps whichever colour it
        // happened to be on". The other clients take their window away; this one has to hand
        // back what it borrowed to do the same thing.
        _desktop?.GiveItBack();
        _hanging = false;
        _tray?.ShowState(_state, hanging: false);
    }

    private void GiveItBack()
    {
        _desktop?.GiveItBack();
        _hanging = false;
        _tray?.ShowState(_state, hanging: false);
    }

    private void RestorePictures()
    {
        WallpaperPictures.Restore(WallpaperConfig.PicturesDirectory);

        // Forced, because the path has not changed — only the bytes at it. Without this the
        // desktop would go on showing the picture that was just painted over, until the colour
        // happened to change.
        Settle(now: true, force: true);
    }

    /// <summary>Re-read the file, for pictures changed by hand while this was running.</summary>
    private void ReloadConfig()
    {
        _config.CopyFrom(WallpaperConfig.Load());

        // Also forced: the usual reason somebody reloads is that they have just replaced a
        // picture in the folder, which the path alone cannot see.
        Settle(now: true, force: true);
    }
}
