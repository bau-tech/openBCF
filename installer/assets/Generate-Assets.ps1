# Generates the installer's wizard banner, small corner logo, and .ico from buildingSMART's
# official 1000x1000 BCF icon (the round swirl mark, not the "BCF" wordmark lockup - we pair the
# mark with our own "openBCF" name rather than reusing buildingSMART's wordmark, since this is an
# unofficial client, not a buildingSMART product). Re-run this after replacing the source icon;
# the outputs are committed since Inno Setup needs them at compile time and PowerShell/
# System.Drawing isn't guaranteed on every machine that runs ISCC.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetsDir = $PSScriptRoot
$sourceIcon = Join-Path $assetsDir "buildingSMART BCF icon - color.png"
$source = [System.Drawing.Image]::FromFile($sourceIcon)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return , @($bmp, $g)
}

# --- Wizard banner (WizardImageFile): 164x314, shown on the Welcome/Finish pages ---
$w = 164; $h = 314
$pair = New-Canvas $w $h
$bmp = $pair[0]; $g = $pair[1]

$g.Clear([System.Drawing.Color]::White)

# Soft vertical wash behind the logo so it doesn't float on flat white.
$washRect = New-Object System.Drawing.Rectangle 0, 0, $w, 170
$wash = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $washRect, [System.Drawing.Color]::FromArgb(255, 246, 248, 252), [System.Drawing.Color]::White,
    [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillRectangle($wash, $washRect)

$logoSize = 96
$logoX = [int](($w - $logoSize) / 2)
$g.DrawImage($source, $logoX, 34, $logoSize, $logoSize)

$titleFont = New-Object System.Drawing.Font("Segoe UI Semibold", 15, [System.Drawing.FontStyle]::Bold)
$titleFormat = New-Object System.Drawing.StringFormat
$titleFormat.Alignment = [System.Drawing.StringAlignment]::Center
$titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 34, 45))
$g.DrawString("openBCF", $titleFont, $titleBrush, (New-Object System.Drawing.RectangleF(0, 146, $w, 26)), $titleFormat)

$subFont = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 110, 116, 128))
$subFormat = New-Object System.Drawing.StringFormat
$subFormat.Alignment = [System.Drawing.StringAlignment]::Center
$subFormat.LineAlignment = [System.Drawing.StringAlignment]::Near
$subRect = New-Object System.Drawing.RectangleF(10, 176, ($w - 20), 60)
$g.DrawString("Open BCF client for Revit and Tekla Structures", $subFont, $subBrush, $subRect, $subFormat)

# Brand accent bar at the very bottom, sampled from the logo's own palette (red -> blue swirl).
$accentRect = New-Object System.Drawing.Rectangle 0, ($h - 6), $w, 6
$accent = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $accentRect, [System.Drawing.Color]::FromArgb(255, 226, 38, 65), [System.Drawing.Color]::FromArgb(255, 27, 92, 202),
    [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
$g.FillRectangle($accent, $accentRect)

$bmp.Save((Join-Path $assetsDir "wizard-image.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose()

# --- Small corner logo (WizardSmallImageFile): 58x58, shown top-right on every other page ---
$w = 58; $h = 58
$pair = New-Canvas $w $h
$bmp = $pair[0]; $g = $pair[1]
$g.Clear([System.Drawing.Color]::White)
$g.DrawImage($source, 5, 5, 48, 48)
$bmp.Save((Join-Path $assetsDir "wizard-small-image.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose()

# --- Setup .ico (SetupIconFile / uninstall entry icon) ---
# Built as a single 256x256 PNG-compressed frame inside a minimal ICO container (supported since
# Vista) - simpler and sharper at large sizes than juggling multiple raw BMP frames from a source
# that's only 32x32 to begin with.
$icoSize = 256
$pair = New-Canvas $icoSize $icoSize
$bmp = $pair[0]; $g = $pair[1]
# Transparent background so it looks native in Explorer/taskbar, not boxed in white.
$g.Clear([System.Drawing.Color]::Transparent)
$pad = 20
$g.DrawImage($source, $pad, $pad, ($icoSize - 2 * $pad), ($icoSize - 2 * $pad))
$pngPath = Join-Path $assetsDir "_icon-temp.png"
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
$icoPath = Join-Path $assetsDir "openBCF.ico"
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type: icon
$bw.Write([UInt16]1)      # image count
$bw.Write([Byte]0)        # width: 0 = 256
$bw.Write([Byte]0)        # height: 0 = 256
$bw.Write([Byte]0)        # palette
$bw.Write([Byte]0)        # reserved
$bw.Write([UInt16]1)      # color planes
$bw.Write([UInt16]32)     # bits per pixel
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)     # offset: 6-byte header + 16-byte dir entry
$bw.Write($pngBytes)
$bw.Flush(); $fs.Close()
Remove-Item $pngPath

$source.Dispose()
Write-Host "Generated wizard-image.bmp, wizard-small-image.bmp, openBCF.ico in $assetsDir"
