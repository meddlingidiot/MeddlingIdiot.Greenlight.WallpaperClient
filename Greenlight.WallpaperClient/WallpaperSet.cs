namespace Greenlight.WallpaperClient;

/// <summary>
/// Which picture goes with which colour — the configured one if there is one and it is really
/// there, and the Greenlight one otherwise.
/// </summary>
/// <remarks>
/// Deliberately free of Win32 and of Avalonia, and it takes its "is that file there?" as a
/// parameter, so the whole of the falling-back can be tested without a desktop to hang anything
/// on. It is the part most likely to be wrong in a way nobody notices: a typo in a path shows
/// up as a desktop that simply stops changing, which looks exactly like Greenlight having
/// nothing new to say.
/// </remarks>
public static class WallpaperSet
{
    /// <summary>The Greenlight picture for a colour, as it is named in the pictures folder.</summary>
    public static string FileNameFor(WallpaperState state) => state switch
    {
        WallpaperState.Green => "greenlight-green.png",
        WallpaperState.Amber => "greenlight-amber.png",
        WallpaperState.Red => "greenlight-red.png",
        _ => "greenlight-grey.png",
    };

    /// <summary>
    /// The file to hang for a colour, or <c>null</c> when there is nothing to hang — a
    /// configured picture that has been deleted and a pictures folder somebody has emptied.
    /// </summary>
    /// <param name="config">The four settings, each empty for "the Greenlight one".</param>
    /// <param name="state">What Greenlight is saying.</param>
    /// <param name="pictures">Where the Greenlight pictures were unpacked.</param>
    /// <param name="exists">How to check for a file. A parameter so this can be tested.</param>
    public static string? Resolve(
        WallpaperConfig config,
        WallpaperState state,
        string pictures,
        Func<string, bool> exists)
    {
        var chosen = config.PictureFor(state);

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var path = Expand(chosen, pictures);

            if (path is not null && exists(path)) return path;

            // Falls through to the Greenlight picture rather than giving up. Somebody who
            // mistyped one of four paths should lose that one colour, not the whole toy, and a
            // desktop that has quietly stopped changing is a much harder fault to see than a
            // desktop wearing a picture you did not pick.
        }

        var greenlight = Path.Combine(pictures, FileNameFor(state));
        return exists(greenlight) ? greenlight : null;
    }

    /// <summary>
    /// What a hand-written path in the file actually points at: environment variables expanded,
    /// and a bare name taken as something dropped in the pictures folder.
    /// </summary>
    /// <remarks>
    /// The bare name is the whole reason this exists. "Put your own picture in the folder the
    /// menu opens and write its name in the file" is an instruction somebody can follow;
    /// "write its full path, and mind the backslashes, which JSON wants doubled" is one they
    /// get wrong on the first go.
    /// </remarks>
    public static string? Expand(string path, string pictures)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (string.IsNullOrWhiteSpace(expanded)) return null;

            return Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(pictures, expanded));
        }
        catch
        {
            // Path.GetFullPath throws on the characters Windows will not have in a filename.
            // Somebody typing one of those into the file meant to type a path, and the honest
            // answer is "that is not one" rather than a crash on the next snapshot.
            return null;
        }
    }

    /// <summary>Whether a path is one of the pictures this client hangs, rather than a wallpaper of the user's own.</summary>
    /// <remarks>
    /// Asked before noting down what the desktop was wearing when this started. After a crash
    /// the desktop is still wearing one of ours, and recording that as "what the user had
    /// before" would be the toy eating somebody's wallpaper and then offering to give its own
    /// back.
    /// </remarks>
    public static bool IsOneOfOurs(string? path, string pictures)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetFullPath(pictures).TrimEnd(Path.DirectorySeparatorChar);

            return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a path is Windows' own working copy of the wallpaper rather than a file a person
    /// chose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows re-encodes whatever wallpaper is set into <c>Themes\TranscodedWallpaper</c> and,
    /// after a logon, answers "what is the wallpaper?" with that rather than with the file it
    /// came from. It is genuinely the user's wallpaper — but it is also the file Windows
    /// overwrites the moment anything else is hung.
    /// </para>
    /// <para>
    /// So this is not "not theirs". It is "copy it now, because the path will be a lie shortly":
    /// the picture is kept aside first, and only if that copy cannot be made does the answer
    /// here matter, and then it means refuse to borrow the desktop at all.
    /// </para>
    /// </remarks>
    public static bool IsWindowsOwnCopy(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            // Always this name, and never anything somebody picked out of a folder.
            return Path.GetFileNameWithoutExtension(path)
                .Equals("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
