# AGENTS.md

GreenBoard / "Project InfiniteCanvas" — a native Windows desktop alternative to Microsoft Whiteboard.
WPF + .NET 9, offline-first, with a cloud sync backend planned.

## Layout

- `InfiniteCanvas.sln` — root solution; solution folders `src/` and `tests/`.
- `src/InfiniteCanvas.App` — WPF UI. `net9.0-windows`, `UseWPF=true`, `OutputType=WinExe`.
- `src/InfiniteCanvas.Core` — domain layer. `net9.0` (cross-platform).
- `src/InfiniteCanvas.Infrastructure` — persistence. `net9.0`, references Core.
- `tests/InfiniteCanvas.Core.Tests`, `tests/InfiniteCanvas.Infrastructure.Tests` — xUnit 2.9.2, `Microsoft.NET.Test.Sdk` 17.11.1, coverlet.collector 6.0.2.
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

Everything builds and the test suite is green (the only tests are the two template `UnitTest1` stubs).
There is no CI, no lint/format config, no `.editorconfig`, no `global.json`, no `Directory.Build.props` —
SDK version is whatever is installed (9.0.101 on this machine). WPF means the App project only builds on
Windows, and `dotnet run` opens a real window (do not launch it expecting a CLI to return).

Git: branch `master`, one baseline commit ("Baseline: working WPF infinite canvas prototype").
`bin/`/`obj/` are ignored via `.gitignore`, and `.gitattributes` normalises line endings (`* text=auto`,
binary assets such as `*.docx` marked `binary`) so the working tree stays clean across machines — do not
commit `.docx` as text.

## File ownership in the App project (do not re-shuffle these)

- `App.xaml` — **application-level resources only**: `<Application>` root, `StartupUri="MainWindow.xaml"`,
  and `<Application.Resources>` holding the shared `ToggleButtonStyle` (used by the pen/eraser radio
  buttons). No layout belongs here.
- `App.xaml.cs` — empty `App : Application` partial class. SDK-style WPF generates `App.g.cs` containing a
  `Main()` that calls `InitializeComponent()` itself, so **do not** add a manual `InitializeComponent()`
  call or an `OnStartup` override that calls it.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — the entire UI and all behaviour, namespace `InfiniteCanvas.App`.
  `x:Class` and the code-behind namespace must stay in sync.

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

- `CanvasContainer` (Grid) intercepts mouse wheel / drag for zoom+pan; `MainCanvas` is a fixed 5000×5000
  `Canvas` whose `RenderTransform` is a `TransformGroup` of `TranslateTransform ControlTranslate` +
  `ScaleTransform ControlScale`. Infinite canvas is faked by transform, not by real unbounded space.
- Zoom is clamped to scale 0.1–8.0 (10 %–800 %). The spec in the design doc says 10 %–1000 % — the
  prototype deviates; treat the spec as the target.
- Pan = right **or** middle mouse button drag, using `CaptureMouse`/`ReleaseMouseCapture`.
- Ink uses the native `System.Windows.Ink.InkCanvas` (`MyInkCanvas`), not SkiaSharp. `IgnorePressure=false`
  and `FitToCurve=true` are deliberate (XP-Pen stylus pressure + Bézier smoothing). Eraser is
  `InkCanvasEditingMode.EraseByStroke` (stroke eraser, not pixel eraser).
- Shapes are added as raw `Rectangle`/`Ellipse`/`Polygon` children of `MyInkCanvas` at hardcoded
  positions (250, 200) — no model, no selection tool, no undo yet.

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
- `Class1.cs` (Core, Infrastructure) and `UnitTest1.cs` (both test projects) are untouched template stubs.
  Replace them when adding real code; do not treat them as meaningful.
