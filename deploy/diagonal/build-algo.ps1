# Build the DiagonalSignalerBot .algo with the official cTrader CLI image,
# then stage it into ./algo/ ready for deployment.
#
# Requires: Docker (Desktop or engine). No local .NET SDK / cTrader desktop needed.
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$algoDir  = Join-Path $PSScriptRoot "algo"
New-Item -ItemType Directory -Force -Path $algoDir | Out-Null

Write-Host "Building DiagonalSignalerBot.algo from $repoRoot ..."

# The CLI image has no git, so the HEAD stamp is taken here and handed over as env vars
# (MSBuild picks them up as properties, see the EmbedGitInfo target in TradeKit.Core).
$commit = ""
$commitDate = ""
try {
    $commit = (git -C $repoRoot rev-parse HEAD | Out-String).Trim()
    $commitDate = (git -C $repoRoot show -s --format=%cI HEAD | Out-String).Trim()
} catch {
    Write-Warning "git is unavailable, the .algo will report an unknown revision."
}

docker run --rm --mount "type=bind,src=$repoRoot,dst=/src" `
    -e "TradeKitGitCommit=$commit" -e "TradeKitGitCommitDate=$commitDate" `
    ghcr.io/spotware/ctrader-console:latest build /src/DiagonalSignalerBot

$src = Join-Path $repoRoot "DiagonalSignalerBot\bin\Release\net6.0\src.algo"
if (-not (Test-Path $src)) {
    throw "Build output not found: $src"
}

$dst = Join-Path $algoDir "DiagonalSignalerBot.algo"
Copy-Item -Force $src $dst
Write-Host "Staged: $dst"
