using System.Runtime.Versioning;
using System.Text.Json;

namespace Greenlight.WallpaperClient;

/// <summary>
/// The desktop itself: what was on it before this started, what is on it now, and putting the
/// first back.
/// </summary>
/// <remarks>
/// <para>
/// The one part of this toy that changes something the user owns. Every other Greenlight client
/// draws a window of its own and takes it away again on exit; this one borrows a setting, and
/// borrowing it properly means writing down what was there before touching anything, and
/// handing it back on the way out.
/// </para>
/// <para>
/// The note goes on the disk rather than into a field, because the case that matters is the one
/// where this process never gets to run its exit: a crash, a hard reboot, a task manager. The
/// next start finds the note still there, leaves it alone, and can still give back the right
/// wallpaper — which is the difference between a desk toy and a thing that eats your wallpaper
/// once and cannot say which one it was.
/// </para>
/// <para>
/// The picture itself is copied aside rather than only noted, and that is not belt and braces.
/// Windows usually answers "what is the wallpaper" with its own re-encoded copy under
/// <c>Themes\TranscodedWallpaper</c>, and it overwrites that copy with whatever is hung next —
/// so a note holding that path would, about five seconds later, be a note pointing at our own
/// green one. The user's wallpaper would be gone, and the note would swear it was not.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DesktopWallpaper
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _home;
    private readonly string _notePath;
    private readonly string _pictures;
    private readonly Func<string, string, string, bool> _hang;

    /// <summary>
    /// The last picture this hung, and how. Kept so an unchanged colour is not a full-screen
    /// repaint: Greenlight re-sends its snapshot on every poll, and the answer is usually the
    /// same one.
    /// </summary>
    /// <remarks>
    /// The fit is half of this and not an afterthought. Picking a new layout off the menu is a
    /// change to how the same picture is hung, so a check on the path alone would quietly do
    /// nothing at all — the one place in this app where the user is watching for an instant
    /// result, because they just clicked for it.
    /// </remarks>
    private string? _hanging;

    private WallpaperFit _hangingFit;

    /// <param name="home">Where the note and the copy of their wallpaper go.</param>
    /// <param name="pictures">Where the Greenlight pictures were unpacked.</param>
    /// <param name="hang">
    /// How to actually hang a picture, given a path and the two registry strings. A parameter so
    /// this can be tested, and not an optional nicety: the alternative is a test suite that
    /// changes the wallpaper of whoever runs it, which is exactly the accident this class exists
    /// to prevent. Defaults to the real thing.
    /// </param>
    public DesktopWallpaper(string home, string pictures, Func<string, string, string, bool>? hang = null)
    {
        _home = home;
        _notePath = Path.Combine(home, "borrowed.json");
        _pictures = pictures;
        _hang = hang ?? WallpaperNative.Hang;
    }

    /// <summary>What the desktop was wearing before this borrowed it.</summary>
    /// <param name="Path">Our own copy of the picture, kept where Windows cannot overwrite it.</param>
    /// <param name="Style">Whatever was in <c>WallpaperStyle</c>, to be put back verbatim.</param>
    /// <param name="Tile">Whatever was in <c>TileWallpaper</c>, to be put back verbatim.</param>
    public sealed record Note(string Path, string? Style, string? Tile);

    /// <summary>Whether there is a wallpaper of the user's own waiting to be given back.</summary>
    public bool HasSomethingToGiveBack => Read() is not null;

    /// <summary>
    /// Write down what the desktop is wearing, if it has not already been written down.
    /// </summary>
    /// <returns>
    /// Whether it is safe to start hanging pictures. <c>false</c> means the user's wallpaper
    /// could not be put somewhere it can be got back from, and the honest thing to do with a
    /// wallpaper you cannot give back is not to borrow it.
    /// </returns>
    public bool RememberTheirs()
    {
        // An existing note is from a run that never got to give it back. It is the older and
        // therefore the truer answer, and overwriting it now would replace the user's wallpaper
        // with one of ours in the only record that still has it.
        if (Read() is not null) return true;

        var current = WallpaperNative.Current();

        // Nothing to lose: a desktop set to a solid colour, or a Windows that would not say.
        if (current is null) return true;

        // Already wearing one of ours — a crash whose note was lost. There is nothing of theirs
        // left on the desktop to write down, and writing down ours would be worse than nothing:
        // "give it back" would then cheerfully hang the green one and call it done.
        if (WallpaperSet.IsOneOfOurs(current, _pictures)) return true;

        var (style, tile) = WallpaperNative.CurrentFit();

        var kept = CopyAside(current);
        if (kept is null)
        {
            // The copy failed. A path is still worth noting if it is a real file of theirs that
            // nothing here will overwrite — but not if it is Windows' own working copy, which
            // is exactly the file the first picture we hang will overwrite.
            if (WallpaperSet.IsWindowsOwnCopy(current)) return false;
            kept = current;
        }

        return Write(new Note(kept, style, tile));
    }

    /// <summary>Hang a picture, unless it is already hanging that way. Says whether Windows took it.</summary>
    /// <param name="path">The picture.</param>
    /// <param name="fit">How to lay it out.</param>
    /// <param name="force">
    /// Hang it again even if nothing here has changed. For the case where the file at that path
    /// is not the file that was there before — "Put the Greenlight pictures back" writes new
    /// bytes to the same four names, and without this the desktop would keep showing the
    /// painted-over one until the colour happened to change.
    /// </param>
    public bool Hang(string path, WallpaperFit fit, bool force = false)
    {
        var same = string.Equals(path, _hanging, StringComparison.OrdinalIgnoreCase) && fit == _hangingFit;
        if (same && !force) return false;

        var (style, tile) = WallpaperNative.Spell(fit);
        if (!_hang(path, style, tile)) return false;

        _hanging = path;
        _hangingFit = fit;
        return true;
    }

    /// <summary>
    /// Put the user's own wallpaper back and tear up the note. Says whether there was anything
    /// to put back.
    /// </summary>
    public bool GiveItBack()
    {
        var note = Read();
        if (note is null) return false;

        // The defaults are Fill and not-tiled, for a note written before there was anything in
        // those two registry values to read — a fresh profile that has never had a wallpaper.
        _hang(note.Path, note.Style ?? "10", note.Tile ?? "0");

        // The note is torn up whether or not Windows took the picture. One that cannot be
        // honoured — a wallpaper that was on a drive nobody has plugged in since — is not worth
        // keeping and offering again at every start for ever.
        //
        // The copy beside it is left alone. It is one file, the next borrow overwrites it, and
        // deleting a picture Windows may still be re-encoding is a way to get half a wallpaper.
        _hanging = null;
        Forget();
        return true;
    }

    /// <summary>
    /// Keep our own copy of the user's wallpaper, somewhere Windows has no reason to touch.
    /// </summary>
    private string? CopyAside(string source)
    {
        try
        {
            if (!File.Exists(source)) return null;

            // Its own extension, because Windows decides how to read a wallpaper by looking at
            // one. TranscodedWallpaper has no extension at all and is a JPEG underneath, which
            // Windows is perfectly happy to be handed back as .jpg.
            var extension = Path.GetExtension(source);
            if (string.IsNullOrEmpty(extension)) extension = ".jpg";

            var kept = Path.Combine(_home, "borrowed" + extension);

            Directory.CreateDirectory(_home);
            File.Copy(source, kept, overwrite: true);

            return kept;
        }
        catch
        {
            return null;
        }
    }

    private Note? Read()
    {
        try
        {
            if (!File.Exists(_notePath)) return null;

            var note = JsonSerializer.Deserialize<Note>(File.ReadAllText(_notePath), Json);
            return string.IsNullOrWhiteSpace(note?.Path) ? null : note;
        }
        catch
        {
            return null;
        }
    }

    private bool Write(Note note)
    {
        try
        {
            Directory.CreateDirectory(_home);
            File.WriteAllText(_notePath, JsonSerializer.Serialize(note, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Forget()
    {
        try
        {
            File.Delete(_notePath);
        }
        catch
        {
            // It will be read again next start, and giving the same wallpaper back twice is
            // harmless.
        }
    }
}
