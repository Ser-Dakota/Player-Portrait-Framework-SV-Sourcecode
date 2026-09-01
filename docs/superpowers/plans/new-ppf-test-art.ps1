# Generates the throwaway acceptance-test art for Player Portraits Framework V2.
# Every cell/frame gets a distinct flat colour and a large painted digit, so the slot or
# frame that is actually on screen is unmistakable at a glance.
#
# Usage (from anywhere):
#   powershell -ExecutionPolicy Bypass -File new-ppf-test-art.ps1 -OutRoot "<game>\Mods"

param(
    [Parameter(Mandatory = $true)][string]$OutRoot
)

Add-Type -AssemblyName System.Drawing

$palette = @(
    [System.Drawing.Color]::FromArgb(220,  60,  60),   # red
    [System.Drawing.Color]::FromArgb(225, 150,  40),   # orange
    [System.Drawing.Color]::FromArgb( 60, 160,  80),   # green
    [System.Drawing.Color]::FromArgb( 55, 115, 215),   # blue
    [System.Drawing.Color]::FromArgb(150,  70, 200),   # purple
    [System.Drawing.Color]::FromArgb( 45, 175, 175)    # teal
)

function New-PpfGrid {
    param(
        [string]$Path,
        [int]$Columns,
        [int]$Rows,
        [int]$Cell,
        [int]$Shift = 0,      # rotates the palette so "winter" variants look obviously different
        [string]$Tag = ''     # extra label painted under the digit
    )

    $bmp = New-Object System.Drawing.Bitmap -ArgumentList (($Columns * $Cell), ($Rows * $Cell))
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    $bigFont   = New-Object System.Drawing.Font -ArgumentList 'Arial', ([single]($Cell * 0.45)), ([System.Drawing.FontStyle]::Bold)
    $smallFont = New-Object System.Drawing.Font -ArgumentList 'Arial', ([single]($Cell * 0.11)), ([System.Drawing.FontStyle]::Bold)

    $center = New-Object System.Drawing.StringFormat
    $center.Alignment     = [System.Drawing.StringAlignment]::Center
    $center.LineAlignment = [System.Drawing.StringAlignment]::Center

    for ($i = 0; $i -lt ($Columns * $Rows); $i++) {
        $col = $i % $Columns
        $row = [math]::Floor($i / $Columns)
        $x   = $col * $Cell
        $y   = $row * $Cell

        $color = $palette[($i + $Shift) % $palette.Count]
        $brush = New-Object System.Drawing.SolidBrush -ArgumentList $color
        $g.FillRectangle($brush, $x, $y, $Cell, $Cell)
        $brush.Dispose()

        # A border makes cell boundaries visible, so a mis-computed rect is obvious on screen.
        $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::Black), 4
        $g.DrawRectangle($pen, $x + 2, $y + 2, $Cell - 4, $Cell - 4)
        $pen.Dispose()

        $digitBox = New-Object System.Drawing.RectangleF -ArgumentList ([single]$x), ([single]$y), ([single]$Cell), ([single]($Cell * 0.85))
        $g.DrawString("$i", $bigFont, [System.Drawing.Brushes]::White, $digitBox, $center)

        if ($Tag) {
            $tagBox = New-Object System.Drawing.RectangleF -ArgumentList ([single]$x), ([single]($y + $Cell * 0.72)), ([single]$Cell), ([single]($Cell * 0.2))
            $g.DrawString($Tag, $smallFont, [System.Drawing.Brushes]::White, $tagBox, $center)
        }
    }

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $center.Dispose(); $smallFont.Dispose(); $bigFont.Dispose(); $g.Dispose(); $bmp.Dispose()
    Write-Output "wrote $Path  ($($Columns * $Cell)x$($Rows * $Cell))"
}

$sheetDir = Join-Path $OutRoot '_PPFTestEmotionSheet\assets'
$animDir  = Join-Path $OutRoot '_PPFTestEmotionAnim\assets'

# Mode 2 — one sheet, 2 columns x 3 rows of 256px slots (slots 0-5).
New-PpfGrid -Path (Join-Path $sheetDir 'sheet.png')        -Columns 2 -Rows 3 -Cell 256 -Shift 0 -Tag 'BASE'
New-PpfGrid -Path (Join-Path $sheetDir 'sheet_winter.png') -Columns 2 -Rows 3 -Cell 256 -Shift 3 -Tag 'WINTER'

# Mode 3 — one horizontal strip per emotion, different lengths on purpose.
New-PpfGrid -Path (Join-Path $animDir 'emotion0.png')        -Columns 4 -Rows 1 -Cell 256 -Shift 0 -Tag 'E0'
New-PpfGrid -Path (Join-Path $animDir 'emotion1.png')        -Columns 2 -Rows 1 -Cell 256 -Shift 2 -Tag 'E1'
New-PpfGrid -Path (Join-Path $animDir 'emotion1_winter.png') -Columns 2 -Rows 1 -Cell 256 -Shift 4 -Tag 'E1-WINTER'
