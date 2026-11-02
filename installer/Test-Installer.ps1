param([string]$CompilerPath = "$PSScriptRoot/../artifacts/tools/InnoSetup/ISCC.exe")
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$token = [guid]::NewGuid().ToString('N')
$testId = '{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}'
$testName = "Focus-InstallerCheck-$token"
$testRoot = Join-Path $repo "artifacts/installer-check/$token"
$installDir = Join-Path $testRoot 'app'
$dataDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) $testName
$groupDir = Join-Path ([Environment]::GetFolderPath('Programs')) $testName
$registryPath = "HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/${testId}_is1"
if ((Test-Path $dataDir) -or (Test-Path $registryPath)) { throw 'Test identity already exists' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
& $CompilerPath /Qp /DInstallerTestBuild "/DInstallerAppId=$testId" "/DDataFolder=$testName" "/DGroupName=$testName" "/O$testRoot" /FInstallerCheck (Join-Path $PSScriptRoot 'Focus.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed' }
$setup = Join-Path $testRoot 'InstallerCheck.exe'
function Run-Checked([string]$Exe, [string[]]$Arguments, [switch]$ExpectFailure) {
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(45000)) { throw "Process timed out: $Exe (PID $($p.Id))" }
    if ($ExpectFailure) {
        if ($p.ExitCode -eq 0) { throw 'Expected running-app protection to reject operation' }
    } elseif ($p.ExitCode -ne 0) { throw "Process failed ($($p.ExitCode)): $Exe" }
}
function Install-Test([string]$LogName) {
    Run-Checked $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=chinesesimp', '/TASKS=', "/DIR=`"$installDir`"", "/LOG=`"$testRoot/$LogName.log`"")
    foreach ($file in @('Focus.exe','coreclr.dll','overlay/FocusOverlay.exe','updater/FocusUpdater.exe','unins000.exe')) {
        if (-not (Test-Path (Join-Path $installDir $file))) { throw "Missing installed file: $file" }
    }
    $registration = Get-ItemProperty $registryPath
    if ($registration.DisplayVersion -ne '0.0.3') { throw 'Wrong uninstall registration version' }
    if (@(Get-ChildItem -LiteralPath $groupDir -Filter '*.lnk').Count -ne 2) { throw 'Missing app/uninstall shortcuts' }
}
function Uninstall-Test([switch]$Purge, [string]$LogName, [switch]$KeepUnrelated) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testRoot/$LogName.log`"")
    if ($Purge) { $arguments += '/PURGEDATA=1' }
    Run-Checked (Join-Path $installDir 'unins000.exe') $arguments
    # Inno launches a temporary child. The original exe can exit before the child
    # completes post-uninstall data cleanup; wait for the child's closed log.
    $log = Join-Path $testRoot "$LogName.log"
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        if ((Test-Path $log) -and (Select-String -LiteralPath $log -SimpleMatch 'Log closed.' -Quiet)) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Select-String -LiteralPath $log -SimpleMatch 'Uninstallation process succeeded.' -Quiet)) { throw 'Uninstall child did not finish successfully' }
    # The uninstaller's temporary copy removes the original uninstaller last.
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $filesLeft = @(Get-ChildItem -LiteralPath $installDir -Recurse -File -ErrorAction SilentlyContinue)
        $done = if ($KeepUnrelated) { $filesLeft.Count -eq 1 -and $filesLeft[0].Name -eq 'unrelated.txt' } else { -not (Test-Path $installDir) }
        if ($done) { break }
        Start-Sleep -Milliseconds 100
    }
    if ((Test-Path $registryPath) -or (Test-Path $groupDir) -or ((Test-Path $installDir) -and -not $KeepUnrelated)) {
        throw 'Installed files, shortcuts or uninstall registry entry remained'
    }
    if ($KeepUnrelated) {
        $remaining = @(Get-ChildItem -LiteralPath $installDir -Recurse -File)
        if ($remaining.Count -ne 1 -or $remaining[0].Name -ne 'unrelated.txt') { throw 'Owned update files remained, or unrelated file was removed' }
        # Remove only the fixture created by this test; never recursively delete an install path.
        Remove-Item -LiteralPath (Join-Path $installDir 'unrelated.txt')
        [IO.Directory]::Delete($installDir)
    }
}
Install-Test 'install'
New-Item -ItemType Directory -Path "$dataDir/sessions", "$dataDir/updates/job" -Force | Out-Null
Set-Content -LiteralPath "$dataDir/settings.json" -Value '{"fixture":true}'
Set-Content -LiteralPath "$dataDir/sessions/test.fss" -Value 'keep-session'
Set-Content -LiteralPath "$dataDir/updates/job/package.zip" -Value 'cache'
(Get-Item -LiteralPath "$dataDir/settings.json").IsReadOnly = $true

# Repair/reinstall must restore installed files and preserve personal data.
New-Item -ItemType Directory -Path "$installDir/new-component" | Out-Null
Set-Content -LiteralPath "$installDir/new-component/from-update.dll" -Value 'updated-owned-file'
Set-Content -LiteralPath "$installDir/unrelated.txt" -Value 'unrelated-user-file'
Add-Content -LiteralPath "$installDir/focus-installed-files.txt" -Value 'new-component/from-update.dll'
Set-Content -LiteralPath "$testRoot/outside.txt" -Value 'outside-install'
Add-Content -LiteralPath "$installDir/focus-installed-files.txt" -Value '../outside.txt'
Set-Content -LiteralPath "$installDir/Focus.Core.dll" -Value 'damaged'
Install-Test 'repair'
$expected = (Get-FileHash "$repo/artifacts/installer-payload/app/Focus.Core.dll").Hash
if ((Get-FileHash "$installDir/Focus.Core.dll").Hash -ne $expected) { throw 'Repair did not replace damaged file' }
if ((Get-Content "$dataDir/sessions/test.fss") -ne 'keep-session') { throw 'Repair changed user data' }

# Start the installed self-contained app with an isolated instance/data root.
$oldData = $env:FOCUS_DATA_ROOT; $oldInstance = $env:FOCUS_INSTANCE
$oldTrace = $env:COREHOST_TRACE; $oldTraceFile = $env:COREHOST_TRACEFILE
try {
    $env:FOCUS_DATA_ROOT = "$testRoot/smoke-data"
    $env:FOCUS_INSTANCE = $testName
    $env:COREHOST_TRACE = '1'; $env:COREHOST_TRACEFILE = "$testRoot/host-trace.log"
    Run-Checked "$installDir/Focus.exe" @('--page','dashboard','--screenshot',"`"$testRoot/dashboard.png`"",'--screenshot-delay','1000')
    if (-not (Test-Path "$testRoot/dashboard.png")) { throw 'Installed application failed to render' }
    $config = Get-Content "$installDir/Focus.runtimeconfig.json" -Raw | ConvertFrom-Json
    if (-not $config.runtimeOptions.includedFrameworks -or $config.runtimeOptions.frameworks) { throw 'Application is not self-contained' }
} finally {
    $env:FOCUS_DATA_ROOT = $oldData; $env:FOCUS_INSTANCE = $oldInstance
    $env:COREHOST_TRACE = $oldTrace; $env:COREHOST_TRACEFILE = $oldTraceFile
}

# Active recordings must never be killed by setup or uninstall.
$mutex = [Threading.Mutex]::new($false, 'Focus.App.SingleInstance.v1')
try {
    Run-Checked $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$installDir`"") -ExpectFailure
    Run-Checked "$installDir/unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/PURGEDATA=1') -ExpectFailure
    if (-not (Test-Path "$dataDir/settings.json")) { throw 'Rejected uninstall deleted data' }
} finally { $mutex.Dispose() }

Uninstall-Test -LogName 'uninstall-keep' -KeepUnrelated
if ((Get-Content "$testRoot/outside.txt") -ne 'outside-install') { throw 'Ownership cleanup escaped installation' }
if (-not (Test-Path "$dataDir/sessions/test.fss")) { throw 'Default uninstall deleted user data' }
Install-Test 'reinstall'

# A junction inside Focus data must not delete unrelated data at its target.
$outside = Join-Path $testRoot 'unrelated-data'
New-Item -ItemType Directory -Path $outside | Out-Null
Set-Content -LiteralPath "$outside/keep.txt" -Value 'unrelated'
New-Item -ItemType Junction -Path "$dataDir/external" -Target $outside | Out-Null
Uninstall-Test -Purge -LogName 'uninstall-purge'
if (Test-Path $dataDir) { throw 'Full uninstall left personal data' }
if ((Get-Content "$outside/keep.txt") -ne 'unrelated') { throw 'Full uninstall followed a junction' }

# Also guard the data directory itself when it is a junction.
Install-Test 'root-link-install'
New-Item -ItemType Junction -Path $dataDir -Target $outside | Out-Null
Uninstall-Test -Purge -LogName 'root-link-uninstall'
if (Test-Path $dataDir) { throw 'Root data link was not removed' }
if ((Get-Content "$outside/keep.txt") -ne 'unrelated') { throw 'Root junction target was modified' }
Write-Output "PASS: install, repair, self-contained launch, active-app protection, keep/purge uninstall, shortcuts/registry removal, update ownership, unrelated-file preservation, path traversal rejection, read-only files and junction safety. Logs: $testRoot"
