using Greenlight.WallpaperClient;

namespace Greenlight.WallpaperClient.UnitTests;

/// <summary>
/// How Windows spells a layout. Two registry strings, no API constants to lean on, and a wrong
/// answer that is completely silent — a picture letterboxed instead of filled looks fine on any
/// screen the same shape as the picture, which is the one the author is looking at.
/// </summary>
public class WallpaperFitTests
{
    [Theory]
    [InlineData(WallpaperFit.Fill, "10", "0")]
    [InlineData(WallpaperFit.Fit, "6", "0")]
    [InlineData(WallpaperFit.Stretch, "2", "0")]
    [InlineData(WallpaperFit.Centre, "0", "0")]
    [InlineData(WallpaperFit.Tile, "0", "1")]
    [InlineData(WallpaperFit.Span, "22", "0")]
    public void EachFitIsSpeltTheWayWindowsReadsIt(WallpaperFit fit, string style, string tile)
    {
        Assert.Equal((style, tile), WallpaperNative.Spell(fit));
    }

    [Fact]
    public void CentreAndTileDifferOnlyInTheOlderOfTheTwoValues()
    {
        // Worth pinning down, because it looks like a bug on the way past. Tiling predates
        // WallpaperStyle and was never folded into it, so the two share a style and are told
        // apart by TileWallpaper alone.
        var centre = WallpaperNative.Spell(WallpaperFit.Centre);
        var tile = WallpaperNative.Spell(WallpaperFit.Tile);

        Assert.Equal(centre.Style, tile.Style);
        Assert.NotEqual(centre.Tile, tile.Tile);
    }

    [Fact]
    public void OnlyTilingEverSetsTheTileValue()
    {
        foreach (var fit in Enum.GetValues<WallpaperFit>())
        {
            var (_, tile) = WallpaperNative.Spell(fit);

            Assert.Equal(fit == WallpaperFit.Tile ? "1" : "0", tile);
        }
    }
}
