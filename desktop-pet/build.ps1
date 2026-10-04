param([string]$OutputPath = (Join-Path $PSScriptRoot 'WhaleOverlay.exe'))
$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$assemblyDir = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Join-Path $frameworkDir 'System.Xml.dll'),
    (Join-Path $frameworkDir 'System.Windows.Forms.dll'),
    (Join-Path $frameworkDir 'System.Drawing.dll'),
    (Join-Path $assemblyDir 'GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'),
    (Join-Path $assemblyDir 'GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'),
    (Join-Path $assemblyDir 'GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll'),
    (Join-Path $assemblyDir 'GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll')
)
$compilerArgs = @('/nologo', '/noconfig', '/target:winexe', "/out:$OutputPath")
$compilerArgs += $references | ForEach-Object { "/r:$_" }
& (Join-Path $frameworkDir 'csc.exe') @compilerArgs (Join-Path $PSScriptRoot 'WhaleOverlay.cs')
if ($LASTEXITCODE -ne 0) { throw 'WhaleOverlay compilation failed.' }
