$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$testExe = Join-Path ([System.IO.Path]::GetTempPath()) ('QuickCaptureTests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /codepage:65001 /main:QuickCapture.Tests /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$testExe" (Join-Path $PSScriptRoot 'ScreenshotTool.cs') (Join-Path $PSScriptRoot 'Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
}
