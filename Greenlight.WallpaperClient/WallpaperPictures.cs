using System.Reflection;

namespace Greenlight.WallpaperClient;

/// <summary>
/// The four Greenlight pictures, unpacked out of the executable and onto the disk where Windows
/// can see them.
/// </summary>
/// <remarks>
/// <para>
/// Windows hangs a wallpaper from a path, not from a stream, so the pictures cannot simply stay
/// embedded. Unpacking them into AppData rather than reading them out of the install folder also
/// means they survive an update — and, more to the point, that "customise them" can be as small
/// an instruction as "open this folder and replace the picture".
/// </para>
/// <para>
/// Nothing here overwrites a file that is already on the disk unless it is explicitly asked to.
/// Somebody who painted their own <c>greenlight-red.png</c> should not have it quietly replaced
/// on the next start — that is what <see cref="Restore"/> is for, and it is a menu item they
/// have to choose.
/// </para>
/// </remarks>
public static class WallpaperPictures
{
    /// <summary>
    /// Plain embedded resources rather than Avalonia's <c>avares:</c> ones, and the difference
    /// matters. Avalonia's asset loader only works once the framework has been set up, which
    /// makes these four files untestable and makes the order things happen in at startup load
    /// bearing. Nothing here is ever drawn by Avalonia — they are files being copied onto a
    /// disk — so they have no business going through a UI toolkit to get there.
    /// </summary>
    private const string Prefix = "Wallpapers.";

    private static readonly WallpaperState[] All =
        [WallpaperState.Off, WallpaperState.Green, WallpaperState.Amber, WallpaperState.Red];

    /// <summary>Put any missing Greenlight picture on the disk. Called at every start.</summary>
    public static void Unpack(string directory) => Write(directory, overwrite: false);

    /// <summary>Put all four back as they shipped, over the top of whatever is there.</summary>
    public static void Restore(string directory) => Write(directory, overwrite: true);

    private static void Write(string directory, bool overwrite)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // No folder, no pictures — and WallpaperSet.Resolve answers null for every colour,
            // which the applier already treats as "leave the desktop alone".
            return;
        }

        foreach (var state in All)
        {
            var name = WallpaperSet.FileNameFor(state);
            var file = Path.Combine(directory, name);

            try
            {
                if (!overwrite && File.Exists(file)) continue;

                using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + name);

                // A picture that was renamed in the project and not here. Nothing to be done
                // about it at runtime — it is caught by a test instead, because the symptom is a
                // desktop that simply never changes and nobody reads a log about that.
                if (source is null) continue;

                // Through a temporary file and a move: a wallpaper half-written is a wallpaper
                // Windows will refuse, and it would be refused on every start afterwards
                // because the broken file does exist.
                var scratch = file + ".new";

                using (var target = File.Create(scratch)) source.CopyTo(target);

                File.Move(scratch, file, overwrite: true);
            }
            catch
            {
                // One picture that cannot be written loses one colour, not the other three.
            }
        }
    }
}
