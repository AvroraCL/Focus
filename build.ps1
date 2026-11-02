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
$updaterOut = Join-Path $publishRoot "updater"

Write-Host "== Focus release build ==" -ForegroundColor Cyan
Write-Host "runtime=$Runtime selfContained=$SelfContained"

$resolvedPublish = [System.IO.Path]::GetFullPath($publishRoot)
$expectedPublish = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/publish'))
if ($resolvedPublish -ne $expectedPublish -or -not $resolvedPublish.StartsWith([System.IO.Path]::GetFullPath($root) + [System.IO.Path]::DirectorySeparatorChar)) {
    throw 'Unsafe publish output path'
}
if (Test-Path -LiteralPath $resolvedPublish) {
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}

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

Write-Host "-- publishing updater"
& $dotnet publish "src/Focus.Updater/Focus.Updater.csproj" @common -o $updaterOut
if ($LASTEXITCODE -ne 0) { throw "updater publish failed" }
Copy-Item -LiteralPath $updaterOut -Destination (Join-Path $appOut 'updater') -Recurse

# Overlay ships beside the app (resolver prefers the overlay/ subfolder).
$appOverlay = Join-Path $appOut "overlay"
if (-not [System.IO.Path]::GetFullPath($appOverlay).StartsWith($resolvedPublish + [System.IO.Path]::DirectorySeparatorChar)) { throw 'Unsafe overlay output path' }
Remove-Item -LiteralPath $appOverlay -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $overlayOut $appOverlay -Recurse
Remove-Item (Join-Path $appOut "FocusOverlay.exe") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $appOut "FocusOverlay.dll") -Force -ErrorAction SilentlyContinue

$version = ((Get-Item (Join-Path $appOut "Focus.exe")).VersionInfo.FileVersion -split '\.')[0..2] -join '.'
Write-Host "version=$version"

if (-not $NoZip) {
    $zip = Join-Path $publishRoot "Focus-v$version-$Runtime.zip"
    Write-Host "-- creating $zip"
    Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($zip + '.sha256', "$hash  $([System.IO.Path]::GetFileName($zip))`n", [System.Text.UTF8Encoding]::new($false))
}

Write-Host "done: $appOut" -ForegroundColor Green
