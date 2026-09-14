# generate_icons.ps1 - 生成 TreeListView 的彩色 PNG 圖示
Add-Type -AssemblyName System.Drawing

$size = 32
$assetsDir = "c:\Users\Administrator\Documents\SControl\BIOSForwardManager\Assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

function Save-Icon {
    param([string]$Name, [ScriptBlock]$Draw)
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $Draw $g
    $g.Dispose()
    $bmp.Save("$assetsDir\$Name", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  Created: $Name"
}

Write-Host "Generating icons to: $assetsDir"

# 1. Folder (📁) - Gold
Save-Icon "folder.png" {
    param($g)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 193, 7))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 167, 38), 1.5)
    # Tab
    $tab = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tab.AddLines(@([System.Drawing.PointF]::new(5,10), [System.Drawing.PointF]::new(5,7), [System.Drawing.PointF]::new(11,7), [System.Drawing.PointF]::new(14,10)))
    $g.FillPath($fill, $tab)
    $g.DrawPath($border, $tab)
    # Body
    $g.FillRectangle($fill, 4, 10, 24, 16)
    $g.DrawRectangle($border, 4, 10, 24, 16)
    $fill.Dispose(); $border.Dispose(); $tab.Dispose()
}

# 2. Globe (🌐) - Blue
Save-Icon "globe.png" {
    param($g)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 33, 150, 243))
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 255, 255), 1.2)
    $g.FillEllipse($fill, 4, 4, 24, 24)
    # Equator
    $g.DrawLine($line, 4, 16, 28, 16)
    # Meridian
    $g.DrawLine($line, 16, 4, 16, 28)
    # Latitude arcs
    $g.DrawArc($line, 7, 4, 18, 24, 0, 360)
    # Longitude arcs
    $g.DrawArc($line, 4, 7, 24, 18, 0, 360)
    $fill.Dispose(); $line.Dispose()
}

# 3. Lock (🔒) - Gray
Save-Icon "lock.png" {
    param($g)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 97, 97, 97))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 69, 69, 69), 1.5)
    # Shackle
    $g.DrawArc($border, 10, 4, 12, 14, 180, 180)
    # Body
    $g.FillRectangle($fill, 7, 14, 18, 13)
    $g.DrawRectangle($border, 7, 14, 18, 13)
    # Keyhole
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillEllipse($white, 14, 17, 4, 4)
    $g.FillRectangle($white, 15, 20, 2, 4)
    $fill.Dispose(); $border.Dispose(); $white.Dispose()
}

# 4. Monitor (🖥️) - Blue-gray
Save-Icon "monitor.png" {
    param($g)
    $frame = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 55, 71, 79))
    $screen = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 100, 181, 246))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 38, 50, 56), 1.5)
    # Screen frame
    $g.FillRectangle($frame, 3, 4, 26, 18)
    $g.DrawRectangle($border, 3, 4, 26, 18)
    # Screen area
    $g.FillRectangle($screen, 5, 6, 22, 14)
    # Stand
    $g.FillRectangle($frame, 13, 22, 6, 4)
    $g.FillRectangle($frame, 9, 26, 14, 3)
    $frame.Dispose(); $screen.Dispose(); $border.Dispose()
}

# 5. Bug (🐞) - Red
Save-Icon "bug.png" {
    param($g)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 244, 67, 54))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 183, 28, 28), 1.5)
    # Legs
    $leg = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 183, 28, 28), 1.5)
    $g.DrawLine($leg, 9, 13, 4, 10)
    $g.DrawLine($leg, 8, 18, 3, 18)
    $g.DrawLine($leg, 9, 23, 4, 26)
    $g.DrawLine($leg, 23, 13, 28, 10)
    $g.DrawLine($leg, 24, 18, 29, 18)
    $g.DrawLine($leg, 23, 23, 28, 26)
    # Body
    $g.FillEllipse($fill, 8, 9, 16, 18)
    $g.DrawEllipse($border, 8, 9, 16, 18)
    # Head
    $g.FillEllipse($fill, 12, 4, 8, 8)
    $g.DrawEllipse($border, 12, 4, 8, 8)
    # Center line
    $g.DrawLine($border, 16, 9, 16, 27)
    $fill.Dispose(); $border.Dispose(); $leg.Dispose()
}

# 6. Scroll (📜) - Tan/Brown
Save-Icon "scroll.png" {
    param($g)
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 141, 110, 99))
    $roll = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 121, 85, 72))
    $border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 93, 64, 55), 1.5)
    # Body
    $g.FillRectangle($fill, 8, 6, 16, 20)
    $g.DrawRectangle($border, 8, 6, 16, 20)
    # Top roll
    $g.FillEllipse($roll, 6, 3, 20, 7)
    $g.DrawEllipse($border, 6, 3, 20, 7)
    # Bottom roll
    $g.FillEllipse($roll, 6, 22, 20, 7)
    $g.DrawEllipse($border, 6, 22, 20, 7)
    # Text lines
    $txt = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 62, 39, 35), 1)
    $g.DrawLine($txt, 11, 12, 21, 12)
    $g.DrawLine($txt, 11, 15, 21, 15)
    $g.DrawLine($txt, 11, 18, 18, 18)
    $fill.Dispose(); $roll.Dispose(); $border.Dispose(); $txt.Dispose()
}

# 7. Link (🔗) - Blue
Save-Icon "link.png" {
    param($g)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 33, 150, 243), 3.5)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    # Two interlocking arcs
    $g.DrawArc($pen, 3, 9, 16, 14, 90, 180)
    $g.DrawArc($pen, 13, 9, 16, 14, 270, 180)
    $pen.Dispose()
}

Write-Host "`nAll 7 icons generated successfully!"
