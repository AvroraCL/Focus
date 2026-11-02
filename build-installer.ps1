param([string]$CompilerPath, [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $root 'artifacts/tools/InnoSetup/ISCC.exe'),
        "$env:LOCALAPPDATA/Programs/Inno Setup 7/ISCC.exe",
        "$env:ProgramFiles/Inno Setup 7/ISCC.exe"
    )
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else { $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 7, or provide -CompilerPath <path to ISCC.exe>.'
}
if (-not $SkipPublish) {
    & (Join-Path $root 'build.ps1') -SelfContained -NoZip -PublishDirectory 'artifacts/installer-payload'
}
$payload = Join-Path $root 'artifacts/installer-payload/app'
foreach ($file in @('Focus.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'overlay/FocusOverlay.exe', 'overlay/coreclr.dll', 'updater/FocusUpdater.exe', 'updater/coreclr.dll', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Incomplete self-contained payload: $file" }
}
# Keep the bundled Microsoft runtime license and upstream notices in the installer.
$runtime = Get-Content (Join-Path $payload 'Focus.runtimeconfig.json') -Raw | ConvertFrom-Json
$assets = Get-Content (Join-Path $root 'src/Focus.App/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packageDir = $assets.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ "$package/$($framework.version)" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $packageDir) { throw "Runtime license source missing: $package" }
    $noticeDir = Join-Path $payload "licenses/$($framework.name)"
    New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
    $licenseFiles = Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' }
    if (-not $licenseFiles) { throw "Missing runtime license: $package" }
    $licenseFiles | Copy-Item -Destination $noticeDir -Force
}
$owned = @(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
    [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
}) + 'focus-installed-files.txt'
[IO.File]::WriteAllLines((Join-Path $payload 'focus-installed-files.txt'), [string[]]($owned | Sort-Object -Unique), [Text.UTF8Encoding]::new($true))
& $CompilerPath /Qp (Join-Path $root 'installer/Focus.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$version = ((Get-Item (Join-Path $payload 'Focus.exe')).VersionInfo.FileVersion -split '\.')[0..2] -join '.'
$installer = Join-Path $root "artifacts/installer/Focus-Setup-$version-win-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installer + '.sha256', "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Installer: $installer"
Write-Host "SHA-256: $hash"
