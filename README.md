# MeddlingIdiot.Greenlight.WallpaperClient

Your desktop wallpaper, turned the colour of your pipeline.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK, and a demonstration of how little an app needs to do to use it. The wallpaper is **green**
while everything passes and **amber** while a pull request wants you. When a build breaks it
turns **red** and stays that way for as long as the pipeline is broken. With no Greenlight on the
machine at all it goes a monochrome **grey**: a green desktop on a four-minute-old snapshot would
be lying to you.

Four pictures, and no more. Greenlight also says whether a build is running, and every other
client does something with it — the halo breathes, the lava lamp bubbles — because they draw a
small thing in a corner and can afford to animate it. This one repaints the entire screen, so a
wallpaper that changed every time somebody pushed would not be a status light, it would be a
strobe. **Nothing here animates and nothing here reacts to a build starting.** Only the four
answers.

The point of it is what it does **not** have. No Azure DevOps client, no GitHub client, no
token, no polling loop — everything it knows arrives through the SDK, from the Greenlight
already running on the machine. Strip out the tray icon and the one Win32 call that hangs a
picture and the integration is about twenty lines, all of them in
[`App.cs`](Greenlight.WallpaperClient/App.cs).

## It gives your wallpaper back

This is the only Greenlight client that changes something you own. The others draw a window of
their own and take it away again; this one borrows a system setting, so it goes to some trouble
about giving it back:

- **Before it hangs anything**, it writes down what your desktop was wearing — and keeps its own
  copy of the picture. Windows usually answers "what is the wallpaper?" with its own re-encoded
  copy under `Themes\TranscodedWallpaper`, and then overwrites that copy with whatever is hung
  next. A note holding only that path would, about five seconds later, be a note pointing at our
  green one.
- **The note is on disk**, not in memory, so a crash or a hard reboot does not lose it. The next
  start finds the note, leaves it alone, and can still give the right wallpaper back.
- **If it cannot write that note, it does nothing at all.** The honest thing to do with a
  wallpaper you cannot give back is not to borrow it.
- It hands the wallpaper back when you quit, when you switch it off from the tray, and whenever
  you ask it to.

Turn off **Put my wallpaper back when this quits** if you would rather the Greenlight picture
simply became your wallpaper from now on.

## Running it

```bash
dotnet run --project Greenlight.WallpaperClient
```

Windows only: hanging a wallpaper is `SystemParametersInfo` and two registry values. The SDK
itself is not — it is plain .NET, and the same twenty lines work anywhere.

## The tray

There is no window in this app at all — the wallpaper is the window — so everything lives on the
mascot in the notification area:

- **Greenlight on the desktop** — take it away and bring it back. Taking it away hands your own
  wallpaper back. Clicking the icon does the same.
- **How it is laid out** — fill, fit, stretch, centre, tile, or span every monitor. Fill by
  default, which is what the Greenlight pictures want.
- **How long a colour has to hold** — how long a new colour has to last before the desktop
  changes. Two seconds by default: Greenlight can go green-amber-green inside a few seconds when
  a run finishes and the next one queues, and a desktop flicking through three pictures to end up
  where it started is worse than one that took two seconds to tell you the truth.
- **Grey wallpaper when Greenlight is away** — the grey picture rather than leaving the desktop
  alone. On by default, because with it off "Greenlight has stopped" and "this has stopped" look
  identical.
- **Put my wallpaper back when this quits** — on by default. See above.
- **Start with Windows** — read from the registry every time it is shown, so it agrees with Task
  Manager's Startup tab rather than with what we last wrote there.
- **Give me my wallpaper back** — now, without stopping. Greyed out when there is nothing to give
  back.
- **Open the pictures folder** / **Put the Greenlight pictures back** — where the four pictures
  live, and how to undo having replaced one.
- **Edit the file…** opens `wallpaper.json`. **Reload the file** picks up hand edits without a
  restart.

Every setting is written straight back to the file, so the menu and the JSON are never two
different sets of settings.

## Using your own pictures

The four Greenlight pictures are unpacked on first run into:

```
%AppData%\Greenlight.Wallpaper\Pictures\
```

The shortest way to use your own is to drop it in that folder and put its name in the file. A
bare name is taken as something in that folder; anything rooted is taken as it stands, and
environment variables are expanded.

`%AppData%\Greenlight.Wallpaper\wallpaper.json`, written with the defaults on first run. An empty
string means "the Greenlight picture for that colour":

```json
{
  "WhenOff":   "",
  "WhenGreen": "",
  "WhenAmber": "",
  "WhenRed":   "on-fire.jpg",
  "Fit": "Fill",
  "ChangeWhenOff": true,
  "SettleSeconds": 2,
  "RestoreOnExit": true
}
```

A picture that is not there falls back to the Greenlight one for that colour rather than being
skipped — one mistyped path out of four should cost you that colour, not the whole thing, and a
desktop that has quietly stopped changing is a much harder fault to notice than a desktop wearing
a picture you did not choose. A file that cannot be parsed falls back to the defaults rather than
refusing to start: it is a desk toy, and a stray comma should not cost you the whole thing.

Replacing a picture in the folder outright works too — nothing overwrites a file that is already
there. **Put the Greenlight pictures back** is how you undo that.

## How it is put together

| | |
|---|---|
| [`App.cs`](Greenlight.WallpaperClient/App.cs) | The whole Greenlight integration, and the settling |
| [`WallpaperSet.cs`](Greenlight.WallpaperClient/WallpaperSet.cs) | Which picture goes with which colour, and the falling back. No Win32, so it is testable |
| [`DesktopWallpaper.cs`](Greenlight.WallpaperClient/DesktopWallpaper.cs) | Borrowing the desktop, and giving it back |
| [`WallpaperNative.cs`](Greenlight.WallpaperClient/WallpaperNative.cs) | One `SystemParametersInfo` and two registry values |
| [`WallpaperPictures.cs`](Greenlight.WallpaperClient/WallpaperPictures.cs) | Getting the four pictures out of the executable and onto the disk |
| [`WallpaperTray.cs`](Greenlight.WallpaperClient/WallpaperTray.cs) | The tray icon and its menu |
| [`WallpaperConfig.cs`](Greenlight.WallpaperClient/WallpaperConfig.cs) | The file, and the clamping that keeps a hand edit from producing something you cannot find |

The registry is written before the picture, not after: `SPI_SETDESKWALLPAPER` hangs the picture
but says nothing about how it is laid out, and Windows reads `WallpaperStyle` as it hangs it. The
other way round gives you the right picture at the previous fit, until something else happens to
change it.

Not `IDesktopWallpaper`, which would allow a different picture per monitor. This is a status
light: the answer is the same on every screen, and a COM interface and a monitor enumeration to
say the same thing four times is a sample making a point nobody asked for.

Choosing a picture is free of Win32 and takes its "is that file there?" as a parameter, so the
whole of the falling-back can be tested without a desktop to hang anything on — and so the thing
that would make the toy look broken, a mistyped path that silently stops the desktop changing at
all, is a test rather than something you would have to catch by staring at your own wallpaper.

`DesktopWallpaper` takes the hanging itself as a parameter for the same reason, and for one more:
a test that called the real one would change the wallpaper of whoever ran the suite. Every test
in `DesktopWallpaperTests` hands it a fake that records what it was asked to do and tells Windows
nothing.

```bash
dotnet test
```

## Building on it

```bash
dotnet add package MeddlingIdiot.Greenlight.Sdk
```

That is exactly what this repository does — the SDK comes from nuget.org like any other
dependency. Greenlight itself is a separate product and is not open source — this sample is.
The two things it wants you to notice: the client sits in a disabled state and reconnects on
its own when Greenlight is not running, so there is nothing to guard; and the events arrive on
a background thread, so anything touching your UI has to get itself back onto the UI thread.
Both are worked through in `App.cs`.
