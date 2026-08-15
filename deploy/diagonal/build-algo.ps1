# Build the DiagonalSignalerBot .algo with the official cTrader CLI image,
# then stage it into ./algo/ ready for deployment.
#
# Requires: Docker (Desktop or engine). No local .NET SDK / cTrader desktop needed.
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$algoDir  = Join-Path $PSScriptRoot "algo"
New-Item -ItemType Directory -Force -Path $algoDir | Out-Null

Write-Host "Building DiagonalSignalerBot.algo from $repoRoot ..."
docker run --rm --mount "type=bind,src=$repoRoot,dst=/src" `
    ghcr.io/spotware/ctrader-console:latest build /src/DiagonalSignalerBot

$src = Join-Path $repoRoot "DiagonalSignalerBot\bin\Release\net6.0\src.algo"
if (-not (Test-Path $src)) {
    throw "Build output not found: $src"
}

$dst = Join-Path $algoDir "DiagonalSignalerBot.algo"
Copy-Item -Force $src $dst
Write-Host "Staged: $dst"
