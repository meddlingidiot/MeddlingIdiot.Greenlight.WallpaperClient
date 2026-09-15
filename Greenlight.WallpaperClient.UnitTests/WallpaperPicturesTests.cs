using Greenlight.WallpaperClient;

namespace Greenlight.WallpaperClient.UnitTests;

/// <summary>
/// Getting the four pictures out of the executable and onto the disk.
/// </summary>
/// <remarks>
/// The failure this is here for is a silent one. The <c>avares:</c> names are strings built from
/// the same <see cref="WallpaperSet.FileNameFor"/> the rest of the app uses, so a file renamed in
/// <c>Assets\Wallpapers</c> — or a csproj that stopped embedding them — compiles perfectly and
/// shows up only as a desktop that never changes, on somebody else's machine, after a release.
/// </remarks>
public class WallpaperPicturesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"wallpapers-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // A temp folder left behind is not a failed test.
        }
    }

    [Fact]
    public void AllFourPicturesAreReallyInThere()
    {
        WallpaperPictures.Unpack(_folder);

        foreach (var state in Enum.GetValues<WallpaperState>())
        {
            var file = Path.Combine(_folder, WallpaperSet.FileNameFor(state));

            Assert.True(File.Exists(file), $"{WallpaperSet.FileNameFor(state)} was not unpacked");

            // Not merely present: a zero-byte file is what a half-written copy leaves behind,
            // and Windows answers a wallpaper it cannot read with a black screen rather than
            // with an error.
            Assert.True(new FileInfo(file).Length > 0);
        }
    }

    [Fact]
    public void EveryPictureIsActuallyAPng()
    {
        WallpaperPictures.Unpack(_folder);

        byte[] signature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

        foreach (var state in Enum.GetValues<WallpaperState>())
        {
            using var file = File.OpenRead(Path.Combine(_folder, WallpaperSet.FileNameFor(state)));

            var head = new byte[4];
            Assert.Equal(4, file.ReadExactly2(head));
            Assert.Equal(signature, head);
        }
    }

    [Fact]
    public void APictureOfYourOwnIsNotOverwrittenOnTheNextStart()
    {
        Directory.CreateDirectory(_folder);

        var mine = Path.Combine(_folder, WallpaperSet.FileNameFor(WallpaperState.Red));
        File.WriteAllText(mine, "my own picture");

        WallpaperPictures.Unpack(_folder);

        // Unpack runs at every start. Somebody who painted their own greenlight-red.png should
        // not find it quietly replaced the next morning — that is what "Put the Greenlight
        // pictures back" is for, and it is a menu item they have to choose.
        Assert.Equal("my own picture", File.ReadAllText(mine));
    }

    [Fact]
    public void PuttingThemBackDoesOverwrite()
    {
        Directory.CreateDirectory(_folder);

        var mine = Path.Combine(_folder, WallpaperSet.FileNameFor(WallpaperState.Red));
        File.WriteAllText(mine, "my own picture");

        WallpaperPictures.Restore(_folder);

        Assert.NotEqual("my own picture", File.ReadAllText(mine));
        Assert.True(new FileInfo(mine).Length > 1000);
    }

    [Fact]
    public void ResolveFindsWhatUnpackWrote()
    {
        // The two halves meeting: the names Unpack writes and the names Resolve looks for are
        // the same names, which is only true for as long as both go through FileNameFor.
        WallpaperPictures.Unpack(_folder);

        foreach (var state in Enum.GetValues<WallpaperState>())
        {
            var resolved = WallpaperSet.Resolve(new WallpaperConfig(), state, _folder, File.Exists);

            Assert.NotNull(resolved);
            Assert.True(File.Exists(resolved));
        }
    }
}

internal static class StreamReadExtensions
{
    /// <summary>Read exactly this many bytes, or as many as there were.</summary>
    public static int ReadExactly2(this Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = stream.Read(buffer, read, buffer.Length - read);
            if (got == 0) break;
            read += got;
        }

        return read;
    }
}
