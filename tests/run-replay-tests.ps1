<#
    Replays every capture in tests/captures and compares the transcript it produces against
    the .expected file beside it.

    Each capture is replayed at several chunk sizes. Chunk boundaries are not cosmetic: a
    screen wipe arriving in the same read as the output it is about to destroy, or an escape
    sequence split across two reads, are exactly the cases the assembly has to get right, and
    a single-read replay never exercises them.
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [int[]]$ChunkSizes = @(16384, 7, 1)
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
}
$captures = Join-Path $Root 'tests\captures'
$cli = Join-Path $Root 'src\BlindTerm.Cli'

# Per-capture terminal size. A recorded capture has to be replayed at the size it was
# recorded at, or every wrapped line lands differently; those carry a .size file beside them.
# Synthetic captures that need a particular width are listed here.
$sizes = @{ 'wrapped' = @{ Cols = 20; Rows = 10 } }

dotnet build $Root -v q --nologo | Out-Null

$failed = 0
$ran = 0

foreach ($raw in Get-ChildItem $captures -Filter *.raw | Sort-Object Name) {
    $name = [IO.Path]::GetFileNameWithoutExtension($raw.Name)
    $expectedPath = Join-Path $captures "$name.expected"
    if (-not (Test-Path $expectedPath)) {
        Write-Host "SKIP $name (no .expected)"
        continue
    }

    $expected = (Get-Content $expectedPath -Raw) -replace "`r`n", "`n"
    $cols = 120; $rows = 30
    $sizePath = Join-Path $captures "$name.size"
    if (Test-Path $sizePath) {
        $recorded = Get-Content $sizePath -Raw | ConvertFrom-Json
        $cols = $recorded.cols; $rows = $recorded.rows
    }
    elseif ($sizes.ContainsKey($name)) { $cols = $sizes[$name].Cols; $rows = $sizes[$name].Rows }

    foreach ($chunk in $ChunkSizes) {
        $ran++
        # Windows PowerShell 5 turns any native stderr into a PowerShell error record when
        # ErrorActionPreference is Stop, even when stderr is redirected. Replay writes its
        # byte-count diagnostic there on every successful run, so relax only around this
        # native invocation and continue to judge the structured stdout below.
        $savedErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $out = & dotnet run --project $cli --no-build -- replay $raw.FullName `
            --cols $cols --rows $rows --chunk $chunk 2>$null
        $ErrorActionPreference = $savedErrorActionPreference

        # Everything between the transcript marker and the next marker.
        $lines = @($out) -split "`n"
        $start = [Array]::IndexOf($lines, '--- transcript ---')
        $end = [Array]::IndexOf($lines, '--- current line ---')
        if ($start -lt 0 -or $end -lt 0) {
            Write-Host "FAIL $name (chunk $chunk): no transcript in output"
            $failed++
            continue
        }
        # An empty transcript is a real and important expectation: a full-screen program's
        # frames must not reach it at all. Slice carefully, because PowerShell reverses a
        # range whose start is past its end.
        $slice = @()
        if (($end - 1) -ge ($start + 1)) { $slice = @($lines[($start + 1)..($end - 1)]) }
        $actual = ($slice -join "`n").TrimEnd() + "`n"
        $want = ([string]$expected).TrimEnd() + "`n"

        if ($actual -ceq $want) {
            Write-Host "ok   $name (chunk $chunk)"
        }
        else {
            Write-Host "FAIL $name (chunk $chunk)"
            Write-Host "  expected:"
            $want.TrimEnd()  -split "`n" | ForEach-Object { Write-Host "    |$_" }
            Write-Host "  actual:"
            $actual.TrimEnd() -split "`n" | ForEach-Object { Write-Host "    |$_" }
            $failed++
        }
    }
}

Write-Host ""
Write-Host "$ran replays, $failed failed"
if ($failed -gt 0) { exit 1 }
exit 0
