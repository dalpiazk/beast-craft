<#
.SYNOPSIS
    Turns approved audio masters into Beast Craft's shipped files (docs/design/audio.md).

.DESCRIPTION
    Reads 48 kHz WAV masters named by the pipeline's contract (Pipeline/README.md, "Audio") and writes the
    shipped files under content/audio/:

      mus_<name>_loop.wav      -> content/audio/music/mus_<name>_loop.ogg     (OGG Vorbis)
      amb_<name>_loop.wav      -> content/audio/ambient/amb_<name>_loop.ogg   (OGG Vorbis)
      sfx_<name>_oneshot.wav   -> content/audio/sfx/sfx_<name>_oneshot.wav    (16-bit mono WAV)

    Music and ambience are loudness normalised to -16 LUFS integrated (true peak -1.5 dBTP) with
    ffmpeg's loudnorm in two passes: measure, then apply the measured values with linear=true.
    A layered track's stems (mus_<track>_<stem>_loop.wav with the stems base, pulse, lead, peak) are
    measured as their full mix and all get that one gain, so the layers keep their balance.
    Sound effects are peak normalised to -1 dBFS and written as 16-bit mono 48 kHz WAV.

    Build-time only: nothing here ships or runs in the game, and nothing calls a generation API. Needs
    ffmpeg (with libvorbis) on the PATH. Review every output by ear before committing it, and record its
    provenance (Write-AudioProvenance.ps1). Check the result with Test-AudioLoudness.ps1.

.PARAMETER InputDir
    The folder of approved masters.

.PARAMETER ContentRoot
    The game's content folder (default: the repository's content/).

.PARAMETER Quality
    The Vorbis quality for music and ambience, 4 or 5 (default 4).

.EXAMPLE
    .\Normalize-Audio.ps1 -InputDir C:\audio\approved
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputDir,

    [string] $ContentRoot = '',

    [ValidateRange(4, 5)]
    [int] $Quality = 4
)

$ErrorActionPreference = 'Stop'
if (-not $ContentRoot) {
    # Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
    $ContentRoot = Join-Path $PSScriptRoot '..\..\content'
}

$TargetLufs = -16.0
$TargetTruePeak = -1.5
$TargetLra = 11.0
$SfxPeakDb = -1.0
$StemOrder = @('base', 'pulse', 'lead', 'peak')

function Assert-Ffmpeg {
    if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
        throw "ffmpeg is not on the PATH. Install it (with libvorbis) and run again."
    }
}

# Runs ffmpeg and returns its stderr (where it reports measurements), throwing on failure.
function Invoke-Ffmpeg([string[]] $Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'ffmpeg'
    $info.Arguments = ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $info.RedirectStandardError = $true
    $info.RedirectStandardOutput = $true
    $info.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($info)
    $stderr = $process.StandardError.ReadToEnd()
    [void] $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "ffmpeg failed ($($process.ExitCode)): $($info.Arguments)`n$stderr"
    }
    return $stderr
}

# The loudnorm first pass: the measured values, from the JSON block ffmpeg prints last.
function Measure-Loudness([string[]] $InputArguments, [string] $Filter) {
    $chain = $Filter
    if ($chain) { $chain = $chain + ',' }
    $chain = $chain + "loudnorm=I=$($TargetLufs):TP=$($TargetTruePeak):LRA=$($TargetLra):print_format=json"
    $stderr = Invoke-Ffmpeg (@('-hide_banner', '-nostats') + $InputArguments + @('-filter_complex', $chain, '-f', 'null', '-'))
    $start = $stderr.LastIndexOf('{')
    $end = $stderr.LastIndexOf('}')
    if ($start -lt 0 -or $end -lt $start) {
        throw "loudnorm printed no measurement."
    }
    return $stderr.Substring($start, $end - $start + 1) | ConvertFrom-Json
}

function Get-OutputPath([string] $Name, [string] $Folder, [string] $Extension) {
    $dir = Join-Path $ContentRoot ("audio\" + $Folder)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return Join-Path $dir ([System.IO.Path]::GetFileNameWithoutExtension($Name) + $Extension)
}

# One music or ambience file: two-pass loudnorm to OGG Vorbis.
function Convert-Loop([System.IO.FileInfo] $Master, [string] $Folder) {
    $measured = Measure-Loudness @('-i', $Master.FullName) ''
    $second = "loudnorm=I=$($TargetLufs):TP=$($TargetTruePeak):LRA=$($TargetLra):measured_I=$($measured.input_i):measured_TP=$($measured.input_tp):" +
              "measured_LRA=$($measured.input_lra):measured_thresh=$($measured.input_thresh):offset=$($measured.target_offset):linear=true"
    $out = Get-OutputPath $Master.Name $Folder '.ogg'
    [void] (Invoke-Ffmpeg @('-hide_banner', '-nostats', '-y', '-i', $Master.FullName, '-af', $second, '-ar', '48000', '-c:a', 'libvorbis', '-q:a', "$Quality", $out))
    Write-Host ("  {0} -> {1} (was {2} LUFS)" -f $Master.Name, $out, $measured.input_i)
}

# A layered track's stems: measure the all-stems mix, then give every stem that one gain.
function Convert-Stems([string] $Track, [System.IO.FileInfo[]] $Stems) {
    $inputs = @()
    foreach ($stem in $Stems) { $inputs += @('-i', $stem.FullName) }
    $mix = ''
    for ($i = 0; $i -lt $Stems.Count; $i++) { $mix += "[$($i):a]" }
    $mix += "amix=inputs=$($Stems.Count):normalize=0"
    $measured = Measure-Loudness $inputs $mix
    $gain = $TargetLufs - [double] $measured.input_i
    $peakAfter = [double] $measured.input_tp + $gain
    if ($peakAfter -gt $TargetTruePeak) {
        # Keep the mix's true peak under the ceiling: less gain rather than a limiter on one stem.
        $gain = $gain - ($peakAfter - $TargetTruePeak)
        Write-Warning ("{0}: gain held back {1:N1} dB for the true peak; the mix lands below {2} LUFS." -f $Track, ($peakAfter - $TargetTruePeak), $TargetLufs)
    }

    foreach ($stem in $Stems) {
        $out = Get-OutputPath $stem.Name 'music' '.ogg'
        [void] (Invoke-Ffmpeg @('-hide_banner', '-nostats', '-y', '-i', $stem.FullName, '-af', ("volume={0:N2}dB" -f $gain), '-ar', '48000', '-c:a', 'libvorbis', '-q:a', "$Quality", $out))
        Write-Host ("  {0} -> {1} ({2:+0.0;-0.0} dB, the track's shared gain)" -f $stem.Name, $out, $gain)
    }
}

# One sound effect: peak normalise to -1 dBFS, 16-bit mono 48 kHz WAV.
function Convert-Sfx([System.IO.FileInfo] $Master) {
    $stderr = Invoke-Ffmpeg @('-hide_banner', '-nostats', '-i', $Master.FullName, '-ac', '1', '-af', 'volumedetect', '-f', 'null', '-')
    $match = [regex]::Match($stderr, 'max_volume:\s*(-?[0-9.]+) dB')
    if (-not $match.Success) {
        throw "$($Master.Name): volumedetect printed no peak."
    }
    $gain = $SfxPeakDb - [double] $match.Groups[1].Value
    $out = Get-OutputPath $Master.Name 'sfx' '.wav'
    [void] (Invoke-Ffmpeg @('-hide_banner', '-nostats', '-y', '-i', $Master.FullName, '-ac', '1', '-ar', '48000', '-af', ("volume={0:N2}dB" -f $gain), '-c:a', 'pcm_s16le', $out))
    Write-Host ("  {0} -> {1} ({2:+0.0;-0.0} dB to peak {3} dBFS)" -f $Master.Name, $out, $gain, $SfxPeakDb)
}

Assert-Ffmpeg
if (-not (Test-Path $InputDir)) {
    throw "No folder $InputDir."
}

$masters = Get-ChildItem -Path $InputDir -Filter '*.wav' -File
$stemGroups = @{}
$failed = 0
foreach ($master in $masters) {
    $name = $master.Name
    try {
        if ($name -match '^sfx_[a-z0-9]+(_[a-z0-9]+)*_oneshot\.wav$') {
            Convert-Sfx $master
        }
        elseif ($name -match '^mus_([a-z0-9_]+)_(base|pulse|lead|peak)_loop\.wav$') {
            $track = $Matches[1]
            if (-not $stemGroups.ContainsKey($track)) { $stemGroups[$track] = @() }
            $stemGroups[$track] += $master
        }
        elseif ($name -match '^mus_[a-z0-9]+(_[a-z0-9]+)*_loop\.wav$') {
            Convert-Loop $master 'music'
        }
        elseif ($name -match '^amb_[a-z0-9]+(_[a-z0-9]+)*_loop\.wav$') {
            Convert-Loop $master 'ambient'
        }
        else {
            Write-Warning "$name does not follow the naming contract (Pipeline/README.md); skipped."
        }
    }
    catch {
        Write-Error -ErrorAction Continue "$($name): $($_.Exception.Message)"
        $failed++
    }
}

foreach ($track in $stemGroups.Keys) {
    $stems = $stemGroups[$track] | Sort-Object { [array]::IndexOf($StemOrder, ($_.Name -replace '^mus_.+_(base|pulse|lead|peak)_loop\.wav$', '$1')) }
    try {
        Convert-Stems $track $stems
    }
    catch {
        Write-Error -ErrorAction Continue "mus_$($track): $($_.Exception.Message)"
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Host "$failed file(s) failed."
    exit 1
}

Write-Host "Done. Listen to every file, record its provenance (Write-AudioProvenance.ps1) and run Test-AudioLoudness.ps1."
