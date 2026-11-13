# Génère l'icône de GreenBoard : tableau vert, symbole ∞ tracé à la craie, stylet ambre.
#   src\InfiniteCanvas.App\Assets\GreenBoard.ico  (16 à 256 px, PNG embarqués)
#   src\InfiniteCanvas.App\Assets\GreenBoard.png  (512 px, pour la documentation et les publications)
# À lancer avec : pwsh -STA -File installer\make-icon.ps1   (WPF exige un thread STA)
# Option : -Preview <dossier> écrit aussi un aperçu de toutes les tailles sur fond clair et sombre.
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\InfiniteCanvas.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function C([string]$hex, [byte]$a = 255) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex); $c.A = $a; return $c
}
function B([string]$hex, [byte]$a = 255) { [System.Windows.Media.SolidColorBrush]::new((C $hex $a)) }
function P($brush, [double]$w) {
    $p = [System.Windows.Media.Pen]::new($brush, $w)
    $p.StartLineCap = $p.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $p.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    return $p
}

# Symbole ∞ (lemniscate) dans une grille 100×100, centré en (50,46)
$infinity = 'M50,46 C61,30 84,30 84,46 C84,62 61,62 50,46 C39,30 16,30 16,46 C16,62 39,62 50,46 Z'

# Dessine l'icône dans un repère 100×100 ; $detail = grille + stylet (masqués aux petites tailles)
function Draw($dc, [bool]$detail, [double]$chalk) {
    $rect = [System.Windows.Rect]::new(0, 0, 100, 100)

    # Tableau : dégradé vert profond en diagonale
    $bg = [System.Windows.Media.LinearGradientBrush]::new((C '#43A047'), (C '#1B5E20'), [System.Windows.Point]::new(0, 0), [System.Windows.Point]::new(1, 1))
    $dc.DrawRoundedRectangle($bg, $null, $rect, 22, 22)

    if ($detail) {
        # Quadrillage discret (le canevas infini), découpé au carré arrondi
        $dc.PushClip([System.Windows.Media.RectangleGeometry]::new($rect, 22, 22))
        $grid = P (B '#FFFFFF' 26) 0.8
        for ($v = 12.5; $v -lt 100; $v += 12.5) {
            $dc.DrawLine($grid, [System.Windows.Point]::new($v, 0), [System.Windows.Point]::new($v, 100))
            $dc.DrawLine($grid, [System.Windows.Point]::new(0, $v), [System.Windows.Point]::new(100, $v))
        }
        # Reflet en haut
        $shine = [System.Windows.Media.LinearGradientBrush]::new((C '#FFFFFF' 46), (C '#FFFFFF' 0), 90)
        $dc.DrawRectangle($shine, $null, [System.Windows.Rect]::new(0, 0, 100, 40))
        $dc.Pop()
        # Liseré intérieur
        $dc.DrawRoundedRectangle($null, (P (B '#FFFFFF' 38) 1.2), [System.Windows.Rect]::new(1.5, 1.5, 97, 97), 20.5, 20.5)
    }

    # ∞ à la craie : ombre légère puis trait blanc
    $geo = [System.Windows.Media.Geometry]::Parse($infinity)
    if ($detail) {
        $dc.PushTransform([System.Windows.Media.TranslateTransform]::new(0, 1.8))
        $dc.DrawGeometry($null, (P (B '#000000' 60) $chalk), $geo)
        $dc.Pop()
    }
    $dc.DrawGeometry($null, (P ([System.Windows.Media.Brushes]::White) $chalk), $geo)

    if ($detail) {
        # Stylet ambre dont la pointe termine le trait (bas du lobe droit), corps vers le coin bas-droit
        $tip = [System.Windows.Point]::new(70.5, 57.5)
        $tg = [System.Windows.Media.TransformGroup]::new()
        $tg.Children.Add([System.Windows.Media.RotateTransform]::new(50))
        $tg.Children.Add([System.Windows.Media.TranslateTransform]::new($tip.X, $tip.Y))

        # Repère local : pointe en (0,0), le stylet s'étend vers +x, largeur 10
        $cone = [System.Windows.Media.Geometry]::Parse('M0,0 L9,-5 L9,5 Z')
        $lead = [System.Windows.Media.Geometry]::Parse('M0,0 L3.4,-1.9 L3.4,1.9 Z')
        $body = [System.Windows.Media.RectangleGeometry]::new([System.Windows.Rect]::new(9, -5, 21, 10), 0.8, 0.8)
        $cap  = [System.Windows.Media.RectangleGeometry]::new([System.Windows.Rect]::new(30, -5, 6, 10), 2.5, 2.5)
        $band = [System.Windows.Media.RectangleGeometry]::new([System.Windows.Rect]::new(28.4, -5, 2.6, 10))
        # Le stylet reste dans le carré arrondi
        $dc.PushClip([System.Windows.Media.RectangleGeometry]::new($rect, 22, 22))

        # Ombre portée du stylet
        $shadow = [System.Windows.Media.TransformGroup]::new()
        $shadow.Children.Add($tg); $shadow.Children.Add([System.Windows.Media.TranslateTransform]::new(1.6, 2.4))
        $dc.PushTransform($shadow)
        $sh = B '#000000' 70
        foreach ($g in $cone, $body, $cap) { $dc.DrawGeometry($sh, $null, $g) }
        $dc.Pop()

        $dc.PushTransform($tg)
        $outline = P (B '#5D4037' 140) 0.6
        $dc.DrawGeometry((B '#FFE0B2'), $outline, $cone)                     # bois taillé
        $dc.DrawGeometry((B '#37474F'), $null, $lead)                        # mine
        $bodyBrush = [System.Windows.Media.LinearGradientBrush]::new((C '#FFD54F'), (C '#FFA000'), [System.Windows.Point]::new(0, 0), [System.Windows.Point]::new(0, 1))
        $bodyBrush.MappingMode = [System.Windows.Media.BrushMappingMode]::Absolute
        $bodyBrush.StartPoint = [System.Windows.Point]::new(0, -5); $bodyBrush.EndPoint = [System.Windows.Point]::new(0, 5)
        $dc.DrawGeometry($bodyBrush, $outline, $body)
        $dc.DrawGeometry((B '#2E7D32'), $outline, $cap)                      # embout vert
        $dc.DrawGeometry((B '#CFD8DC'), $null, $band)                        # bague métal
        $dc.DrawLine((P (B '#FFFFFF' 110) 1.2), [System.Windows.Point]::new(11, -2.8), [System.Windows.Point]::new(27.5, -2.8))  # reflet
        $dc.Pop()
        $dc.Pop()
    }
}

function Render([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 100.0, $size / 100.0))
    # Petites tailles : pas de détails, trait plus épais pour rester lisible
    $detail = $size -ge 48
    $chalk = if ($size -le 16) { 11 } elseif ($size -le 24) { 9.5 } elseif ($size -le 32) { 8.5 } else { 7 }
    Draw $dc $detail $chalk
    $dc.Pop(); $dc.Close()
    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    return $bmp
}

function ToPng($bmp) {
    $enc = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = [System.IO.MemoryStream]::new(); $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($s in $sizes) { , (ToPng (Render $s)) }

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
$ico = Join-Path $assets 'GreenBoard.ico'
[System.IO.File]::WriteAllBytes($ico, $ms.ToArray())
"Icône écrite : $ico ($([Math]::Round($ms.Length / 1KB)) Ko)"

$png = Join-Path $assets 'GreenBoard.png'
[System.IO.File]::WriteAllBytes($png, (ToPng (Render 512)))
"Image écrite : $png"

if ($Preview) {
    # Planche d'aperçu : toutes les tailles, à l'échelle 1, sur fond clair puis sombre
    $show = @(512) + ($sizes | Sort-Object -Descending)
    $width = ($show | Measure-Object -Sum).Sum + 20 * ($show.Count + 1)
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $rowH = 512 + 40
    foreach ($row in 0, 1) {
        $dc.DrawRectangle((B $(if ($row) { '#202124' } else { '#F3F4F6' })), $null, [System.Windows.Rect]::new(0, $row * $rowH, $width, $rowH))
        $x = 20
        foreach ($s in $show) {
            $dc.DrawImage((Render $s), [System.Windows.Rect]::new($x, $row * $rowH + 20 + (512 - $s), $s, $s))
            $x += $s + 20
        }
    }
    $dc.Close()
    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new([int]$width, $rowH * 2, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    New-Item -ItemType Directory -Force $Preview | Out-Null
    $prev = Join-Path $Preview 'icone-apercu.png'
    [System.IO.File]::WriteAllBytes($prev, (ToPng $bmp))
    "Aperçu : $prev"
}
