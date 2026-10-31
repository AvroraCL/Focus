param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# Prefer a dotnet that actually has an SDK (the x86 host on PATH may have none).
$dotnet = "dotnet"
$sdks = & $dotnet --list-sdks 2>$null
if (-not $sdks) {
    $candidate = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $candidate) { $dotnet = $candidate }
}
Write-Host "dotnet: $dotnet"

$publishRoot = Join-Path $root "artifacts/publish"
$appOut = Join-Path $publishRoot "app"
$overlayOut = Join-Path $publishRoot "overlay"

Write-Host "== Focus release build ==" -ForegroundColor Cyan
Write-Host "runtime=$Runtime selfContained=$SelfContained"

Remove-Item $publishRoot -Recurse -Force -ErrorAction SilentlyContinue

$common = @(
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", ($SelfContained.IsPresent.ToString().ToLowerInvariant()),
    "-p:PublishSingleFile=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "--nologo"
)

Write-Host "-- publishing overlay"
& $dotnet publish "src/Focus.Overlay/Focus.Overlay.csproj" @common -o $overlayOut
if ($LASTEXITCODE -ne 0) { throw "overlay publish failed" }

Write-Host "-- publishing app"
& $dotnet publish "src/Focus.App/Focus.App.csproj" @common -o $appOut
if ($LASTEXITCODE -ne 0) { throw "app publish failed" }

# Overlay ships beside the app (resolver prefers the overlay/ subfolder).
$appOverlay = Join-Path $appOut "overlay"
Remove-Item $appOverlay -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $overlayOut $appOverlay -Recurse
Remove-Item (Join-Path $appOut "FocusOverlay.exe") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $appOut "FocusOverlay.dll") -Force -ErrorAction SilentlyContinue

$version = ((Get-Item (Join-Path $appOut "Focus.exe")).VersionInfo.FileVersion -split '\.')[0..2] -join '.'
Write-Host "version=$version"

if (-not $NoZip) {
    $zip = Join-Path $publishRoot "Focus-v$version-$Runtime.zip"
    Write-Host "-- creating $zip"
    Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $zip -Force
}

Write-Host "done: $appOut" -ForegroundColor Green
