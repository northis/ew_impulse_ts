<#
.SYNOPSIS
Checks parameters.cbotset against the parameters compiled into the .algo.

.DESCRIPTION
cTrader CLI rejects a .cbotset that names a parameter the cBot does not have:

    Error: Unknown cBot parameters found: <Name>

The reverse case is silent: a parameter missing from the file just falls back to
its compiled default. Both happen after the cBot code changes (a parameter is
renamed/removed, or a new filter is added) while the .cbotset stays as it was.

This script reads the parameter metadata produced by `build-algo.ps1`
(<name>.algo.metadata) and reports:

  * unknown  -- keys in the file that the cBot does not declare (runtime error);
  * missing  -- parameters the file does not set (compiled default is used);
  * invalid  -- values outside [MinValue..MaxValue] or not a bool.

With -Fix the file is rewritten: unknown keys are dropped, missing ones are
added with their compiled defaults. Values that are already set are kept as-is
(including their string form), so the tuned instance survives the update.

.PARAMETER ParamsFile
.cbotset to check. Defaults to parameters.cbotset next to this script.

.PARAMETER MetadataFile
Parameter metadata. Defaults to ./algo/DiagonalSignalerBot.algo.metadata and,
if that is absent, to the build output src.algo.metadata.

.PARAMETER Fix
Rewrite ParamsFile instead of failing on drift.

.EXAMPLE
powershell -File .\sync-params.ps1
powershell -File .\sync-params.ps1 -Fix

.EXAMPLE
# after a code change: rebuild, then actualize the instance parameters
powershell -File .\build-algo.ps1
powershell -File .\sync-params.ps1 -Fix
#>
[CmdletBinding()]
param(
    [string] $ParamsFile,
    [string] $MetadataFile,
    [switch] $Fix
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not $ParamsFile) { $ParamsFile = Join-Path $PSScriptRoot 'parameters.cbotset' }

function Resolve-MetadataPath {
    param([string] $Path)

    if ($Path) {
        if (-not (Test-Path $Path)) { throw "Metadata file not found: $Path" }
        return (Resolve-Path $Path).Path
    }

    $candidates = @(
        (Join-Path $PSScriptRoot 'algo\DiagonalSignalerBot.algo.metadata'),
        (Join-Path $repoRoot 'DiagonalSignalerBot\bin\Release\net6.0\src.algo.metadata')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
    }

    throw ("Parameter metadata not found. Build the .algo first " +
        "(powershell -File .\build-algo.ps1) or pass -MetadataFile.")
}

# .cbotset values are strings, and the CLI is culture-invariant about them
# ("0.5", never "0,5"), so every number is formatted explicitly.
function Format-DefaultValue {
    param($Parameter, $Fallback)

    $type = $Parameter.ParameterType
    $value = $Parameter.DefaultValue

    if ($type -eq 'Boolean') {
        if ($value -eq $true) { return 'True' }
        return 'False'
    }

    # Enum metadata only carries the numeric default, while the .cbotset needs
    # the member name (e.g. "NONE"), so reuse the template's value.
    if ($type -eq 'Enum') {
        if ($Fallback -and $Fallback.ContainsKey($Parameter.PropertyName)) {
            return [string] $Fallback[$Parameter.PropertyName]
        }
        throw ("Cannot derive a value for enum parameter '$($Parameter.PropertyName)': " +
            "add it to parameters.cbotset manually.")
    }

    if ($null -eq $value -or "$value" -eq '') { return '' }

    return [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}', $value)
}

function Test-Numeric {
    param([string] $Value, [double] $Min, [double] $Max)

    $number = 0.0
    $parsed = [double]::TryParse(
        $Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref] $number)

    if (-not $parsed) { return "not a number: '$Value'" }
    if ($null -ne $Min -and $number -lt $Min) { return "$Value < MinValue $Min" }
    if ($null -ne $Max -and $number -gt $Max) { return "$Value > MaxValue $Max" }
    return $null
}

$metadataPath = Resolve-MetadataPath $MetadataFile
$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
$declared = @{}
foreach ($parameter in $metadata.Parameters) { $declared[$parameter.PropertyName] = $parameter }

if (-not (Test-Path $ParamsFile)) {
    throw "Parameters file not found: $ParamsFile (copy parameters.cbotset.example first)."
}
$paramsPath = (Resolve-Path $ParamsFile).Path
$document = Get-Content $paramsPath -Raw | ConvertFrom-Json
if (-not $document.Parameters) { throw "No 'Parameters' object in $paramsPath" }

# Template used only for enum fallbacks; the file itself wins.
$templatePath = Join-Path $PSScriptRoot 'parameters.cbotset.example'
$template = @{}
if (Test-Path $templatePath) {
    $templateDocument = Get-Content $templatePath -Raw | ConvertFrom-Json
    if ($templateDocument.Parameters) {
        foreach ($property in $templateDocument.Parameters.PSObject.Properties) {
            $template[$property.Name] = $property.Value
        }
    }
}

$actual = @{}
$actualNames = @($document.Parameters.PSObject.Properties.Name)
foreach ($name in $actualNames) { $actual[$name] = $document.Parameters.PSObject.Properties[$name].Value }

$unknown = @($actualNames | Where-Object { -not $declared.ContainsKey($_) } | Sort-Object)
$missing = @($declared.Keys | Where-Object { -not $actual.ContainsKey($_) } | Sort-Object)

$invalid = @()
foreach ($name in $actual.Keys) {
    if (-not $declared.ContainsKey($name)) { continue }

    $parameter = $declared[$name]
    $value = [string] $actual[$name]
    $problem = $null

    switch ($parameter.ParameterType) {
        'Boolean' {
            if ($value -ne 'True' -and $value -ne 'False') { $problem = "expected True/False, got '$value'" }
        }
        'Double' { $problem = Test-Numeric $value $parameter.MinValue $parameter.MaxValue }
        'Integer' {
            $number = 0
            if (-not [int]::TryParse($value, [ref] $number)) { $problem = "not an integer: '$value'" }
            else { $problem = Test-Numeric $value $parameter.MinValue $parameter.MaxValue }
        }
    }

    if ($problem) { $invalid += "{0}: {1}" -f $name, $problem }
}

Write-Host "Parameters file : $paramsPath"
Write-Host "Metadata        : $metadataPath"
Write-Host "cBot parameters : $($declared.Count), set in file: $($actual.Count)"
Write-Host ''

if ($unknown.Count -gt 0) {
    Write-Host 'Unknown (cTrader CLI would fail with "Unknown cBot parameters found"):'
    $unknown | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ''
}
if ($missing.Count -gt 0) {
    Write-Host 'Missing (the compiled default is used):'
    $missing | ForEach-Object { Write-Host "  - $_ = $((Format-DefaultValue $declared[$_] $template))" -ForegroundColor Yellow }
    Write-Host ''
}
if ($invalid.Count -gt 0) {
    Write-Host 'Invalid (the CLI would reject these values):'
    $invalid | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ''
}

if ($unknown.Count -eq 0 -and $missing.Count -eq 0 -and $invalid.Count -eq 0) {
    Write-Host 'OK: parameters.cbotset matches the cBot.' -ForegroundColor Green
    exit 0
}

if (-not $Fix) {
    Write-Host 'Run with -Fix to actualize the file, or fix the values above by hand.' -ForegroundColor Yellow
    exit 1
}

if ($invalid.Count -gt 0) {
    throw 'Invalid values are not fixed automatically: choose values within the declared range first.'
}

# Keep the existing order for known keys, then append the new ones with their
# compiled defaults -- so the diff after an update is just the added lines.
$result = [ordered] @{}
foreach ($name in $actualNames) {
    if ($declared.ContainsKey($name)) { $result[$name] = $actual[$name] }
}
foreach ($name in $missing) {
    $result[$name] = Format-DefaultValue $declared[$name] $template
}

$output = [ordered] @{ Parameters = $result }

# Hand-rolled writer: ConvertTo-Json in Windows PowerShell aligns values under the
# key column, which makes the file unreadable and every -Fix diff huge.
function ConvertTo-JsonString {
    param([string] $Value)
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

$lines = @('{', '  "Parameters": {')
$index = 0
foreach ($name in $result.Keys) {
    $index++
    $comma = if ($index -lt $result.Count) { ',' } else { '' }
    $lines += '    "{0}": "{1}"{2}' -f $name, (ConvertTo-JsonString ([string] $result[$name])), $comma
}
$lines += '  }'
$lines += '}'
$json = $lines -join "`r`n"

# UTF-8 without BOM: the JSON parser on the CLI side is happier without it.
[System.IO.File]::WriteAllText($paramsPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Updated: $paramsPath (dropped $($unknown.Count), added $($missing.Count))." -ForegroundColor Green
exit 0
