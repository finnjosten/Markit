# Markit

A minimal Win11-style "freeze and draw" screen annotation tool. Press a hotkey,
the monitor under your cursor freezes, and a small floating toolbar lets you
draw on top of it — pen, highlighter, eraser, undo, a quick radial tool menu,
and edits that persist per monitor so you can pick up where you left off.

## How it works

1. Press **Ctrl+Alt+D** by default to start a session (changeable from the
   tray icon's *Settings...* menu). **Ctrl+Alt+E** starts a session restoring
   whatever you last drew on that same monitor.
2. The monitor your cursor is currently on freezes (a screenshot of just that monitor).
3. A borderless fullscreen overlay shows the frozen screenshot with an amber border
   and a "DRAWING" watermark, so it's always obvious you're in drawing mode.
4. The floating toolbar (draggable) has three tools — **Pen**, **Highlighter**, and
   **Eraser** — each remembering its own color/thickness. Pick a color from the
   expandable palette, adjust thickness with the slider, undo the last stroke, or
   clear everything.
5. Tap **Space** anywhere in the overlay for a radial quick-tool menu at your
   cursor — click a tool to switch, click anywhere else to dismiss.
6. Press **Esc**, click the ✕ button, or press the hotkey again to exit — the
   overlay closes (saving your strokes for that monitor) and the toolbar hides.
7. Right-click the tray icon to toggle drawing mode manually, open Settings, or quit.

Keyboard shortcuts also work directly while the overlay is focused: `R G B O Y P`
for colors (`Shift+color` for highlighter), `Ctrl+Z` undo, `E` erase all, `X`
toggle eraser, `Space` radial menu, `Esc` exit.

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

Produces a single self-contained `Markit.exe` under
`bin\Release\net8.0-windows\win-x64\publish\` — no .NET runtime needs to be
installed on the target machine. This build output folder gets wiped on every
rebuild/republish, so copy the exe out to a stable location rather than
running it from there directly (or pointing shortcuts at it). A `Release`
directory junction at the project root points at this publish folder for
convenience — recreate it (`New-Item -ItemType Junction -Path Release -Target
bin\Release\net8.0-windows\win-x64\publish`) if a clean/rebuild ever removes it.

### Installed location + autostart

This machine's copy lives at `F:\Programs\Markit\Markit.exe`,
with a shortcut in the Windows Startup folder (`shell:startup` →
`Markit.lnk`) so it launches automatically on login.

To pick up a new build after making changes:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
copy "bin\Release\net8.0-windows\win-x64\publish\Markit.exe" "F:\Programs\Markit\Markit.exe"
```

Same filename, so the existing Startup shortcut keeps working — no need to
recreate it. To remove autostart, delete the `.lnk` file from `shell:startup`.

## Design notes / things worth knowing

- **Shapes (rectangle/ellipse/arrow/line) are intentionally out of scope for now.**
  Freehand pen/highlighter/eraser only, via WPF's `InkCanvas`. When shape tools
  are added, they'll be a settings-bindable toggle keybind (switch into shape
  mode, draw shapes, switch out) — not a hold-a-modifier gesture.
- **Pen/Highlighter/Eraser are three independent tools** (`MainWindow`'s
  `ToolMode`), each with its own remembered color/thickness, selected via
  mutually-exclusive `RadioButton`s (`GroupName="Tool"`) so there's no way to
  get stuck in eraser mode. Eraser size uses `InkCanvas.EraserShape`.
- **Radial menu**: a `Grid` layer already inside `DrawOverlayWindow` (not a
  separate popup window — avoids extra HWND/topmost/DPI complexity), positioned
  at the cursor via `Canvas.SetLeft/SetTop` computed from however many buttons
  are in the array, so adding shape tools later is just adding another button.
  Selecting a tool raises `ToolSelected`, which the toolbar handles by checking
  the matching `RadioButton` — same single code path as clicking it directly.
- **Multi-monitor + DPI**: only the monitor under the cursor is captured
  (`Services/MonitorCapture.cs`), using physical-pixel bounds so it's correct
  on mixed-DPI setups (`app.manifest` declares Per-Monitor-V2 DPI awareness).
- **Per-monitor ink persistence** (`Services/InkStore.cs`): strokes are
  serialized (WPF's native ISF format via `StrokeCollection.Save`/`ctor(Stream)`)
  to `%AppData%\Markit\ink\<display>.isf`, keyed by `Screen.DeviceName`, on
  every session exit. The resume hotkey loads them back in for a fresh capture
  of that same monitor.
- **Z-order**: the toolbar and the fullscreen overlay are separate topmost
  windows. Clicking into the overlay can otherwise bump it above the toolbar
  in the topmost z-order band — the overlay fires `UserInteracted` on every
  click, and the toolbar re-asserts its own topmost position in response
  (`WindowStyles.BringToTop`). Do **not** solve this via window ownership
  (`GWL_HWNDPARENT`) — destroying an owner window auto-destroys its owned
  windows, which took the toolbar down with it the first time this was tried.
  Relatedly: reclaiming keyboard focus for the overlay (needed so Esc/shortcuts
  work after using a toolbar control like the slider) can itself re-trigger
  that z-order bump, so `ReclaimOverlayFocus()` always re-asserts topmost right
  after focusing.
- **Settings** (`Services/AppSettings.cs`) persist to
  `%AppData%\Markit\settings.json`: both hotkeys and the last-used
  color/thickness per tool.
- **Deferred to a future v2**: pausing media on freeze (sending the
  `VK_MEDIA_PLAY_PAUSE` virtual key), and a "live edit" mode where the draw
  layer is click-through for scrolling except while actively drawing (doable
  later via `WS_EX_TRANSPARENT`/`WS_EX_LAYERED` toggling in `WindowStyles.cs`).
  A "switch to another display" toolbar option is also plausible cheaply, since
  it'd reuse the same per-monitor ink save/load path.

## Project layout

```text
Markit/
├── Markit.csproj
├── app.manifest              (Per-Monitor-V2 DPI awareness)
├── Assets/AppIcon.ico
├── App.xaml / App.xaml.cs    (tray icon, startup, crash logging)
├── MainWindow.xaml / .cs     (the floating toolbar; tool/color/width state)
├── DrawOverlayWindow.xaml / .cs   (fullscreen freeze + InkCanvas + radial menu)
├── SettingsWindow.xaml / .cs (hotkey capture UI, both hotkeys)
└── Services/
    ├── MonitorCapture.cs      (screenshot of the monitor under the cursor)
    ├── WindowStyles.cs        (topmost / tool-window / no-activate / exact positioning)
    ├── GlobalHotkeyService.cs (RegisterHotKey/WM_HOTKEY wrapper, supports multiple ids)
    ├── InkStore.cs            (per-monitor ISF stroke persistence)
    └── AppSettings.cs         (JSON-persisted hotkeys + last color/width per tool)
```
