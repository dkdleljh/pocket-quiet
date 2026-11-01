# Mechanical PNG-to-ICO conversion; the generated artwork is preserved unchanged.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\PocketQuiet.App\Assets'
$source = [System.Drawing.Bitmap]::FromFile((Join-Path $assets 'PocketQuiet.png'))
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
try {
 foreach ($size in $sizes) {
  $bitmap = New-Object System.Drawing.Bitmap($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $stream = New-Object System.IO.MemoryStream
  try {
   $graphics.Clear([System.Drawing.Color]::Transparent)
   $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
   $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
   $graphics.DrawImage($source,0,0,$size,$size)
   $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
   $frames.Add($stream.ToArray())
  } finally {$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
 }
 $file = [System.IO.File]::Create((Join-Path $assets 'PocketQuiet.ico'))
 $writer = New-Object System.IO.BinaryWriter($file)
 try {
  $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
  $offset = 6+16*$sizes.Count
  for ($i=0;$i -lt $sizes.Count;$i++) {
   $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
   $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
   $writer.Write([byte]0);$writer.Write([byte]0)
   $writer.Write([uint16]1);$writer.Write([uint16]32)
   $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
   $offset += $frames[$i].Length
  }
  foreach($frame in $frames){$writer.Write($frame)}
 } finally {$writer.Dispose();$file.Dispose()}
 Write-Host "ICO created with $($sizes.Count) alpha-preserving sizes. Source corner alpha: $($source.GetPixel(0,0).A)"
} finally {$source.Dispose()}
