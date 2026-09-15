using Greenlight.WallpaperClient;

namespace Greenlight.WallpaperClient.UnitTests;

/// <summary>
/// Which picture goes with which colour, without a desktop. Everything here is a decision
/// somebody would otherwise have to check by looking at their own wallpaper — and a wallpaper
/// that has quietly stopped changing looks exactly like a build that has quietly stopped
/// breaking, which is the failure this whole file exists to catch.
/// </summary>
public class WallpaperSetTests
{
    private const string Pictures = @"C:\pictures";

    private static string Greenlight(WallpaperState state) =>
        Path.Combine(Pictures, WallpaperSet.FileNameFor(state));

    /// <summary>A disk where the four shipped pictures are there and nothing else is.</summary>
    private static Func<string, bool> OnlyTheGreenlightOnes => path =>
        path.StartsWith(Pictures + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path).StartsWith("greenlight-", StringComparison.OrdinalIgnoreCase);

    // ── the four colours ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(WallpaperState.Off, "greenlight-grey.png")]
    [InlineData(WallpaperState.Green, "greenlight-green.png")]
    [InlineData(WallpaperState.Amber, "greenlight-amber.png")]
    [InlineData(WallpaperState.Red, "greenlight-red.png")]
    public void OutOfTheBoxEachColourGetsItsGreenlightPicture(WallpaperState state, string expected)
    {
        var resolved = WallpaperSet.Resolve(new WallpaperConfig(), state, Pictures, OnlyTheGreenlightOnes);

        Assert.Equal(Path.Combine(Pictures, expected), resolved);
    }

    [Fact]
    public void EachColourGetsADifferentPicture()
    {
        // A copy-paste in FileNameFor would show up as a desktop that goes green and then never
        // changes again, which is the most reassuring possible way for this to be broken.
        var all = new[] { WallpaperState.Off, WallpaperState.Green, WallpaperState.Amber, WallpaperState.Red }
            .Select(WallpaperSet.FileNameFor)
            .ToArray();

        Assert.Equal(all.Length, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── somebody's own pictures ───────────────────────────────────────────────

    [Fact]
    public void APictureOfYourOwnIsUsedInsteadOfTheGreenlightOne()
    {
        var config = new WallpaperConfig { WhenRed = @"D:\mine\on-fire.jpg" };

        var resolved = WallpaperSet.Resolve(
            config, WallpaperState.Red, Pictures, path => path == @"D:\mine\on-fire.jpg");

        Assert.Equal(@"D:\mine\on-fire.jpg", resolved);
    }

    [Fact]
    public void ABareNameMeansAPictureDroppedInTheFolder()
    {
        // The whole reason Expand exists. "Put it in the folder the menu opens and write its
        // name in the file" is an instruction somebody can follow on the first go; "write the
        // full path, and mind that JSON wants the backslashes doubled" is not.
        var config = new WallpaperConfig { WhenGreen = "mine.png" };

        var resolved = WallpaperSet.Resolve(
            config, WallpaperState.Green, Pictures, path => path == Path.Combine(Pictures, "mine.png"));

        Assert.Equal(Path.Combine(Pictures, "mine.png"), resolved);
    }

    [Fact]
    public void AMistypedPathFallsBackToTheGreenlightPicture()
    {
        var config = new WallpaperConfig { WhenAmber = @"D:\mine\typo.jpg" };

        // One mistyped path out of four should cost that one colour, not the whole toy. A
        // desktop stuck on the last picture with no hint why is a much harder fault to see than
        // a desktop wearing a picture you did not choose.
        var resolved = WallpaperSet.Resolve(config, WallpaperState.Amber, Pictures, OnlyTheGreenlightOnes);

        Assert.Equal(Greenlight(WallpaperState.Amber), resolved);
    }

    [Fact]
    public void WhitespaceIsNotAPicture()
    {
        var config = new WallpaperConfig { WhenGreen = "   " };

        var resolved = WallpaperSet.Resolve(config, WallpaperState.Green, Pictures, OnlyTheGreenlightOnes);

        Assert.Equal(Greenlight(WallpaperState.Green), resolved);
    }

    [Fact]
    public void NothingToHangIsSaidWithNullRatherThanAPathThatIsNotThere()
    {
        // An emptied pictures folder and no picture of their own. The applier reads null as
        // "leave the desktop alone", which is the only decent answer — handing Windows a path
        // to nothing gets you a black screen, and a black screen means nothing at all.
        var resolved = WallpaperSet.Resolve(new WallpaperConfig(), WallpaperState.Red, Pictures, _ => false);

        Assert.Null(resolved);
    }

    [Fact]
    public void AnEnvironmentVariableInThePathIsExpanded()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new WallpaperConfig { WhenGreen = @"%USERPROFILE%\mine.png" };

        var resolved = WallpaperSet.Resolve(config, WallpaperState.Green, Pictures, _ => true);

        Assert.Equal(Path.Combine(home, "mine.png"), resolved);
    }

    [Fact]
    public void SomethingThatIsNotAPathAtAllIsNotACrash()
    {
        // The file is hand-edited. Somebody will eventually type a sentence into it, and the
        // answer to that is the Greenlight picture, not an unhandled exception on the next
        // snapshot.
        var config = new WallpaperConfig { WhenRed = "|<>?*" };

        var resolved = WallpaperSet.Resolve(config, WallpaperState.Red, Pictures, OnlyTheGreenlightOnes);

        Assert.Equal(Greenlight(WallpaperState.Red), resolved);
    }

    // ── telling our pictures from theirs ──────────────────────────────────────

    [Fact]
    public void OurOwnPicturesAreRecognised()
    {
        Assert.True(WallpaperSet.IsOneOfOurs(Greenlight(WallpaperState.Green), Pictures));
    }

    [Fact]
    public void SomebodyElsesWallpaperIsNotOneOfOurs()
    {
        Assert.False(WallpaperSet.IsOneOfOurs(@"C:\Users\someone\Pictures\lake.jpg", Pictures));
        Assert.False(WallpaperSet.IsOneOfOurs(null, Pictures));
    }

    [Fact]
    public void AFolderMerelyStartingWithTheSameLettersIsNotOurs()
    {
        // C:\pictures-old is not inside C:\pictures, and a prefix match without the separator
        // would say it was — which would end with somebody's real wallpaper written off as one
        // of ours and never given back.
        Assert.False(WallpaperSet.IsOneOfOurs(@"C:\pictures-old\lake.jpg", Pictures));
    }

    [Fact]
    public void WindowsOwnWorkingCopyIsRecognisedForWhatItIs()
    {
        // The file Windows overwrites with a copy of whatever is hung next. A note pointing at
        // it would be a note pointing at our green one five seconds later.
        Assert.True(WallpaperSet.IsWindowsOwnCopy(
            @"C:\Users\someone\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper"));

        Assert.False(WallpaperSet.IsWindowsOwnCopy(@"C:\Users\someone\Pictures\lake.jpg"));
        Assert.False(WallpaperSet.IsWindowsOwnCopy(null));
    }

    [Fact]
    public void WindowsOwnWorkingCopyIsNotMistakenForOneOfOurPictures()
    {
        // It is the user's wallpaper, re-encoded — so it must be remembered and copied aside,
        // not written off as ours and skipped. Getting this backwards eats the wallpaper of
        // everybody whose Windows reports it this way, which after a logon is most of them.
        Assert.False(WallpaperSet.IsOneOfOurs(
            @"C:\Users\someone\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper", Pictures));
    }
}
