# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added

- First cut: the desktop wallpaper, coloured from the Greenlight running on the machine. Green
  while everything passes, amber while a pull request wants you, red when a pipeline breaks, and
  a monochrome grey when there is no Greenlight to ask.
- The four Greenlight wallpapers, shipped in the executable and unpacked to
  `%AppData%\Greenlight.Wallpaper\Pictures` on first run — where Windows can see them, where an
  update cannot take them away, and where somebody can replace one by dropping a file on it.
- Any picture you like instead, one per colour, in `wallpaper.json`. A bare name means a file in
  the pictures folder, anything rooted is taken as it stands, and environment variables are
  expanded. A path that is not there falls back to the Greenlight picture for that colour rather
  than leaving the desktop stuck on the last one with no hint why.
- Giving the wallpaper back. What the desktop was wearing is written down — and the picture
  itself copied aside, because Windows overwrites its own `TranscodedWallpaper` with whatever is
  hung next — before anything is hung. The note is on disk, so a crash or a reboot does not lose
  it. If the note cannot be written, nothing is hung at all: the honest thing to do with a
  wallpaper you cannot give it back is not to borrow it.
- A settle. A new colour has to hold for a couple of seconds before the desktop changes, so a
  pipeline going green-amber-green while a run finishes and the next one queues does not flick
  the screen through three pictures to end up where it started.
- Six layouts — fill, fit, stretch, centre, tile, and span every monitor — written to
  `WallpaperStyle` and `TileWallpaper` before the picture rather than after, so the fit arrives
  with the picture instead of one change later.
- Deliberately nothing for a build being under way. Every other client animates something small
  in a corner; this one repaints the whole screen, and a wallpaper that changed every time
  somebody pushed would be a strobe rather than a status light.
- A tray menu for everything — the layout, the settle, whether Greenlight being away shows grey
  or leaves the desktop alone, whether the wallpaper comes back on exit, giving it back now,
  opening the pictures folder, and putting the shipped pictures back — each written straight back
  to `wallpaper.json`.
- "Start with Windows" in the tray menu, registering the stable shim beside the install rather
  than the versioned copy an update would move.
