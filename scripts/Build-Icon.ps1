param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\ClickBoard.App\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($assetRoot) | Out-Null
function RoundedPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x,$y,$d,$d,180,90); $path.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90); $path.AddArc($x,($y+$h-$d),$d,$d,90,90); $path.CloseFigure()
    return ,$path
}
function RenderIcon([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform(($size/256.0),($size/256.0))
    $tile = RoundedPath 8 8 240 240 56
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(32,8),[Drawing.Point]::new(224,248),[Drawing.ColorTranslator]::FromHtml('#3569F6'),[Drawing.ColorTranslator]::FromHtml('#6541D9'))
    $g.FillPath($gradient,$tile)
    $back = RoundedPath 48 78 132 140 22
    $backBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(65,185,220,255)); $g.FillPath($backBrush,$back)
    $shadow = RoundedPath 65 58 139 150 22
    $shadowBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(35,15,22,70)); $g.FillPath($shadowBrush,$shadow)
    $paper = [Drawing.Drawing2D.GraphicsPath]::new()
    $paper.StartFigure(); $paper.AddLine(91,48,151,48); $paper.AddLine(151,48,200,97); $paper.AddLine(200,97,200,180)
    $paper.AddArc(156,158,44,44,0,90); $paper.AddLine(178,202,91,202); $paper.AddArc(69,158,44,44,90,90)
    $paper.AddLine(69,180,69,70); $paper.AddArc(69,48,44,44,180,90); $paper.CloseFigure()
    $white = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F5F8FF')); $g.FillPath($white,$paper)
    $fold = [Drawing.Drawing2D.GraphicsPath]::new(); $fold.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(151,48),[Drawing.PointF]::new(151,97),[Drawing.PointF]::new(200,97)))
    $foldBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#A7C8FF')); $g.FillPath($foldBrush,$fold)
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#3F4CC9'),13)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round; $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(101,116),[Drawing.PointF]::new(121,135),[Drawing.PointF]::new(101,154)))
    $g.DrawLine($pen,141,155,165,155)
    foreach($item in @($g,$tile,$gradient,$back,$backBrush,$shadow,$shadowBrush,$paper,$white,$fold,$foldBrush,$pen)) { $item.Dispose() }
    return ,$bitmap
}
$images = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16,20,24,32,40,48,64,128,256)
foreach($size in $sizes) {
    # Supersampling preserves the mark in the tray sizes.
    $source = RenderIcon 1024
    $resized = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($resized); $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($source,0,0,$size,$size); $graphics.Dispose(); $source.Dispose()
    $stream = [IO.MemoryStream]::new(); $resized.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $images.Add($stream.ToArray()); $stream.Dispose(); $resized.Dispose()
}
$preview = RenderIcon 1024
$preview.Save((Join-Path $assetRoot 'ClipForge.png'),[Drawing.Imaging.ImageFormat]::Png); $preview.Dispose()
$output = [IO.File]::Create((Join-Path $assetRoot 'ClipForge.ico')); $writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for($index=0;$index -lt $sizes.Count;$index++) {
        $dimension = if($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
