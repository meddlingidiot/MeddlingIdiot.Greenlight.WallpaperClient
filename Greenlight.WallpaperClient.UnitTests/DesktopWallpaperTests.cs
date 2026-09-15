using Greenlight.WallpaperClient;

namespace Greenlight.WallpaperClient.UnitTests;

/// <summary>
/// Borrowing the desktop and giving it back — the bookkeeping, which is the half that decides
/// whether somebody gets their wallpaper back.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these hands <see cref="DesktopWallpaper"/> a fake hanger and records what it was
/// asked to do, rather than letting it reach Windows. That is not tidiness. A test that called
/// the real one would change the wallpaper of whoever ran the suite — including CI, and
/// including the author, who would find their desktop set to <c>D:\theirs\lake.jpg</c>, a path
/// that exists in this file and nowhere else on earth.
/// </para>
/// <para>
/// Which is exactly what happened the first time this file was written. Hence the parameter.
/// </para>
/// </remarks>
public class DesktopWallpaperTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"wallpaper-home-{Guid.NewGuid():N}");
    private readonly string _pictures;

    /// <summary>Everything the desktop was asked to wear, in order.</summary>
    private readonly List<(string Path, string Style, string Tile)> _hung = [];

    private bool _windowsAccepts = true;

    public DesktopWallpaperTests()
    {
        _pictures = Path.Combine(_home, "Pictures");
        Directory.CreateDirectory(_pictures);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        }
        catch
        {
            // A temp folder left behind is not a failed test.
        }
    }

    private DesktopWallpaper Fresh() => new(_home, _pictures, (path, style, tile) =>
    {
        if (!_windowsAccepts) return false;

        _hung.Add((path, style, tile));
        return true;
    });

    private string NotePath => Path.Combine(_home, "borrowed.json");

    private void NoteSaying(string path, string style = "10", string tile = "0") =>
        File.WriteAllText(NotePath, $$"""
            { "Path": {{System.Text.Json.JsonSerializer.Serialize(path)}}, "Style": "{{style}}", "Tile": "{{tile}}" }
            """);

    // ── hanging a picture ─────────────────────────────────────────────────────

    [Fact]
    public void TheSamePictureIsNotHungTwice()
    {
        var desktop = Fresh();

        Assert.True(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));
        Assert.False(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));

        // Greenlight re-sends its snapshot on every poll and the answer is usually the same one.
        // Repainting the whole screen each time would be a visible flash every few seconds.
        Assert.Single(_hung);
    }

    [Fact]
    public void ANewLayoutReHangsTheSamePicture()
    {
        // The bug this is here for: a check on the path alone means picking a new layout off the
        // menu quietly does nothing — in the one place the user is watching for an instant
        // result, because they have just clicked for it.
        var desktop = Fresh();

        Assert.True(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));
        Assert.True(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Span));

        Assert.Equal(2, _hung.Count);
        Assert.Equal("10", _hung[0].Style);
        Assert.Equal("22", _hung[1].Style);
    }

    [Fact]
    public void ForcingReHangsEvenWhenNothingHasChanged()
    {
        // For "Put the Greenlight pictures back", where the path is the same and only the bytes
        // at it have changed. Without it the desktop keeps showing the picture that was just
        // painted over, until the colour happens to change.
        var desktop = Fresh();

        Assert.True(desktop.Hang(@"C:\pictures\red.png", WallpaperFit.Fill));
        Assert.False(desktop.Hang(@"C:\pictures\red.png", WallpaperFit.Fill));
        Assert.True(desktop.Hang(@"C:\pictures\red.png", WallpaperFit.Fill, force: true));

        Assert.Equal(2, _hung.Count);
    }

    [Fact]
    public void APictureWindowsRefusedIsNotRememberedAsHanging()
    {
        var desktop = Fresh();
        _windowsAccepts = false;

        Assert.False(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));

        // Otherwise the next attempt at the same picture is skipped as "already up" and the
        // desktop stays wrong until the colour changes to something else and back.
        _windowsAccepts = true;
        Assert.True(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));
    }

    [Fact]
    public void TheLayoutGoesUpSpeltTheWayWindowsReadsIt()
    {
        Fresh().Hang(@"C:\pictures\green.png", WallpaperFit.Tile);

        Assert.Equal((@"C:\pictures\green.png", "0", "1"), _hung.Single());
    }

    // ── the note ──────────────────────────────────────────────────────────────

    [Fact]
    public void WithNoNoteThereIsNothingToGiveBack()
    {
        var desktop = Fresh();

        Assert.False(desktop.HasSomethingToGiveBack);
        Assert.False(desktop.GiveItBack());
        Assert.Empty(_hung);
    }

    [Fact]
    public void AnExistingNoteSurvivesAFreshStart()
    {
        // The case this whole mechanism is for: a crash, or a task manager. The old note is the
        // older and therefore the truer answer, and a second start must not overwrite it with
        // whatever is on the desktop now — which by then is one of ours.
        NoteSaying(@"D:\theirs\lake.jpg", "6");

        var desktop = Fresh();

        Assert.True(desktop.RememberTheirs());
        Assert.True(desktop.HasSomethingToGiveBack);
        Assert.Contains("lake.jpg", File.ReadAllText(NotePath));
    }

    [Fact]
    public void ANoteThatCannotBeReadIsNoNoteAtAll()
    {
        File.WriteAllText(NotePath, "this is not json");

        Assert.False(Fresh().HasSomethingToGiveBack);
    }

    [Fact]
    public void AnEmptyPathIsNoNoteAtAll()
    {
        // Deserializes cleanly and is still useless. Handing Windows an empty path gets a black
        // screen, which is a worse answer than admitting there is nothing to give back.
        NoteSaying(string.Empty);

        Assert.False(Fresh().HasSomethingToGiveBack);
    }

    [Fact]
    public void GivingItBackPutsUpExactlyWhatWasWrittenDown()
    {
        NoteSaying(@"D:\theirs\lake.jpg", "6", "0");

        Assert.True(Fresh().GiveItBack());

        // The fit is put back verbatim, not translated through WallpaperFit. A layout this
        // client does not model — a value from a future Windows, or one another tool wrote —
        // should survive being borrowed for an afternoon.
        Assert.Equal((@"D:\theirs\lake.jpg", "6", "0"), _hung.Single());
    }

    [Fact]
    public void GivingItBackTearsUpTheNote()
    {
        NoteSaying(@"D:\theirs\lake.jpg");

        var desktop = Fresh();

        Assert.True(desktop.GiveItBack());

        // Torn up whether or not Windows took the picture. A note that cannot be honoured — the
        // wallpaper was on a drive nobody has plugged in since — is not worth offering again at
        // every start for ever.
        Assert.False(File.Exists(NotePath));
        Assert.False(desktop.HasSomethingToGiveBack);
        Assert.False(desktop.GiveItBack());
    }

    [Fact]
    public void ANoteWithNoLayoutFallsBackToFilling()
    {
        // A note written on a fresh profile that has never had a wallpaper, so the two registry
        // values were not there to read.
        File.WriteAllText(NotePath, """{ "Path": "D:\\theirs\\lake.jpg" }""");

        Assert.True(Fresh().GiveItBack());

        Assert.Equal(("10", "0"), (_hung.Single().Style, _hung.Single().Tile));
    }

    [Fact]
    public void AfterGivingItBackTheNextColourIsHungAgain()
    {
        // Giving it back clears what this thinks is on the desktop. Without that, switching off
        // and straight back on would skip the first picture as "already up" — and it would not
        // be, because the user's own wallpaper is.
        NoteSaying(@"D:\theirs\lake.jpg");

        var desktop = Fresh();
        desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill);
        desktop.GiveItBack();

        Assert.True(desktop.Hang(@"C:\pictures\green.png", WallpaperFit.Fill));
    }

    [Fact]
    public void ADesktopWearingOneOfOursIsNotWrittenDownAsTheirs()
    {
        // A crash that also lost the note. There is nothing of theirs left on screen to record,
        // and recording ours would be worse than recording nothing: "give me my wallpaper back"
        // would then cheerfully hang the green one and call it done.
        var ours = Path.Combine(_pictures, WallpaperSet.FileNameFor(WallpaperState.Green));
        File.WriteAllText(ours, "a picture");

        Assert.True(WallpaperSet.IsOneOfOurs(ours, _pictures));
        Assert.False(Fresh().HasSomethingToGiveBack);
    }
}
