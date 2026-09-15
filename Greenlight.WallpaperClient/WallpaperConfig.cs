using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.WallpaperClient;

/// <summary>What the desktop is wearing. One of these per thing Greenlight can say.</summary>
/// <remarks>
/// Four, and no more. Greenlight also says whether a build is running, and this client
/// deliberately ignores it: a wallpaper that changed every time somebody pushed would be a
/// full-screen flash several times an hour, which is not a status light, it is a strobe.
/// </remarks>
public enum WallpaperState
{
    /// <summary>No Greenlight to ask. The grey picture, or the desktop left alone — see <see cref="WallpaperConfig.ChangeWhenOff"/>.</summary>
    Off,
    Green,
    Amber,
    Red,
}

/// <summary>
/// How Windows lays the picture out on the desktop. The same six the Personalisation page
/// offers, because they are the six Windows actually has.
/// </summary>
public enum WallpaperFit
{
    /// <summary>Fill the screen, cropping whatever does not fit. What the Greenlight pictures want.</summary>
    Fill,

    /// <summary>The whole picture, letterboxed.</summary>
    Fit,

    /// <summary>Stretched to the screen, aspect ratio and all.</summary>
    Stretch,

    /// <summary>Middle of the screen, at its own size.</summary>
    Centre,

    /// <summary>Repeated from the top left.</summary>
    Tile,

    /// <summary>One picture stretched across every monitor.</summary>
    Span,
}

/// <summary>
/// The wallpapers and how they are hung, read from a JSON file the user can edit. Written out
/// with the defaults the first time it is missing, so "how do I use my own pictures" has an
/// answer that does not involve rebuilding anything.
/// </summary>
/// <remarks>
/// Kept in AppData rather than beside the executable: the executable lives under <c>bin</c>,
/// which a rebuild is entitled to delete, and losing somebody's pictures to a rebuild would be
/// its own small betrayal.
/// </remarks>
public sealed class WallpaperConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Everything this client keeps: the file, the pictures, and the note of what was there before.</summary>
    public static string HomeDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.Wallpaper");

    public static string DefaultPath { get; } = Path.Combine(HomeDirectory, "wallpaper.json");

    /// <summary>
    /// Where the four Greenlight pictures are unpacked on first run, and where somebody
    /// dropping in their own is invited to put them.
    /// </summary>
    public static string PicturesDirectory { get; } = Path.Combine(HomeDirectory, "Pictures");

    // ── the pictures ──────────────────────────────────────────────────────────
    // Empty means "the Greenlight one", which is the picture of that colour unpacked into
    // PicturesDirectory. A path here is taken as it stands — anywhere on the disk, any format
    // Windows will hang on a desktop.

    /// <summary>The picture for no Greenlight at all. Empty for the Greenlight grey one.</summary>
    public string WhenOff { get; set; } = string.Empty;

    /// <summary>Everything passing. Empty for the Greenlight green one.</summary>
    public string WhenGreen { get; set; } = string.Empty;

    /// <summary>A pull request waiting on you. Empty for the Greenlight amber one.</summary>
    public string WhenAmber { get; set; } = string.Empty;

    /// <summary>A broken pipeline. Empty for the Greenlight red one.</summary>
    public string WhenRed { get; set; } = string.Empty;

    /// <summary>How Windows lays the picture out.</summary>
    public WallpaperFit Fit { get; set; } = WallpaperFit.Fill;

    /// <summary>Hang the grey picture when Greenlight is away, rather than leaving the desktop alone.</summary>
    /// <remarks>
    /// On by default. With it off, "Greenlight has stopped" and "this has stopped" look
    /// identical — the desktop simply keeps whatever colour it was last told, for ever, and the
    /// difference is exactly the thing a status light exists to show.
    /// </remarks>
    public bool ChangeWhenOff { get; set; } = true;

    /// <summary>
    /// How long a new colour has to hold before the desktop changes, in seconds.
    /// </summary>
    /// <remarks>
    /// A couple of seconds, because this is a whole screen rather than a tray icon. Greenlight
    /// can go green-amber-green inside a few seconds when a run finishes and the next one
    /// queues, and a desktop flicking through three pictures to end up where it started is
    /// worse than a desktop that took two seconds to tell you the truth.
    /// </remarks>
    public double SettleSeconds { get; set; } = 2;

    /// <summary>
    /// Put the user's own wallpaper back when this quits. On by default, and the reason it is a
    /// setting at all is the person who wants the Greenlight picture to simply be their
    /// wallpaper from now on.
    /// </summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>The configured picture for a given state, or empty for "the Greenlight one".</summary>
    public string PictureFor(WallpaperState state) => state switch
    {
        WallpaperState.Green => WhenGreen,
        WallpaperState.Amber => WhenAmber,
        WallpaperState.Red => WhenRed,
        _ => WhenOff,
    };

    /// <summary>
    /// Load the file, writing the defaults out first if it is not there. A file that cannot be
    /// read or parsed falls back to the defaults rather than refusing to start: this is a desk
    /// toy, and a stray comma should not cost you the whole thing.
    /// </summary>
    public static WallpaperConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new WallpaperConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<WallpaperConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new WallpaperConfig();

            // A file written before one of these existed deserializes it as null, and so does a
            // hand edit that deleted a line. Neither should be a crash on the next snapshot —
            // and null here would be read as "a picture at the path null", not as "the default".
            loaded.WhenOff ??= string.Empty;
            loaded.WhenGreen ??= string.Empty;
            loaded.WhenAmber ??= string.Empty;
            loaded.WhenRed ??= string.Empty;

            // Clamped on the way in, not only on the way out. The file is hand-editable, and a
            // settle of 90000 should give you a slow desktop rather than one that never changes
            // again and no way to tell why.
            loaded.SettleSeconds = Math.Clamp(loaded.SettleSeconds, 0, 60);

            return loaded;
        }
        catch
        {
            return new WallpaperConfig();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // A toy that cannot write its config still runs perfectly well on the defaults.
        }
    }

    /// <summary>Take on everything from a freshly-read file, in place.</summary>
    /// <remarks>
    /// Copied into this instance rather than swapping it for the new one: the tray is holding
    /// this object, and it is the tray's menu that has to keep agreeing with the file.
    /// </remarks>
    public void CopyFrom(WallpaperConfig other)
    {
        WhenOff = other.WhenOff;
        WhenGreen = other.WhenGreen;
        WhenAmber = other.WhenAmber;
        WhenRed = other.WhenRed;
        Fit = other.Fit;
        ChangeWhenOff = other.ChangeWhenOff;
        SettleSeconds = other.SettleSeconds;
        RestoreOnExit = other.RestoreOnExit;
    }
}
