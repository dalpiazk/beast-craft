<#
.SYNOPSIS
    Checks every shipped audio file against docs/design/audio.md; usable in CI.

.DESCRIPTION
    Scans content/audio/ and fails (exit code 1) on:
      - a file off the naming contract or in the wrong folder;
      - a file no cue in content/data/Audio/audio-cues.json uses;
      - a sound effect that is not 16-bit PCM mono WAV, is longer than 5 s, or whose peak is not
        -1 dBFS (+/- 0.5 dB);
      - music or ambience whose integrated loudness is not -16 LUFS (+/- 1 LU) or whose true peak is
        above -1 dBTP; a layered track is measured as its full mix (its stems share one gain);
      - a layered track whose stems differ in length (more than 5 ms), sample rate or channels.

    With no audio files it passes at once (none ship yet). Measuring needs ffmpeg on the PATH; without it
    the script checks names, cues and WAV headers, then exits 2 if anything still needed measuring.
    Build-time only: nothing here ships.

.PARAMETER ContentRoot
    The game's content folder (default: the repository's content/).
#>
[CmdletBinding()]
param(
    [string] $ContentRoot = ''
)

$ErrorActionPreference = 'Stop'
if (-not $ContentRoot) {
    # Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
    $ContentRoot = Join-Path $PSScriptRoot '..\..\content'
}

$TargetLufs = -16.0
$LufsTolerance = 1.0
$MaxTruePeak = -1.0
$SfxPeak = -1.0
$SfxPeakTolerance = 0.5
$MaxSfxSeconds = 5.0
$StemLengthTolerance = 0.005

$failures = New-Object System.Collections.Generic.List[string]
function Fail([string] $Message) { $failures.Add($Message) }

$audioRoot = Join-Path $ContentRoot 'audio'
$files = @()
if (Test-Path $audioRoot) {
    $files = @(Get-ChildItem -Path $audioRoot -Recurse -File | Where-Object { $_.Extension -in '.wav', '.ogg' })
}

if ($files.Count -eq 0) {
    Write-Host "No audio files under $audioRoot yet: nothing to check."
    exit 0
}

# The files the cue list names, as <folder>/<file>.
$cuesPath = Join-Path $ContentRoot 'data\Audio\audio-cues.json'
$listed = @{}
foreach ($cue in (Get-Content -Raw -Path $cuesPath | ConvertFrom-Json).Cues) {
    $folder = switch ($cue.Kind) { 'music' { 'music' } 'ambient' { 'ambient' } default { 'sfx' } }
    foreach ($file in $cue.Files) { $listed["$folder/$file"] = $cue.Id }
}

$ffmpeg = [bool] (Get-Command ffmpeg -ErrorAction SilentlyContinue)
$unmeasured = 0

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
    return $stderr
}

# Integrated loudness and true peak (loudnorm's measurement) of one or more inputs mixed.
function Measure-Loudness([string[]] $Paths) {
    $inputs = @()
    foreach ($path in $Paths) { $inputs += @('-i', $path) }
    $chain = ''
    if ($Paths.Count -gt 1) {
        for ($i = 0; $i -lt $Paths.Count; $i++) { $chain += "[$($i):a]" }
        $chain += "amix=inputs=$($Paths.Count):normalize=0,"
    }
    $chain += "loudnorm=I=$($TargetLufs):TP=$($MaxTruePeak):print_format=json"
    $stderr = Invoke-Ffmpeg (@('-hide_banner', '-nostats') + $inputs + @('-filter_complex', $chain, '-f', 'null', '-'))
    $start = $stderr.LastIndexOf('{')
    $end = $stderr.LastIndexOf('}')
    if ($start -lt 0 -or $end -lt $start) { return $null }
    return $stderr.Substring($start, $end - $start + 1) | ConvertFrom-Json
}

# Duration (s), sample rate and channel layout from ffmpeg's banner.
function Get-StreamInfo([string] $Path) {
    $stderr = Invoke-Ffmpeg @('-hide_banner', '-i', $Path)
    $duration = [regex]::Match($stderr, 'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)')
    $stream = [regex]::Match($stderr, 'Audio:\s*[^,]+,\s*(\d+) Hz,\s*([^,]+)')
    $seconds = -1.0
    if ($duration.Success) {
        $seconds = [double] $duration.Groups[1].Value * 3600 + [double] $duration.Groups[2].Value * 60 + [double] $duration.Groups[3].Value
    }
    return [pscustomobject]@{ Seconds = $seconds; Rate = $stream.Groups[1].Value; Layout = $stream.Groups[2].Value.Trim() }
}

# A WAV's format from its RIFF header: format tag, channels, bits, data seconds.
function Read-WavHeader([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 44 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4) -ne 'WAVE') {
        return $null
    }
    $at = 12
    $format = $null
    while ($at + 8 -le $bytes.Length) {
        $id = [System.Text.Encoding]::ASCII.GetString($bytes, $at, 4)
        $size = [BitConverter]::ToInt32($bytes, $at + 4)
        if ($id -eq 'fmt ') {
            $format = [pscustomobject]@{
                Tag      = [BitConverter]::ToInt16($bytes, $at + 8)
                Channels = [BitConverter]::ToInt16($bytes, $at + 10)
                Rate     = [BitConverter]::ToInt32($bytes, $at + 12)
                Bits     = [BitConverter]::ToInt16($bytes, $at + 22)
                Seconds  = 0.0
            }
        }
        elseif ($id -eq 'data' -and $format -ne $null) {
            $format.Seconds = $size / [double] ($format.Rate * $format.Channels * ($format.Bits / 8))
        }
        $at += 8 + $size + ($size % 2)
    }
    return $format
}

$stemGroups = @{}
foreach ($file in $files) {
    $folder = $file.Directory.Name
    $name = $file.Name
    $key = "$folder/$name"
    $expected = $null
    if ($name -match '^sfx_[a-z0-9]+(_[a-z0-9]+)*_oneshot\.wav$') { $expected = 'sfx' }
    elseif ($name -match '^mus_[a-z0-9]+(_[a-z0-9]+)*_loop\.ogg$') { $expected = 'music' }
    elseif ($name -match '^amb_[a-z0-9]+(_[a-z0-9]+)*_loop\.ogg$') { $expected = 'ambient' }

    if ($expected -eq $null) { Fail "$($key): not on the naming contract."; continue }
    if ($folder -ne $expected -or $file.Directory.Parent.Name -ne 'audio') { Fail "$($key): belongs in audio/$expected/."; continue }
    if (-not $listed.ContainsKey($key)) { Fail "$($key): no cue in audio-cues.json uses it." }

    if ($expected -eq 'sfx') {
        $wav = Read-WavHeader $file.FullName
        if ($wav -eq $null) { Fail "$($key): not a WAV file."; continue }
        if ($wav.Tag -ne 1 -or $wav.Bits -ne 16 -or $wav.Channels -ne 1) { Fail "$($key): not 16-bit PCM mono (tag $($wav.Tag), $($wav.Bits)-bit, $($wav.Channels) ch)." }
        if ($wav.Seconds -gt $MaxSfxSeconds) { Fail ("{0}: {1:N1} s long; a sound effect is under {2} s." -f $key, $wav.Seconds, $MaxSfxSeconds) }
        if (-not $ffmpeg) { $unmeasured++; continue }
        $stderr = Invoke-Ffmpeg @('-hide_banner', '-nostats', '-i', $file.FullName, '-af', 'volumedetect', '-f', 'null', '-')
        $peak = [regex]::Match($stderr, 'max_volume:\s*(-?[0-9.]+) dB')
        if (-not $peak.Success) { Fail "$($key): could not measure its peak."; continue }
        if ([Math]::Abs([double] $peak.Groups[1].Value - $SfxPeak) -gt $SfxPeakTolerance) { Fail "$($key): peak $($peak.Groups[1].Value) dBFS; expected $SfxPeak." }
        continue
    }

    if ($name -match '^mus_([a-z0-9_]+)_(base|pulse|lead|peak)_loop\.ogg$') {
        $track = $Matches[1]
        if (-not $stemGroups.ContainsKey($track)) { $stemGroups[$track] = @() }
        $stemGroups[$track] += $file
        continue
    }

    if (-not $ffmpeg) { $unmeasured++; continue }
    $loud = Measure-Loudness @($file.FullName)
    if ($loud -eq $null) { Fail "$($key): could not measure its loudness."; continue }
    if ([Math]::Abs([double] $loud.input_i - $TargetLufs) -gt $LufsTolerance) { Fail "$($key): $($loud.input_i) LUFS; expected $TargetLufs +/- $LufsTolerance." }
    if ([double] $loud.input_tp -gt $MaxTruePeak) { Fail "$($key): true peak $($loud.input_tp) dBTP; at most $MaxTruePeak." }
}

foreach ($track in $stemGroups.Keys) {
    $stems = $stemGroups[$track]
    if (-not $ffmpeg) { $unmeasured += $stems.Count; continue }
    $infos = @($stems | ForEach-Object { Get-StreamInfo $_.FullName })
    $first = $infos[0]
    for ($i = 1; $i -lt $infos.Count; $i++) {
        if ([Math]::Abs($infos[$i].Seconds - $first.Seconds) -gt $StemLengthTolerance) { Fail ("mus_{0}: stem {1} is {2:N3} s, {3} is {4:N3} s; stems must be the same length." -f $track, $stems[$i].Name, $infos[$i].Seconds, $stems[0].Name, $first.Seconds) }
        if ($infos[$i].Rate -ne $first.Rate -or $infos[$i].Layout -ne $first.Layout) { Fail "mus_$($track): stem $($stems[$i].Name) is $($infos[$i].Rate) Hz $($infos[$i].Layout), not $($first.Rate) Hz $($first.Layout)." }
    }

    $loud = Measure-Loudness @($stems | ForEach-Object { $_.FullName })
    if ($loud -eq $null) { Fail "mus_$($track): could not measure its mix."; continue }
    if ([Math]::Abs([double] $loud.input_i - $TargetLufs) -gt $LufsTolerance) { Fail "mus_$($track) (all stems): $($loud.input_i) LUFS; expected $TargetLufs +/- $LufsTolerance." }
    if ([double] $loud.input_tp -gt $MaxTruePeak) { Fail "mus_$($track) (all stems): true peak $($loud.input_tp) dBTP; at most $MaxTruePeak." }
}

foreach ($failure in $failures) { Write-Host "FAIL $failure" }
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) problem(s) in $($files.Count) audio file(s)."
    exit 1
}

if ($unmeasured -gt 0) {
    Write-Host "Names, cues and WAV headers are fine, but ffmpeg is not on the PATH, so $unmeasured file(s) were not measured."
    exit 2
}

Write-Host "All $($files.Count) audio file(s) pass."
exit 0
