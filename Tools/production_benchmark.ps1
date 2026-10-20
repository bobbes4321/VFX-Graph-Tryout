param(
    [ValidateSet('smoke', 'full')]
    [string]$Suite = 'full'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$player = Join-Path $projectRoot 'Builds/ProductionLab/ProductionLab.exe'

if (-not (Test-Path -LiteralPath $player)) {
    throw 'Build the complete combat effects player through the Line Lab menu first.'
}

$run = Join-Path $projectRoot ('BenchmarkResults/production/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$arguments = @(
    '--production-benchmark',
    '--suite', $Suite,
    '--results', ('"' + $run + '"'),
    '-logFile', ('"' + (Join-Path $run 'player.log') + '"'),
    '-screen-fullscreen', '0',
    '-force-d3d12'
)

# Keep the player visible: Windows can skip rendering hidden/minimized windows.
$process = Start-Process -FilePath $player -WorkingDirectory $projectRoot -ArgumentList $arguments -PassThru
$process.WaitForExit()

if ($process.ExitCode -ne 0) {
    throw "Player exited with $($process.ExitCode). Inspect $run"
}

python (Join-Path $PSScriptRoot 'production_report.py') $run --out (Join-Path $run 'report')
Write-Host "Measured run: $run"
