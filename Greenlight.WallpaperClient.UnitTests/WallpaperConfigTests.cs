using Greenlight.WallpaperClient;

namespace Greenlight.WallpaperClient.UnitTests;

/// <summary>
/// The file. It is hand-editable and reloadable from the menu, which between them mean every
/// value in it can arrive wrong, missing, or out of range while the thing is running.
/// </summary>
public class WallpaperConfigTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"wallpaper-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try
        {
            File.Delete(_file);
        }
        catch
        {
            // A temp file left behind is not a failed test.
        }
    }

    [Fact]
    public void OutOfTheBoxEveryColourIsTheGreenlightOne()
    {
        var config = new WallpaperConfig();

        // Empty rather than a path. The pictures live in AppData under a folder this config has
        // no business knowing the layout of, and a default full of absolute paths would be four
        // things to keep in step instead of none.
        Assert.Equal(string.Empty, config.PictureFor(WallpaperState.Off));
        Assert.Equal(string.Empty, config.PictureFor(WallpaperState.Green));
        Assert.Equal(string.Empty, config.PictureFor(WallpaperState.Amber));
        Assert.Equal(string.Empty, config.PictureFor(WallpaperState.Red));
    }

    [Fact]
    public void TheDefaultsAreTheOnesTheReadmeDescribes()
    {
        var config = new WallpaperConfig();

        Assert.Equal(WallpaperFit.Fill, config.Fit);
        Assert.True(config.ChangeWhenOff);
        Assert.True(config.RestoreOnExit);
        Assert.Equal(2, config.SettleSeconds);
    }

    [Fact]
    public void AMissingFileIsWrittenOutWithTheDefaults()
    {
        var loaded = WallpaperConfig.Load(_file);

        Assert.True(File.Exists(_file));
        Assert.Equal(WallpaperFit.Fill, loaded.Fit);
    }

    [Fact]
    public void EverythingSurvivesTheRoundTrip()
    {
        new WallpaperConfig
        {
            WhenGreen = @"D:\mine\calm.png",
            WhenRed = "shouting.png",
            Fit = WallpaperFit.Span,
            ChangeWhenOff = false,
            RestoreOnExit = false,
            SettleSeconds = 10,
        }.Save(_file);

        var loaded = WallpaperConfig.Load(_file);

        Assert.Equal(@"D:\mine\calm.png", loaded.WhenGreen);
        Assert.Equal("shouting.png", loaded.WhenRed);
        Assert.Equal(WallpaperFit.Span, loaded.Fit);
        Assert.False(loaded.ChangeWhenOff);
        Assert.False(loaded.RestoreOnExit);
        Assert.Equal(10, loaded.SettleSeconds);
    }

    [Fact]
    public void ASettleTypedIntoTheFileIsBroughtBackIntoRange()
    {
        new WallpaperConfig { SettleSeconds = 90000 }.Save(_file);

        // Clamped on the way in, not only on the way out: a settle of a day is a desktop that
        // never changes again, and no way to tell that from a Greenlight with nothing to say.
        Assert.Equal(60, WallpaperConfig.Load(_file).SettleSeconds);
    }

    [Fact]
    public void ANegativeSettleIsSimplyAtOnce()
    {
        new WallpaperConfig { SettleSeconds = -5 }.Save(_file);

        Assert.Equal(0, WallpaperConfig.Load(_file).SettleSeconds);
    }

    [Fact]
    public void ADeletedLineIsTheGreenlightPictureRatherThanACrash()
    {
        // What a hand edit that removed a line deserializes as. Null here would be read further
        // down as "a picture at the path null" instead of as "the default".
        File.WriteAllText(_file, """{ "Fit": "Fit" }""");

        var loaded = WallpaperConfig.Load(_file);

        Assert.Equal(string.Empty, loaded.WhenGreen);
        Assert.Equal(string.Empty, loaded.WhenRed);
        Assert.Equal(WallpaperFit.Fit, loaded.Fit);
    }

    [Fact]
    public void AStrayCommaCostsTheSettingsAndNotTheWholeThing()
    {
        File.WriteAllText(_file, """{ "Fit": "Fill",, }""");

        var loaded = WallpaperConfig.Load(_file);

        Assert.Equal(WallpaperFit.Fill, loaded.Fit);
        Assert.True(loaded.RestoreOnExit);
    }

    [Fact]
    public void AReloadedFileArrivesInTheObjectTheTrayIsHolding()
    {
        // What "Reload the file" does. The menu ticks itself from this instance, so a new file
        // has to be copied in rather than swapped for.
        var live = new WallpaperConfig();

        live.CopyFrom(new WallpaperConfig
        {
            WhenAmber = "waiting.png",
            Fit = WallpaperFit.Centre,
            ChangeWhenOff = false,
            RestoreOnExit = false,
            SettleSeconds = 30,
        });

        Assert.Equal("waiting.png", live.WhenAmber);
        Assert.Equal(WallpaperFit.Centre, live.Fit);
        Assert.False(live.ChangeWhenOff);
        Assert.False(live.RestoreOnExit);
        Assert.Equal(30, live.SettleSeconds);
    }
}
