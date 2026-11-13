# AGENTS.md

GreenBoard / "Project InfiniteCanvas" — a native Windows desktop alternative to Microsoft Whiteboard.
WPF + .NET 9, offline-first, with a cloud sync backend planned.

## Layout

- `InfiniteCanvas.sln` — root solution; solution folders `src/` and `tests/`.
- `src/InfiniteCanvas.App` — WPF UI. `net9.0-windows`, `UseWPF=true`, `OutputType=WinExe`.
- `src/InfiniteCanvas.Core` — domain layer. `net9.0` (cross-platform).
- `src/InfiniteCanvas.Infrastructure` — persistence. `net9.0`, references Core.
- `src/InfiniteCanvas.Mcp` — MCP server (stdio), `net9.0` console, references Core. Packages:
  `ModelContextProtocol` 2.3.1, `Microsoft.Extensions.Hosting` 10.0.15, `Svg.Skia` 5.2.2.
- `tests/InfiniteCanvas.Core.Tests`, `tests/InfiniteCanvas.Infrastructure.Tests` — xUnit 2.9.2, `Microsoft.NET.Test.Sdk` 17.11.1, coverlet.collector 6.0.2.
- `tests/InfiniteCanvas.App.Tests` — same xUnit stack, `net9.0-windows` + `UseWPF`; WPF work runs on an STA
  thread via the `Sta(...)` helper (ink and `RenderTargetBitmap` fail on xUnit's MTA threads).
- `designDocs/00_Inception/` — numbered phase folders; holds the authoritative spec + schema.

Dependency direction is `App → Infrastructure → Core` (App references both Core and Infrastructure directly).

Empty directories are the **intended** target layout, not leftovers from deleted files:
`Core/{Interfaces,Models,Services}`, `Infrastructure/{Database,Repositories}`, `App/{Views,ViewModels,Converters}`.

## Build & test

```powershell
dotnet build InfiniteCanvas.sln
dotnet test  InfiniteCanvas.sln
dotnet run   --project src\InfiniteCanvas.App\InfiniteCanvas.App.csproj
```

Everything builds and the test suite is green (real tests live in `InfiniteCanvas.App.Tests`; the Core and
Infrastructure test projects still only hold template `UnitTest1` stubs).
The App project's `AssemblyName` is `GreenBoard` (exe `GreenBoard.exe`, version/icon in its csproj); the
namespace stays `InfiniteCanvas.App`. A running `GreenBoard.exe` locks its output — close it before rebuilding
the App or its tests. The same applies to `InfiniteCanvas.Mcp.exe` while an MCP client has it running.

## Installer / release

```powershell
pwsh -File installer\build-installer.ps1   # tests → self-contained win-x64 publish → Inno Setup
```

- Output: `dist\GreenBoard-Setup-<Version>.exe` (version read from `InfiniteCanvas.App.csproj`). `dist/` and
  `installer/publish/` are git-ignored. Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`).
- `installer\GreenBoard.iss`: per-user install without admin (`PrivilegesRequired=lowest`, `{autopf}`), FR/EN,
  optional `mcp` component (installed to `{app}\mcp`, writes `LISEZ-MOI-MCP.txt` with the real path),
  `.gboard` association (task `assoc`, `HKA\Software\Classes`), closes/restarts a running GreenBoard on upgrade.
  **Never change `AppId`** — upgrades and uninstall depend on it. Bump `<Version>` for each release.
- `GreenBoard.exe "<file>.gboard"` opens the file (`OpenFromCommandLine`, called on `Loaded`).
- Icon (green board, chalk ∞, amber stylus; ≤ 40 px drops grid and stylus): `src\InfiniteCanvas.App\Assets\GreenBoard.ico`
  + `GreenBoard.png` (512 px, toolbar logo) + `Splash.png` (startup screen, shows the csproj `<Version>`), all
  regenerated with `pwsh -STA -File installer\make-icon.ps1` (`-Preview <dir>` writes contact sheets);
  `build-installer.ps1` runs it first so the splash version is never stale. `AlfaComputers.png` (publisher logo)
  and `Auteur.png` (author photo, 360 px) are hand-supplied assets used by the About tab and the splash.
- Identity: publisher **Alfa Computers Apps**, author **Ferid HELALI + Claude Code** (csproj `Company`/`Authors`,
  `AppInfo.cs`, installer `AppPublisher`). Public repo / releases: https://github.com/feridHelali/GreenBoard
- Self-contained check: `GreenBoard.runtimeconfig.json` must list `includedFrameworks` (not `frameworks`), and
  native DLLs import only Windows system DLLs or files shipped alongside (WPF brings `vcruntime140_cor3.dll`).
- The setup exe is not code-signed: Windows SmartScreen warns on first run.
There is no CI, no lint/format config, no `.editorconfig`, no `global.json`, no `Directory.Build.props` —
SDK version is whatever is installed (9.0.101 on this machine). WPF means the App project only builds on
Windows, and `dotnet run` opens a real window (do not launch it expecting a CLI to return).

Git: branch `master`, one baseline commit ("Baseline: working WPF infinite canvas prototype").
`bin/`/`obj/` are ignored via `.gitignore`, and `.gitattributes` normalises line endings (`* text=auto`,
binary assets such as `*.docx` marked `binary`) so the working tree stays clean across machines — do not
commit `.docx` as text.

## File ownership in the App project (do not re-shuffle these)

- `App.xaml` — **application-level resources only**: `<Application>` root, `StartupUri="MainWindow.xaml"`,
  and `<Application.Resources>` holding the toolbar look: icon geometries (`Icon*` `PathGeometry`, 24×24
  stroke-only grid), brushes, `ToggleButtonStyle` (tool radio buttons — `Content` is an icon geometry),
  `IconButtonStyle` / `IconToggleStyle` (action buttons), `ColorSwatchStyle`, `ToolbarDivider`,
  `FloatingButtonStyle` (round gear/help buttons), and the shared dialog look (`DialogTitle`, `DialogSection`,
  `DialogHint`, `DialogButtonStyle`, `PrimaryButtonStyle`, `KeyCap`). No layout belongs here.
- `App.xaml.cs` — `App : Application`; only the splash screen (`OnStartup` shows `Assets/Splash.png`,
  `HideSplash()` fades it ≥ 1.5 s after start, called from `MainWindow.ContentRendered`). SDK-style WPF generates
  `App.g.cs` containing a `Main()` that calls `InitializeComponent()` itself, so **do not** add a manual
  `InitializeComponent()` call anywhere in `App`.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — the entire UI and its event handling, namespace
  `InfiniteCanvas.App`. `x:Class` and the code-behind namespace must stay in sync.
- Non-UI helpers next to it (all `public` so `InfiniteCanvas.App.Tests` can reach them):
  `BoardBackgrounds.cs` (`BoardBackground` enum + brush factory shared by screen and export),
  `BoardHistory.cs` (undo/redo), `BoardFile.cs` (`.gboard` save/load), `BoardExporter.cs` (PNG/JPEG/SVG/PDF),
  `BoardPipeServer.cs` (named-pipe endpoint for the MCP server), `AppSettings.cs` (user settings record,
  `%APPDATA%\GreenBoard\reglages.json`, `Normalized()` clamps every value, corrupt file → defaults),
  `AppInfo.cs` (version from `AssemblyInformationalVersion`, publisher, author, repo URL).
- `Views/` dialogs: `ShareWindow` (e-mail), `SettingsWindow` (opened by the bottom-left gear or Ctrl+, ; applies
  live through a callback and reverts on Cancel/close), `HelpWindow` (F1 / « ? » button; tabs `HelpTab`
  Features, Assistant (MCP setup for Claude Desktop, Claude Code, OpenCode, generic stdio + 10 teaching
  prompts), Shortcuts, About). Keep the Shortcuts tab in sync with `Window_PreviewKeyDown`.
- The main window opens maximized. The floating `CornerButtons` sit above the sheet; `CanvasContainer_PreviewMouseDown`
  ignores clicks on them so the Pan tool does not swallow them.

## WPF/XAML gotchas that will cost you build cycles

- `Application.Resources` must have a `<ResourceDictionary>` as its direct root child — a `<Grid>` or any
  other element there is a markup-compile error.
- `StackPanel` has **no** `Padding` property (use a wrapping `<Border>`); `Border` has **no**
  `Foreground` property (use `TextElement.Foreground`, and note `ContentPresenter` exposes only the
  attached `TextElement.Foreground`, not `Foreground`).
- Markup-compile failures are reported as `MC1000`/`MC2000`/`MC3072` wrappers — read the **inner** message
  for the real cause and line.
- A `StaticResource` that is not defined in any merged resource dictionary fails the markup compile, not
  just at runtime.

## Prototype rendering model (the code that does exist, in `MainWindow.xaml.cs`)

- `CanvasContainer` (Grid) intercepts wheel / drag via **Preview** events for zoom+pan; `MainCanvas` is a
  fixed 20000×20000 `Canvas` (top-left aligned) whose `RenderTransform` is `ScaleTransform ControlScale`
  **then** `TranslateTransform ControlTranslate`, i.e. `screen = world × scale + translate` (translate is in
  screen pixels). `ClampView()` keeps the sheet on screen; `ResetView()` centres it at 100 %.
- Zoom is clamped to 0.1–10.0 (10 %–1000 %, per spec) and anchored on the cursor (`ZoomAt`).
- Pan = hold **Space + left drag**, the Pan tool (H), or right/middle drag. While Space is held the
  InkCanvas `EditingMode` is `None` so no stray ink is drawn.
- Ink uses the native `System.Windows.Ink.InkCanvas` (`MyInkCanvas`), not SkiaSharp. `IgnorePressure=false`
  and `FitToCurve=true` are deliberate (XP-Pen stylus pressure + Bézier smoothing). Eraser is
  `InkCanvasEditingMode.EraseByStroke` (stroke eraser, not pixel eraser).
- Shapes (rectangle, ellipse, triangle, line, arrow) are drawn by dragging (Shift constrains) and are
  **ordinary `Stroke`s** (polyline points, `FitToCurve=false`, `IgnorePressure=true`, tagged with the
  `ShapeKindProperty` ISF property). That is deliberate: as WPF `Shape` children they could not be erased,
  tap-selected or saved with the ink. Do not go back to `Shape` elements.
- Images (imported PNG/JPEG, MCP illustrations) are `Image` children of `MyInkCanvas` whose `Tag` is a
  `BoardItemTag(Id, Data, Label)`; position them with **`InkCanvas.SetLeft/SetTop`** (`Canvas.SetLeft` is
  ignored by InkCanvas). Ink renders above them, so they can be annotated. The eraser does not touch them —
  select + Delete instead.
- Undo/redo (`BoardHistory`): stroke adds/removes are captured from `Strokes.StrokesChanged`; image
  add/remove, selection move/resize (`SelectionMoving`→`SelectionMoved` snapshots) and recolouring are
  recorded explicitly. Window-level left mouse down/up opens/closes a group so one gesture = one undo step.
  After replacing `MyInkCanvas.Strokes` call `_history.Reset()`.
- Pages (tabs): one `InkCanvas` serves every page. `ActivatePage` stores the outgoing page
  (`StoreActivePage`: children → `Elements`, background, view), then swaps in the target's `Strokes`,
  children, background, view and **its own `BoardHistory`** (`Detach`/`Attach`, not `Reset`). Anything that
  reads all pages (save, export, MCP `get_board`) must call `StoreActivePage()` first or read the live canvas
  for the active page. Tabs are rebuilt in code (`RebuildTabs`); a focused `TextBox` (tab rename) disables
  all keyboard shortcuts in `Window_PreviewKeyDown`.
- Rotation: InkCanvas Select mode only moves/resizes, so `OverlayCanvas` (screen space, null background,
  above `MainCanvas`) hosts a rotation handle placed via `TranslatePoint` from the selection bounds. Dragging
  re-applies `RotateAt` to a snapshot of the selected strokes (no drift); one undo step on release. Images
  are not rotated. Call `UpdateRotateHandle()` after anything that moves the view or the selection.
- Auto-placement: `AddImageItem` without x/y uses `FindFreeSpot` (no ink/image overlap, nearest the view
  centre, else right of all content) and recentres the view if the item lands off-screen.
- `.gboard` file = ZIP, version 3: `pages/<n>/ink.isf` (native Ink Serialized Format),
  `pages/<n>/items/<i>.png|.jpg`, `board.json` (active page + per page: id, title, background, view, item
  positions). Versions 1–2 (single page at the archive root) still load. Written to `<path>.tmp` then moved.
- Export: PDF = every page (one PDF page each, empty page → A4 landscape); PNG/JPEG/SVG = active page only.
- Share by e-mail (`MailShare.cs` + `Views/ShareWindow.xaml`): exports the PDF to
  `%TEMP%\GreenBoard\Partage\`, then opens the user's mail client compose window via Simple MAPI
  (`MAPISendMailW`, `MAPI_DIALOG` — the user reviews and sends; GreenBoard never sends mail or stores
  credentials). MAPI blocks while the compose window is open on some clients, so it runs on a dedicated STA
  thread. If MAPI is unavailable (new Outlook, webmail, no client): `mailto:` + Explorer with the PDF selected.
  Last recipients are remembered in `%APPDATA%\GreenBoard\partage.json`. Do not add an SMTP sender without
  a proper credential store.
- Export (`BoardExporter`): content bounds + 40 margin. PNG/JPEG via `RenderTargetBitmap` at 2× (≤ 40 MP);
  SVG and PDF are vector — stroke outlines come from `Stroke.GetGeometry()` flattened to polygons; the
  background tile is re-emitted as an SVG `<pattern>` / PDF tiling pattern; PDF is hand-written (no library),
  images as FlateDecode + SMask. Numbers are always formatted with `InvariantCulture` (French locale!).
- Backgrounds (`BoardBackground` enum, 6 kinds) are a tiled `DrawingBrush` on `CanvasContainer`, rebuilt on
  scale change (constant on-screen line width) and given the view matrix as its `Transform`, so the
  pattern is infinite and follows pan/zoom.

## MCP server (AI illustrations for teachers)

- Flow: MCP client (Claude) → `InfiniteCanvas.Mcp.exe` over stdio → the model writes an SVG → `SvgRenderer`
  (Svg.Skia) rasterises it at 3× → PNG sent to the running app over the named pipe
  `GreenBoard.Board.v1` (`PipeOptions.CurrentUserOnly`) → `AddImageItem` places it.
- Pipe protocol lives in Core (`Core/Models/BoardProtocol.cs`): one JSON line request, one JSON line
  response; commands `get_board`, `add_image`, `remove_item`, `set_background`, `add_page`, `select_page`.
  Everything acts on the active page.
- Tools: `get_board`, `preview_svg` (render only, returns the image so the model can self-check),
  `draw_svg`, `insert_image`, `remove_item`, `set_background`, `new_page`, `select_page`;
  prompt `illustration_cours`.
  Design rules for the model are in `DesignGuide.cs` (sent as server instructions).
- `SvgRenderer` rejects external `href`/`url()`, `<script>`, DOCTYPE/ENTITY and `@import` — keep it that way.
- stdout is the MCP channel: never `Console.WriteLine` in the Mcp project; logging goes to stderr.
- Registered in Claude Code (local scope) as `greenboard` pointing at the Debug exe.

## Design docs

- `designDocs/00_Inception/GreenboardInfinitCanvas.docx` — the authoritative French spec (WPF + InkCanvas
  or SkiaSharp, local SQLite/LiteDB, ASP.NET Core + PostgreSQL + S3/MinIO cloud tier, event-sourced
  outbox sync, QuadTree viewport culling, PNG/JPEG/SVG/PDF export, sticky notes, image/PDF import,
  open-core business model). **It is a binary .docx — you cannot grep it.** Read it by unzipping and
  pulling `word/document.xml`:
  `Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead(<path>)`
- `designDocs/00_Inception/database_schema.sql` — SQLite-flavoured schema: `CanvasObjects`
  (Id TEXT PK, Type, X, Y, Width, Height, ColorHex, StrokeThickness) and `HandDrawnStrokes`
  (Id, ColorHex, Thickness, GeometryData as SVG path data). `Type` values in the comment:
  `Rectangle`, `Ellipse`, `Triangle`, `StickyNote`.

## Conventions

- **French** is the language of the project: code comments, UI strings (`"Main Libre"`, `"Gomme"`), and
  design docs. Keep new comments and UI text in French to match.
- All projects: `Nullable=enable`, `ImplicitUsings=enable`. Do not add redundant `using` directives for
  `System`, `System.Linq`, `System.Collections.Generic`.
- Test projects declare `<Using Include="Xunit" />` — write `[Fact]` tests without `using Xunit;`.
- `Class1.cs` (Infrastructure) and `UnitTest1.cs` (Core/Infrastructure test projects) are untouched template stubs.
  Replace them when adding real code; do not treat them as meaningful.
