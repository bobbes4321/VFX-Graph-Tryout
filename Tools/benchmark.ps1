param(
    [ValidateSet('smoke', 'full')]
    [string]$Suite = 'full'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$playerPath = Join-Path $projectRoot 'Builds/LineLab/LineLab.exe'

if (-not (Test-Path -LiteralPath $playerPath)) {
    throw 'Build the Windows player with the Line Lab menu first.'
}

$runName = Get-Date -Format 'yyyyMMdd-HHmmss'
$relativeOutput = "BenchmarkResults/$runName-$Suite"
$player = Start-Process -FilePath $playerPath -WorkingDirectory $projectRoot -WindowStyle Normal -PassThru -ArgumentList @(
    '--benchmark',
    '--suite', $Suite,
    '--results', $relativeOutput,
    '-force-d3d12',
    '-screen-fullscreen', '0',
    '-logFile', "$relativeOutput-player.log"
)

Write-Host "Benchmark PID $($player.Id). Progress: $relativeOutput/progress.txt"
$player.WaitForExit()
$runPath = Join-Path $projectRoot $relativeOutput

if (-not (Test-Path -LiteralPath (Join-Path $runPath 'COMPLETE.txt'))) {
    throw "Benchmark did not finish. Inspect $relativeOutput-player.log"
}

Push-Location $projectRoot
try {
    python Tools/report.py $relativeOutput
}
finally {
    Pop-Location
}

Write-Host 'Open BenchmarkResults/report.html for the measured comparison.'
