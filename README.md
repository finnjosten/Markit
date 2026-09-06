# ZoomIt Toolbar

A minimal Win11-style "freeze and draw" screen annotation tool. Press a hotkey,
the monitor under your cursor freezes, and a small floating toolbar lets you
draw on top of it with the mouse — colors, highlighter, thickness, undo, erase.

## How it works

1. Press **Ctrl+Alt+D** by default (changeable from the tray icon's *Settings...* menu).
2. The monitor your cursor is currently on freezes (a screenshot of just that monitor).
3. A borderless fullscreen overlay shows the frozen screenshot with an amber border
   and a "DRAWING" watermark, so it's always obvious you're in drawing mode.
4. Draw freehand with the mouse. The floating toolbar (draggable) lets you pick a
   pen color from an expandable palette, toggle the highlighter, adjust thickness,
   undo the last stroke, or clear everything.
5. Press **Esc**, click the ✕ button, or press the hotkey again to exit — the
   overlay closes and the toolbar hides.
6. Right-click the tray icon to toggle drawing mode manually, open Settings, or quit.

Keyboard shortcuts also work directly while the overlay is focused: `R G B O Y P`
for colors (`Shift+color` for highlighter), `Ctrl+Z` undo, `E` erase all, `Esc` exit.

## Build & run

Requires the .NET 8 SDK with the Windows desktop workload.

```powershell
dotnet build
dotnet run
```

## Publish a standalone .exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Produces a single self-contained `ZoomItToolbar.exe` under
`bin\Release\net8.0-windows\win-x64\publish\` — no .NET runtime needs to be
installed on the target machine. This build output folder gets wiped on every
rebuild/republish, so copy the exe out to a stable location rather than
running it from there directly (or pointing shortcuts at it).

### Installed location + autostart

This machine's copy lives at `F:\Programs\ZoomItToolbar\ZoomItToolbar.exe`,
with a shortcut in the Windows Startup folder (`shell:startup` →
`ZoomIt Toolbar.lnk`) so it launches automatically on login.

To pick up a new build after making changes:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
copy "bin\Release\net8.0-windows\win-x64\publish\ZoomItToolbar.exe" "F:\Programs\ZoomItToolbar\ZoomItToolbar.exe"
```

Same filename, so the existing Startup shortcut keeps working — no need to
recreate it. To remove autostart, delete the `.lnk` file from `shell:startup`.

## Design notes / things worth knowing

- **Shapes (rectangle/ellipse/arrow/line) are intentionally out of scope.**
  Freehand pen only, via WPF's `InkCanvas`.
- **Highlighter** is a toggle (`H`) next to the pen swatch — applies to all
  colors, rendered via `InkCanvas`'s built-in `IsHighlighter` blend so
  overlapping strokes don't over-darken.
- **Multi-monitor + DPI**: only the monitor under the cursor is captured
  (`Services/MonitorCapture.cs`), using physical-pixel bounds so it's correct
  on mixed-DPI setups (`app.manifest` declares Per-Monitor-V2 DPI awareness).
- **Z-order**: the toolbar and the fullscreen overlay are separate topmost
  windows. Clicking into the overlay can otherwise bump it above the toolbar
  in the topmost z-order band — the overlay fires `UserInteracted` on every
  click, and the toolbar re-asserts its own topmost position in response
  (`WindowStyles.BringToTop`). Do **not** solve this via window ownership
  (`GWL_HWNDPARENT`) — destroying an owner window auto-destroys its owned
  windows, which took the toolbar down with it the first time this was tried.
- **Settings** (`Services/AppSettings.cs`) persist to
  `%AppData%\ZoomItToolbar\settings.json`: the hotkey and the last-used pen
  color/thickness.
- **Deferred to a future v2**: pausing media on freeze (sending the
  `VK_MEDIA_PLAY_PAUSE` virtual key), and a "live edit" mode where the draw
  layer is click-through for scrolling except while actively drawing (doable
  later via `WS_EX_TRANSPARENT`/`WS_EX_LAYERED` toggling in `WindowStyles.cs`).

## Project layout

```
ZoomItToolbar/
├── ZoomItToolbar.csproj
├── app.manifest              (Per-Monitor-V2 DPI awareness)
├── Assets/AppIcon.ico
├── App.xaml / App.xaml.cs    (tray icon, startup)
├── MainWindow.xaml / .cs     (the floating toolbar)
├── DrawOverlayWindow.xaml / .cs   (fullscreen freeze + InkCanvas draw surface)
├── SettingsWindow.xaml / .cs (hotkey capture UI)
└── Services/
    ├── MonitorCapture.cs      (screenshot of the monitor under the cursor)
    ├── WindowStyles.cs        (topmost / tool-window / no-activate / exact positioning)
    ├── GlobalHotkeyService.cs (RegisterHotKey/WM_HOTKEY wrapper)
    └── AppSettings.cs         (JSON-persisted hotkey + last pen color/width)
```
