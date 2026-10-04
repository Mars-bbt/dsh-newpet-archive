param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\desktop-pet\WhaleOverlay.exe'),
    [switch]$CloseCycle,
    [switch]$PluginHostEnvironment
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $env:TEMP 'DshNewpet.FocusRegression.exe'
& $compiler /nologo /target:exe "/out:$testExe" (Join-Path $PSScriptRoot 'FocusDshRegression.cs')
if ($LASTEXITCODE -ne 0) { throw 'Regression test compilation failed.' }
$testArgs = @([System.IO.Path]::GetFullPath($AssemblyPath))
if ($CloseCycle) { $testArgs += '--close-cycle' }
$previousRunMode = $env:ELECTRON_RUN_AS_NODE
try {
    if ($PluginHostEnvironment) { $env:ELECTRON_RUN_AS_NODE = '1' }
    & $testExe @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Desktop focus regression failed.' }
} finally {
    $env:ELECTRON_RUN_AS_NODE = $previousRunMode
}
