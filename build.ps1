param(
    [string]$OutputDirectory = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $root $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(47,100,245))
$white = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),4
$graphics.FillEllipse($blue,2,2,60,60)
$graphics.DrawRectangle($white,16,22,32,24)
$graphics.DrawEllipse($white,25,27,14,14)
$graphics.DrawLine($white,23,18,34,18)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $OutputDirectory 'app.ico'
$stream = [System.IO.File]::Create($iconPath)
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$white.Dispose()
$blue.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$appName = -join ([char[]](0x8F7B, 0x622A, 0x56FE))
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/win32icon:$iconPath" "/out:$(Join-Path $OutputDirectory ($appName + '.exe'))" (Join-Path $root 'ScreenshotTool.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($OutputDirectory.TrimEnd('\') -ne $root.TrimEnd('\')) {
    foreach ($document in @('LICENSE', 'NOTICE', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $root $document) -Destination $OutputDirectory -Force
    }
}
Write-Host 'Build completed.'
