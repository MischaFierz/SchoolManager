Add-Type -AssemblyName System.Drawing

# Zeichnet das Programmsymbol: ein abgerundetes Quadrat im Akzentgrau der App
# mit einem hellen Doktorhut darauf.

$outIco = Join-Path $PSScriptRoot '../SchoolManager.App/app.ico'
$sizes = @(256, 64, 48, 32, 16)
$pngs = @()

# Gleiche Farben wie Theme.xaml: Accent / AccentPressed / AccentText
$accentTop = [System.Drawing.Color]::FromArgb(255, 99, 102, 108)
$accentBottom = [System.Drawing.Color]::FromArgb(255, 74, 77, 81)
$light = [System.Drawing.Color]::FromArgb(255, 242, 243, 244)

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0

    # Abgerundetes Quadrat als Hintergrund
    $r = 56 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90)
    $path.AddArc($size - 2*$r, 0, 2*$r, 2*$r, 270, 90)
    $path.AddArc($size - 2*$r, $size - 2*$r, 2*$r, 2*$r, 0, 90)
    $path.AddArc(0, $size - 2*$r, 2*$r, 2*$r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($size, $size)),
        $accentTop, $accentBottom)
    $g.FillPath($brush, $path)

    $capBrush = New-Object System.Drawing.SolidBrush($light)

    # Hutplatte als Raute
    $board = @(
        (New-Object System.Drawing.PointF((128 * $s), (68 * $s))),
        (New-Object System.Drawing.PointF((222 * $s), (112 * $s))),
        (New-Object System.Drawing.PointF((128 * $s), (156 * $s))),
        (New-Object System.Drawing.PointF((34 * $s),  (112 * $s)))
    )
    $g.FillPolygon($capBrush, $board)

    # Kopfteil darunter, unten leicht gerundet
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddLine((88 * $s), (134 * $s), (168 * $s), (134 * $s))
    $body.AddBezier(
        (168 * $s), (134 * $s),
        (168 * $s), (196 * $s),
        (88 * $s),  (196 * $s),
        (88 * $s),  (134 * $s))
    $body.CloseFigure()
    $g.FillPath($capBrush, $body)

    # Quaste rechts
    $pen = New-Object System.Drawing.Pen($light, [Math]::Max(1.0, 13 * $s))
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $g.DrawLine($pen, (205 * $s), (120 * $s), (205 * $s), (176 * $s))
    $g.FillEllipse($capBrush, (194 * $s), (172 * $s), (22 * $s), (26 * $s))

    $pen.Dispose(); $capBrush.Dispose(); $brush.Dispose(); $path.Dispose(); $body.Dispose(); $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

# ICO-Datei zusammensetzen (PNG-Nutzlast, ab Windows Vista unterstuetzt)
$fs = [System.IO.File]::Create($outIco)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)                # reserviert
$bw.Write([UInt16]1)                # Typ: Icon
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([Byte]$dim)           # Breite
    $bw.Write([Byte]$dim)           # Hoehe
    $bw.Write([Byte]0)              # Farbanzahl
    $bw.Write([Byte]0)              # reserviert
    $bw.Write([UInt16]1)            # Ebenen
    $bw.Write([UInt16]32)           # Bit pro Pixel
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "geschrieben: $outIco ($((Get-Item $outIco).Length) Bytes)"
