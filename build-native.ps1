param([switch]$Test, [switch]$Lint)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
# GNU ld on Windows may reject Unicode object paths. Keep its intermediate paths ASCII.
$hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($projectRoot))).Substring(0,16)
$nativeTarget = Join-Path $env:TEMP "focus-native-$hash"
$manifest = Join-Path $projectRoot 'Cargo.toml'
$target = 'x86_64-pc-windows-gnu'
if ($Lint) {
    & cargo clippy --locked --workspace --all-targets --target $target --target-dir $nativeTarget --manifest-path $manifest -- -D warnings
} elseif ($Test) {
    & cargo test --locked --workspace --target $target --target-dir $nativeTarget --manifest-path $manifest
} else {
    & cargo build --locked --release --target $target --target-dir $nativeTarget --manifest-path $manifest
}
if ($LASTEXITCODE -ne 0) { throw 'Rust core build/check failed.' }
if (-not $Test -and -not $Lint) {
    $destination = Join-Path $projectRoot 'artifacts/native'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $nativeTarget "$target/release/focus_native.dll") -Destination $destination -Force
}
