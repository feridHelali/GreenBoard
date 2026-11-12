# Génère src\InfiniteCanvas.App\Assets\GreenBoard.ico (16 à 256 px, PNG embarqués) à partir du logo de la barre d'outils.
# À lancer avec : pwsh -STA -File installer\make-icon.ps1   (WPF exige un thread STA)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\InfiniteCanvas.App\Assets\GreenBoard.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

# Stylo : même tracé que l'icône IconPen (grille 24×24)
$penPath = 'M4,20 L5,15.5 L15.5,5 C16.5,4 18,4 19,5 C20,6 20,7.5 19,8.5 L8.5,19 Z M13.5,7 L17,10.5 M4,20 L8.5,19'

function Render([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()

    # Carré arrondi vert en dégradé
    $grad = [System.Windows.Media.LinearGradientBrush]::new(
        [System.Windows.Media.Color]::FromRgb(0x4C, 0xAF, 0x50),
        [System.Windows.Media.Color]::FromRgb(0x2E, 0x7D, 0x32), 90)
    $r = $size * 0.22
    $dc.DrawRoundedRectangle($grad, $null, [System.Windows.Rect]::new(0, 0, $size, $size), $r, $r)

    # Stylo blanc centré (≈ 62 % de l'icône), trait plus épais aux petites tailles pour rester lisible
    $geo = [System.Windows.Media.Geometry]::Parse($penPath)
    $scale = $size * 0.62 / 16.0          # le tracé occupe ~16 unités (4 → 20)
    $offset = ($size - 24 * $scale) / 2
    $tg = [System.Windows.Media.TransformGroup]::new()
    $tg.Children.Add([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $tg.Children.Add([System.Windows.Media.TranslateTransform]::new($offset, $offset))
    $dc.PushTransform($tg)
    $thickness = if ($size -le 24) { 2.8 } elseif ($size -le 48) { 2.4 } else { 2.0 }
    $pen = [System.Windows.Media.Pen]::new([System.Windows.Media.Brushes]::White, $thickness)
    $pen.StartLineCap = $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $dc.DrawGeometry($null, $pen, $geo)
    $dc.Pop()
    $dc.Close()

    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = [System.IO.MemoryStream]::new()
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render $s) }

# Format ICO : en-tête (6 octets) + 16 octets par image + données PNG
$ms = [System.IO.MemoryStream]::new()
$w = [System.IO.BinaryWriter]::new($ms)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
"Icône écrite : $out ($([Math]::Round($ms.Length / 1KB)) Ko)"
