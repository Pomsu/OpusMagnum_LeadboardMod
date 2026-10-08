# Regenerates tests/ZlbbCore.Tests/fixtures/P007_frontier.json (live API data) and P007_golden.json
# (expected histogram per metric, computed here INDEPENDENTLY of the C# code, straight from the raw JSON).
param([string]$PuzzleId = 'P007')
$ErrorActionPreference = 'Stop'
$fix = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\ZlbbCore.Tests\fixtures'
New-Item -ItemType Directory -Force $fix | Out-Null
$raw = Join-Path $fix "${PuzzleId}_frontier.json"
Invoke-WebRequest "https://zlbb.faendir.com/om/puzzle/$PuzzleId/records?includeFrontier=true" -OutFile $raw -UseBasicParsing
$records = Get-Content $raw -Raw -Encoding UTF8 | ConvertFrom-Json

# metric key -> scriptblock returning a number or $null (same definitions as the website's visualizer)
$metrics = [ordered]@{
    g  = { param($s) $s.cost }
    i  = { param($s) $s.instructions }
    c  = { param($s) $s.cycles }
    a  = { param($s) $s.area }
    h  = { param($s) $s.height }
    w  = { param($s) $s.width }
    b  = { param($s) $s.boundingHex }
    r  = { param($s) $s.rate }
    aI = { param($s) if ($null -ne $s.areaINFValue -and $null -ne $s.areaINFLevel) { $s.areaINFValue * [math]::Pow(10, 5 * $s.areaINFLevel) } else { $null } }
    hI = { param($s) $s.heightINF }
    wI = { param($s) $s.widthINF }
    bI = { param($s) $s.boundingHexINF }
}

$golden = [ordered]@{ puzzle = $PuzzleId; recordCount = @($records).Count; metrics = [ordered]@{}; trackless = [ordered]@{} }
foreach ($variant in 'all', 'trackless') {
    foreach ($key in $metrics.Keys) {
        $values = @()
        foreach ($rec in $records) {
            if ($variant -eq 'trackless' -and $rec.score.trackless) { continue }   # "trackless = false" filter
            $v = & $metrics[$key] $rec.score
            if ($null -ne $v) { $values += [int][math]::Round([double]$v, [MidpointRounding]::AwayFromZero) }
        }
        $text = ''
        if ($values.Count -gt 0) {
            $min = ($values | Measure-Object -Minimum).Minimum
            $max = ($values | Measure-Object -Maximum).Maximum
            $counts = New-Object 'int[]' ($max - $min + 1)
            foreach ($v in $values) { $counts[$v - $min]++ }
            $text = "$min," + ($counts -join ',')
        }
        if ($variant -eq 'all') { $golden.metrics[$key] = $text } else { $golden.trackless[$key] = $text }
    }
}
$golden | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fix "${PuzzleId}_golden.json") -Encoding UTF8
"records=$(@($records).Count)"; $golden.metrics.GetEnumerator() | ForEach-Object { "{0,-3} {1}" -f $_.Key, ($(if ($_.Value.Length -gt 70) { $_.Value.Substring(0, 70) + '...' } else { $_.Value })) }
