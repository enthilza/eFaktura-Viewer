$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssets = Join-Path $PSScriptRoot '../src/eFakturaViewer/Assets'
New-Item -ItemType Directory -Force $taskAssets | Out-Null
$taskBitmap = New-Object Drawing.Bitmap(256,256)
$taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
$taskGraphics.SmoothingMode = 'AntiAlias'
$taskGraphics.Clear([Drawing.Color]::FromArgb(40,42,47))
$taskWhite = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(247,246,244))
$taskOrange = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(231,135,61))
$taskGraphics.FillRectangle($taskWhite,65,36,126,184)
$taskGraphics.FillRectangle($taskOrange,65,36,126,30)
$taskPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(104,108,114),9)
foreach ($taskY in @(94,124,154)) { $taskGraphics.DrawLine($taskPen,88,$taskY,167,$taskY) }
$taskGraphics.FillEllipse($taskOrange,143,165,78,78)
$taskFont = New-Object Drawing.Font('Arial',38,[Drawing.FontStyle]::Bold)
$taskGraphics.DrawString('e',$taskFont,$taskWhite,158,171)
$taskPng = New-Object IO.MemoryStream
$taskBitmap.Save($taskPng,[Drawing.Imaging.ImageFormat]::Png)
$taskBytes = $taskPng.ToArray()
$taskOutput = [IO.File]::Create((Join-Path $taskAssets 'app.ico'))
$taskWriter = New-Object IO.BinaryWriter($taskOutput)
$taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]1)
$taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
$taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskBytes.Length); $taskWriter.Write([uint32]22); $taskWriter.Write($taskBytes)
$taskWriter.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose(); $taskPng.Dispose(); $taskPen.Dispose(); $taskFont.Dispose(); $taskWhite.Dispose(); $taskOrange.Dispose()
