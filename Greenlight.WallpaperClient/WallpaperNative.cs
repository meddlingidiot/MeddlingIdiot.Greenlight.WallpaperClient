using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Greenlight.WallpaperClient;

/// <summary>
/// What the desktop is wearing, and how it is hung. One <c>SystemParametersInfo</c> and two
/// registry values, which between them are the whole of the Windows wallpaper API that matters.
/// </summary>
/// <remarks>
/// <para>
/// <c>SPI_SETDESKWALLPAPER</c> hangs the picture but says nothing about how it is laid out;
/// the layout comes from <c>WallpaperStyle</c> and <c>TileWallpaper</c> under
/// <c>Control Panel\Desktop</c>, which Windows reads as it hangs it. So the registry is written
/// first and the picture second — the other way round gives you the right picture at the
/// previous fit, until something else happens to change it.
/// </para>
/// <para>
/// Not <c>IDesktopWallpaper</c>, which would allow a different picture per monitor. This is a
/// status light: the answer is the same on every screen, and one COM interface and a monitor
/// enumeration to say the same thing four times is a sample making a point nobody asked for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WallpaperNative
{
    private const uint SetDeskWallpaper = 0x0014;
    private const uint GetDeskWallpaper = 0x0073;

    /// <summary>Write it to the user's profile, and tell everybody else it changed.</summary>
    private const uint UpdateAndNotify = 0x01 | 0x02;

    private const string DesktopKey = @"Control Panel\Desktop";

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetParameter(uint action, uint param, string? value, uint winIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetParameter(uint action, uint param, [Out] char[] value, uint winIni);

    /// <summary>How Windows spells a fit, in the two registry values it spells it across.</summary>
    /// <remarks>
    /// Centre and Tile are the same <c>WallpaperStyle</c> and differ only in
    /// <c>TileWallpaper</c>: tiling is the older setting of the two and was never folded into
    /// the newer one.
    /// </remarks>
    public static (string Style, string Tile) Spell(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fill => ("10", "0"),
        WallpaperFit.Fit => ("6", "0"),
        WallpaperFit.Stretch => ("2", "0"),
        WallpaperFit.Centre => ("0", "0"),
        WallpaperFit.Tile => ("0", "1"),
        WallpaperFit.Span => ("22", "0"),
        _ => ("10", "0"),
    };

    /// <summary>The picture the desktop is wearing, or <c>null</c> when Windows would not say.</summary>
    public static string? Current()
    {
        try
        {
            // 260 is MAX_PATH, which is what this particular call still answers in: it predates
            // long paths and was never widened.
            var buffer = new char[260];

            if (!GetParameter(GetDeskWallpaper, (uint)buffer.Length, buffer, 0)) return null;

            var path = new string(buffer).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How the desktop is currently hung, as the two raw registry strings.</summary>
    /// <remarks>
    /// Kept as Windows wrote them rather than translated into a <see cref="WallpaperFit"/>.
    /// These are put back verbatim when the user's own wallpaper is restored, and a fit this
    /// client does not model — a value from a future Windows, or one some other tool wrote —
    /// should survive being borrowed for an afternoon.
    /// </remarks>
    public static (string? Style, string? Tile) CurrentFit()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
            return (key?.GetValue("WallpaperStyle") as string, key?.GetValue("TileWallpaper") as string);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Hang a picture, laid out the given way. Says whether Windows took it.</summary>
    public static bool Hang(string path, string style, string tile)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true))
            {
                // Best effort, and deliberately before the picture. A machine with a policy on
                // this key should still get the right picture at whatever fit it already had,
                // which is a much better failure than no picture at all.
                key?.SetValue("WallpaperStyle", style, RegistryValueKind.String);
                key?.SetValue("TileWallpaper", tile, RegistryValueKind.String);
            }
        }
        catch
        {
            // Carry on to the picture.
        }

        try
        {
            return SetParameter(SetDeskWallpaper, 0, path, UpdateAndNotify);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Hang a picture at one of the fits this client offers.</summary>
    public static bool Hang(string path, WallpaperFit fit)
    {
        var (style, tile) = Spell(fit);
        return Hang(path, style, tile);
    }
}
