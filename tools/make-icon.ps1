# Makes src/Udjat/Assets/udjat.ico: the Eye of Horus in gold with depth, on a lapis-blue rounded tile with a gold rim, at 16 to 256 pixels.
# Run it again only if the drawing changes:   powershell -STA -File tools/make-icon.ps1
#
# The eye is the same drawing as src/Udjat/Themes/Shared.xaml (EyeShapes, EyeSpiral, EyePupil: a 120 x 96 box), so change them together.
# The tile has a glazed shine and, from 48 pixels up, a few gold flecks. The icon is a PNG inside each size's entry, which every
# Windows since Vista reads.
param([string]$Output = (Join-Path $PSScriptRoot '..\src\Udjat\Assets\udjat.ico'))

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

function C($hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Solid($hex) { New-Object System.Windows.Media.SolidColorBrush((C $hex)) }
function Pt($x, $y) { New-Object System.Windows.Point($x, $y) }
function Pen($brush, $w) {
    $p = New-Object System.Windows.Media.Pen($brush, $w)
    $p.LineJoin = 'Round'; $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'
    return $p
}
function Linear($stops, $x1, $y1, $x2, $y2, [switch]$Absolute) {
    $b = New-Object System.Windows.Media.LinearGradientBrush
    $b.StartPoint = Pt $x1 $y1; $b.EndPoint = Pt $x2 $y2
    if ($Absolute) { $b.MappingMode = 'Absolute' }
    foreach ($s in $stops) { $b.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C $s[0]), $s[1]))) }
    return $b
}
function Radial($stops) {
    $b = New-Object System.Windows.Media.RadialGradientBrush
    $b.Center = Pt 0.5 0.4; $b.GradientOrigin = $b.Center; $b.RadiusX = 0.75; $b.RadiusY = 0.75
    foreach ($s in $stops) { $b.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C $s[0]), $s[1]))) }
    return $b
}

$shapes = [System.Windows.Media.Geometry]::Parse(
    'M 18,25 C 40,7 88,5 116,15 L 115,21 C 88,12 44,14 22,30 Z ' +
    'M 11,47 C 30,21 80,17 103,35 L 119,35 L 119,42 L 101,42 C 80,28 32,30 17,46 Z ' +
    'M 17,46 C 38,62 80,60 101,42 L 105,42 C 82,65 36,67 11,47 Z ' +
    'M 37,60 L 46,60 L 45,80 C 44,88 40,93 31,95 C 36,89 37,84 37,80 Z')
$spiral = [System.Windows.Media.Geometry]::Parse('M 70,61 C 72,74 85,81 97,80 C 109,79 112,66 103,63 C 95,60 92,70 99,72')

$gold = @(@('#FFF7CC', 0), @('#F8D774', 0.28), @('#D9A21B', 0.62), @('#8C5A00', 1))
$lapis = @(@('#3B5FB8', 0), @('#16327A', 0.55), @('#081440', 1))
$fit = 0.80

function New-Visual([scriptblock]$draw, $effect = $null) {
    $v = New-Object System.Windows.Media.DrawingVisual
    $dc = $v.RenderOpen(); & $draw $dc; $dc.Close()
    if ($effect) { $v.Effect = $effect }
    return $v
}

# The eye in one brush, moved by (dx, dy) pixels, centred in a square of the given size.
function Draw-Eye($dc, $size, $brush, [double]$dy = 0) {
    $scale = $size * $fit / 120
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform((($size - 120 * $scale) / 2), (($size - 96 * $scale) / 2 + $dy))))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $dc.DrawGeometry($null, (Pen $brush 5), $spiral)
    $dc.DrawEllipse($brush, $null, (Pt 57 39), 11, 11)
    $dc.DrawGeometry($brush, $null, $shapes)
    $dc.Pop(); $dc.Pop()
}

function New-Shadow([double]$depth, [double]$blur) {
    $e = New-Object System.Windows.Media.Effects.DropShadowEffect
    $e.ShadowDepth = $depth; $e.BlurRadius = $blur; $e.Opacity = 0.75; $e.Direction = 270; $e.Color = (C '#000000')
    return $e
}

# One size, as PNG bytes.
function Render-Png([int]$size) {
    $rim = [Math]::Max(2.0, $size / 20)
    $corner = $size * 0.22
    $inside = New-Object System.Windows.Rect(($rim / 2), ($rim / 2), ($size - $rim), ($size - $rim))

    # The tile, its flecks and its shine. Each number is worked out first: inside a call, a comma binds tighter than a minus.
    $back = New-Visual {
        param($dc)
        $dc.DrawRoundedRectangle((Radial $lapis), (Pen (Linear $gold 0 0 0 1) $rim), $inside, $corner, $corner)
        if ($size -ge 48) {
            foreach ($p in @(@(0.18, 0.2), @(0.8, 0.15), @(0.87, 0.82), @(0.13, 0.8), @(0.5, 0.91), @(0.3, 0.11))) {
                $dc.DrawEllipse((Solid '#E8C15A'), $null, (Pt ($size * $p[0]) ($size * $p[1])), ($size / 130), ($size / 130))
            }
        }
        $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry($inside, $corner, $corner)))
        $half = New-Object System.Windows.Rect(0, 0, $size, ($size * 0.5))
        $dc.DrawRectangle((Linear @(@('#29FFFFFF', 0), @('#00FFFFFF', 1)) 0 0 0 1), $null, $half)
        $dc.Pop()
    }

    # The eye with depth: a dark copy under it (with a shadow), a light copy over it, then the gold gradient on top.
    $o = [Math]::Max(0.5, $size / 160)
    $under = New-Visual { param($dc) Draw-Eye $dc $size (Solid '#4A2E00') ($o * 1.6) } (New-Shadow ([Math]::Max(1, $size / 70)) ([Math]::Max(2, $size / 28)))
    $over = New-Visual { param($dc) Draw-Eye $dc $size (Solid '#FFF9E0') (-$o) }
    $main = New-Visual { param($dc) Draw-Eye $dc $size (Linear $gold 0 8 0 96 -Absolute) 0 }

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    foreach ($visual in $back, $under, $over, $main) { $bitmap.Render($visual) }
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (Render-Png $size) }

# The .ico file: a 6-byte header, a 16-byte entry per size, then the PNG images.
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256 in the format
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$target = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
[System.IO.File]::WriteAllBytes($target, $file.ToArray())
"wrote $target ($($file.Length) bytes, sizes: $($sizes -join ', '))"

# The largest size as a picture, to look at.
$preview = [System.IO.Path]::ChangeExtension($target, '.preview.png')
[System.IO.File]::WriteAllBytes($preview, $images[$images.Count - 1])
