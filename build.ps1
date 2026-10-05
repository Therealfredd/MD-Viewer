<#
.SYNOPSIS
  Builds MD Viewer and its Windows installer.

.EXAMPLE
  .\build.ps1                 # framework-dependent (~2 MB installer, needs .NET Desktop Runtime 8+)
  .\build.ps1 -SelfContained  # bundles the .NET runtime (larger, no prerequisites besides WebView2)
  .\build.ps1 -NoInstaller    # publish only
#>
param(
    [switch]$SelfContained,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish (Join-Path $root 'src\MdViewer\MdViewer.csproj') -c Release -r win-x64 `
    --self-contained $sc -p:PublishReadyToRun=true -p:DebugType=none -p:GenerateDocumentationFile=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Drop files the app does not need at runtime.
Get-ChildItem $publish -Filter *.xml | Remove-Item -Force
if (Test-Path (Join-Path $publish 'runtimes')) { Remove-Item -Recurse -Force (Join-Path $publish 'runtimes') }

Write-Host "Published to $publish"
if ($NoInstaller) { return }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or use -NoInstaller." }

$defines = @("/DPublishDir=$publish")
if ($SelfContained) { $defines += '/DSelfContained' }
& $iscc @defines (Join-Path $root 'installer\MdViewer.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Write-Host "Installer written to $(Join-Path $root 'dist')"
