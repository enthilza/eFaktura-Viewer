param([string]$PreviewDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssets = Join-Path $PSScriptRoot '../src/eFakturaViewer/Assets'
New-Item -ItemType Directory -Force $taskAssets | Out-Null
if ($PreviewDirectory) { New-Item -ItemType Directory -Force $PreviewDirectory | Out-Null }
# Vlastne velkosti zabranuju tomu, aby Windows pre malu ikonu iba zmensil velky raster.
$taskSizes = @(16,24,32,48,64,128,256)
$taskFrames = [Collections.Generic.List[byte[]]]::new()
foreach ($taskSize in $taskSizes) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize,$taskSize,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    # ClearType by na priehladnom okraji pismena vytvoril farebne lemy. Pouzijeme
    # bezne vyhladzovanie, aby ikona zostala ciernobiela na svetlom aj tmavom pozadi.
    $taskGraphics.SmoothingMode = 'AntiAlias'; $taskGraphics.TextRenderingHint = 'AntiAliasGridFit'; $taskGraphics.Clear([Drawing.Color]::Transparent)
    $taskGraphics.ScaleTransform(($taskSize / 256.0),($taskSize / 256.0))
    $taskPaper = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(250,250,250))
    $taskFold = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(214,214,214))
    $taskInk = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(49,49,49))
    $taskPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(88,88,88),6)
    $taskPen.LineJoin = 'Round'; $taskPen.StartCap = 'Round'; $taskPen.EndCap = 'Round'
    $taskOutline = [Drawing.Drawing2D.GraphicsPath]::new()
    $taskOutline.AddLines([Drawing.PointF[]]@([Drawing.PointF]::new(58,20),[Drawing.PointF]::new(166,20),[Drawing.PointF]::new(208,62),[Drawing.PointF]::new(208,214)))
    $taskOutline.AddArc(188,204,20,20,0,90); $taskOutline.AddLine(198,224,58,224)
    $taskOutline.AddArc(48,204,20,20,90,90); $taskOutline.AddLine(48,214,48,30); $taskOutline.AddArc(48,20,20,20,180,90); $taskOutline.CloseFigure()
    $taskGraphics.FillPath($taskPaper,$taskOutline); $taskGraphics.DrawPath($taskPen,$taskOutline)
    $taskGraphics.FillPolygon($taskFold,[Drawing.PointF[]]@([Drawing.PointF]::new(166,23),[Drawing.PointF]::new(166,62),[Drawing.PointF]::new(205,62)))
    $taskGraphics.DrawLines($taskPen,[Drawing.PointF[]]@([Drawing.PointF]::new(166,25),[Drawing.PointF]::new(166,62),[Drawing.PointF]::new(203,62)))
    $taskGraphics.FillRectangle($taskInk,72,48,66,10)
    $taskGraphics.DrawLine($taskPen,73,91,182,91); $taskGraphics.DrawLine($taskPen,73,115,182,115); $taskGraphics.DrawLine($taskPen,73,139,182,139)
    $taskGraphics.DrawLine($taskPen,151,91,151,139); $taskGraphics.DrawLine($taskPen,73,176,120,176)
    $taskGraphics.FillEllipse($taskInk,139,161,92,82)
    $taskRing = [Drawing.Pen]::new([Drawing.Color]::White,5); $taskGraphics.DrawEllipse($taskRing,139,161,92,82)
    $taskFont = [Drawing.Font]::new('Segoe UI',56,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
    $taskFormat = [Drawing.StringFormat]::new(); $taskFormat.Alignment = 'Center'; $taskFormat.LineAlignment = 'Center'
    $taskGraphics.DrawString('e',$taskFont,$taskPaper,[Drawing.RectangleF]::new(139,155,92,82),$taskFormat)
    $taskPng = [IO.MemoryStream]::new(); $taskBitmap.Save($taskPng,[Drawing.Imaging.ImageFormat]::Png); $taskFrames.Add($taskPng.ToArray())
    if ($PreviewDirectory) { $taskBitmap.Save((Join-Path $PreviewDirectory "icon-$taskSize.png"),[Drawing.Imaging.ImageFormat]::Png) }
    $taskPng.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose(); $taskPaper.Dispose(); $taskFold.Dispose(); $taskInk.Dispose(); $taskPen.Dispose(); $taskRing.Dispose(); $taskOutline.Dispose(); $taskFont.Dispose(); $taskFormat.Dispose()
}
$taskWriter = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $taskAssets 'app.ico')))
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
    # ICO ma 6-bajtovu hlavicku a 16 bajtov na kazdy zaznam; PNG data nasleduju az za celou tabulkou.
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
        # Rozmer v ICO je jeden bajt; hodnota 0 je predpisany zapis rozmeru 256, nie prazdna ikona.
        $taskSide = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
        $taskWriter.Write([byte]$taskSide); $taskWriter.Write([byte]$taskSide); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskFrames[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskFrames[$taskIndex].Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write($taskFrame) }
} finally { $taskWriter.Dispose() }
