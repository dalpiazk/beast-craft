<#
.SYNOPSIS
    Records where a shipped audio file came from (docs/design/audio.md, "Making the files").

.DESCRIPTION
    Writes Pipeline/audio-generation/provenance/<file name>.json for one file under content/audio/: the
    source (a master's path, a generation job id or a URL), the licence, the tool and its tier, the
    prompt (for a generated sound), the date, who approved it, and the file's SHA-256, so the file can be
    matched to its record later. Run it after Normalize-Audio.ps1, once the file is approved by ear.

    The licensing rules (Pipeline/README.md) are enforced: Google Lyria output never ships (it is for
    prototyping only), and Stable Audio output ships only from the Creator tier. Re-read the Stable Audio
    Creator terms before generating shipped audio. Nothing here calls a generation API.

.EXAMPLE
    .\Write-AudioProvenance.ps1 -File ..\..\content\audio\sfx\sfx_hit_fire_oneshot.wav -Source "stable-audio job 8f2c" `
        -Licence "Stable Audio Creator tier commercial terms" -Tool "Stable Audio" -Tier "Creator" -ApprovedBy "producer" `
        -Prompt "short warm fire whoosh impact, no reverb tail"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $File,

    [Parameter(Mandatory = $true)]
    [string] $Source,

    [Parameter(Mandatory = $true)]
    [string] $Licence,

    [Parameter(Mandatory = $true)]
    [string] $Tool,

    [string] $Tier = '',

    [string] $Prompt = '',

    [string] $ApprovedBy = '',

    [string] $Notes = '',

    [string] $Date = (Get-Date -Format 'yyyy-MM-dd'),

    [string] $OutDir = ''
)

$ErrorActionPreference = 'Stop'
if (-not $OutDir) {
    # Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
    $OutDir = Join-Path $PSScriptRoot 'provenance'
}

if (-not (Test-Path $File -PathType Leaf)) {
    throw "No file $File."
}

if ($Tool -match 'lyria') {
    throw "Google Lyria output is prototype-only and never ships (Pipeline/README.md). Regenerate it with Stable Audio (Creator tier)."
}

if ($Tool -match 'stable\s*audio' -and $Tier -notmatch '^creator$') {
    throw "Stable Audio output ships only from the Creator tier (its commercial terms); this says tier '$Tier'."
}

if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
    throw "The date '$Date' is not yyyy-MM-dd."
}

$item = Get-Item $File
$name = $item.Name
if ($name -notmatch '^(sfx_[a-z0-9_]+_oneshot\.wav|mus_[a-z0-9_]+_loop\.ogg|amb_[a-z0-9_]+_loop\.ogg)$') {
    throw "$name does not follow the naming contract (docs/design/audio.md)."
}

$hash = (Get-FileHash -Path $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$record = [ordered]@{
    File       = $name
    Sha256     = $hash
    Bytes      = $item.Length
    Source     = $Source
    Licence    = $Licence
    Tool       = $Tool
    Tier       = $Tier
    Prompt     = $Prompt
    Date       = $Date
    ApprovedBy = $ApprovedBy
    Notes      = $Notes
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir ($name + '.json')
$json = ($record | ConvertTo-Json -Depth 3) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($out, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $out"
