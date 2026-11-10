param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)
$ErrorActionPreference = 'Stop'
$fullPath = (Resolve-Path -LiteralPath $Path).Path
$times = [System.Collections.Generic.List[double]]::new()
$waits = [System.Collections.Generic.List[double]]::new()
$captures = 0
$failures = 0
$limitFailures = 0
foreach ($line in [System.IO.File]::ReadLines($fullPath)) {
    if ($line -match 'CAPTURE FAILED') {
        $failures++
        if ($line -match 'Snapshot (object|slot|byte) limit exceeded') { $limitFailures++ }
    }
    if ($line -notmatch '\bCAPTURE id=') { continue }
    $captures++
    if ($line -match '\bcaptureMs=(\d+)') { $times.Add([double]$Matches[1]) }
    elseif ($line -match '\btotal=(\d+)\)') { $times.Add([double]$Matches[1]) }
    if ($line -match '\b(?:arrivalWaitSeconds|after)=([\d.]+)') {
        $waits.Add([double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture))
    }
}
$sorted = @($times | Sort-Object)
function Percentile([double]$fraction) {
    if (!$sorted.Count) { return $null }
    $position = ($sorted.Count - 1) * $fraction
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    return $sorted[$lower] + ($sorted[$upper] - $sorted[$lower]) * ($position - $lower)
}
[pscustomobject]@{
    Path = $fullPath
    Captures = $captures
    TimedCaptures = $times.Count
    UntimedCaptures = $captures - $times.Count
    CaptureMsMedian = Percentile 0.5
    CaptureMsP95 = Percentile 0.95
    CaptureMsMax = ($times | Measure-Object -Maximum).Maximum
    CapturesOver5Seconds = @($times | Where-Object { $_ -gt 5000 }).Count
    ArrivalWaitSecondsMax = ($waits | Measure-Object -Maximum).Maximum
    Failures = $failures
    LimitFailures = $limitFailures
    Note = 'after/arrivalWaitSeconds measures waiting since arrival, not capture work. Untimed records cannot establish stall duration.'
}
