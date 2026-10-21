param(
    [ValidateSet('smoke', 'authored-smoke', 'full', 'scale', 'spread', 'workloads', 'line-hdr')]
    [string]$Suite = 'scale',
    [ValidateRange(20, 10000)]
    [int]$Warmup = 120,
    [ValidateRange(20, 10000)]
    [int]$Samples = 240
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$player = Join-Path $projectRoot 'Builds/AuthoredLab/AuthoredLab.exe'
if (-not (Test-Path -LiteralPath $player)) {
    throw 'Build the authored effects player through the Effect Workbench first.'
}

# Milliseconds plus a random suffix avoid reusing a prior run directory.
$runName = 'run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$run = Join-Path $projectRoot ('BenchmarkResults/authored/' + $runName)
$parentDirectory = Split-Path $run -Parent
New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
$temporaryLog = $run + '.player.log'
$arguments = @(
    '--production-benchmark',
    '--suite', $Suite,
    '--warmup', $Warmup,
    '--samples', $Samples,
    '--results', ('"' + $run + '"'),
    '-logFile', ('"' + $temporaryLog + '"'),
    '-screen-fullscreen', '0',
    '-force-d3d12'
)

# Rendering remains visible: minimized Windows players can skip GPU work.
# The player creates the empty result directory. Its log stays outside until exit.
if (-not ('LineLabBenchmarkPower' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class LineLabBenchmarkPower
{
    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint flags);
}
'@
}
# Hold a temporary system/display power request, without changing the power plan.
[LineLabBenchmarkPower]::SetThreadExecutionState([uint32]2147483651) | Out-Null
try {
    $process = Start-Process -FilePath $player -WorkingDirectory $projectRoot -ArgumentList $arguments -PassThru
    $process.WaitForExit()
}
finally {
    [LineLabBenchmarkPower]::SetThreadExecutionState([uint32]2147483648) | Out-Null
}
foreach ($manifest in @('source-manifest.json', 'binary-manifest.json', 'build-report.json')) {
    $source = Join-Path (Split-Path $player -Parent) $manifest
    if (Test-Path -LiteralPath $source) {
        New-Item -ItemType Directory -Path $run -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination (Join-Path $run $manifest)
    }
}
if (Test-Path -LiteralPath $temporaryLog) {
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    Move-Item -LiteralPath $temporaryLog -Destination (Join-Path $run 'player.log')
}
if ($process.ExitCode -ne 0) {
    throw "Player exited with $($process.ExitCode). Inspect $run"
}

python (Join-Path $PSScriptRoot 'production_report.py') $run --out (Join-Path $run 'report')
if ($LASTEXITCODE -ne 0) {
    throw "Benchmark audit failed. Inspect $run"
}
python (Join-Path $PSScriptRoot 'authored_index.py')
Write-Host "Measured run: $run"
