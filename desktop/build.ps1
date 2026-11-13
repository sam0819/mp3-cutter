# 編譯 MP3 剪接器桌面版並打包成 zip。只用 Windows 內建的 .NET Framework 編譯器，不需要另外安裝工具。
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'MP3剪接器'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Add-Type -AssemblyName System.Drawing

# 圖示：深青底、黃色波形，中間一道斜切的接點
$icon = Join-Path $here 'icon.ico'
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$path.AddArc(8, 8, $r, $r, 180, 90); $path.AddArc(248 - $r, 8, $r, $r, 270, 90)
$path.AddArc(248 - $r, 248 - $r, $r, $r, 0, 90); $path.AddArc(8, 248 - $r, $r, $r, 90, 90)
$path.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(31, 111, 134))), $path)
$yellow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 183, 5))
$heights = 50, 96, 140, 72, 168, 110, 60, 130, 88, 44
for ($i = 0; $i -lt $heights.Count; $i++) {
  $x = 36 + $i * 19; $h = $heights[$i]
  $g.FillRectangle($yellow, $x, 128 - $h / 2, 12, $h)
}
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(31, 111, 134)), 10
$g.DrawLine($pen, 112, 216, 148, 40)
$g.Dispose()
$png = New-Object System.IO.MemoryStream
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $png.ToArray()
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)
$w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0); $w.Write([Byte]0)
$w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$bytes.Length); $w.Write([UInt32]22)
$w.Write($bytes); $w.Flush()
[System.IO.File]::WriteAllBytes($icon, $ico.ToArray())

if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
$exe = Join-Path $stage 'MP3剪接器.exe'
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$icon" `
  "/resource:$(Join-Path $root 'index.html'),index.html" /r:System.Windows.Forms.dll (Join-Path $here 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

$readme = @"
MP3 剪接器（桌面版）

使用方式
  解壓縮後，雙擊「MP3剪接器.exe」。會開一個獨立視窗，用法和網頁版相同。
  匯出的 MP3 會存到「下載」資料夾。關閉視窗就結束程式。

需求
  Windows 10 / 11，並裝有 Microsoft Edge 或 Google Chrome（Windows 11 內建 Edge）。
  只剪 MP3 時不需要網路；含 M4A 或不同規格的 MP3 要轉檔時，需要網路載入 MP3 編碼器。

注意
  這個程式沒有數位簽章，第一次開啟時 Windows 可能跳出「Windows 已保護您的電腦」，
  按「其他資訊」→「仍要執行」即可。音檔只在這台電腦處理，不會上傳。

網頁版：https://sam0819.github.io/mp3-cutter/
"@
[System.IO.File]::WriteAllText((Join-Path $stage '說明.txt'), $readme, (New-Object System.Text.UTF8Encoding $true))

$zip = Join-Path $dist 'MP3剪接器.zip'
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
Get-Item $exe, $zip | Select-Object Name, Length
