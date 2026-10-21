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
    $commit = (git -C $repoRoot rev-parse --short HEAD | Out-String).Trim()
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

# The parameter metadata is what sync-params.ps1 checks parameters.cbotset against
# (unknown keys make the CLI abort with "Unknown cBot parameters found"). Keep it
# next to the .algo so the check works without local build output.
$srcMeta = Join-Path $repoRoot "DiagonalSignalerBot\bin\Release\net6.0\src.algo.metadata"
if (Test-Path $srcMeta) {
    $dstMeta = Join-Path $algoDir "DiagonalSignalerBot.algo.metadata"
    Copy-Item -Force $srcMeta $dstMeta
    Write-Host "Staged: $dstMeta"
} else {
    Write-Warning "Parameter metadata not found: $srcMeta"
}

# Fail-fast check: the .cbotset is a bind mount, so a stale key only shows up when
# the container starts. Catch it here instead.
$params = Join-Path $PSScriptRoot "parameters.cbotset"
if (Test-Path $params) {
    Write-Host ""
    & (Join-Path $PSScriptRoot "sync-params.ps1") -ParamsFile $params
    if ($LASTEXITCODE -ne 0) {
        Write-Warning ("parameters.cbotset does not match the built cBot. " +
            "Run: powershell -File .\sync-params.ps1 -Fix")
    }
} else {
    Write-Host "No parameters.cbotset yet - copy parameters.cbotset.example (step 3.3)."
}
