param(
    [int]$Trials = 1000000,
    [int]$Seed = 20260924
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$definitionPath = Join-Path $modRoot 'Patches\ToolCabinet_Upgradeable.xml'
[xml]$definition = Get-Content -LiteralPath $definitionPath -Raw

$levelNodes = @($definition.Patch.Operation.value.li.upgradeLevels.li)
if ($levelNodes.Count -ne 4) {
    throw "Expected four tool cabinet states (base plus three upgrades), found $($levelNodes.Count)."
}

$labels = @{
    PU_LevelStandard       = '标准'
    PU_LevelPrecision      = 'I级'
    PU_LevelUltraPrecision = 'II级'
    PU_LevelPersona        = 'III级'
}

$levels = foreach ($node in $levelNodes) {
    [pscustomobject]@{
        Name     = $labels[[string]$node.labelKey]
        Speed    = [float]$node.statOffsets.WorkTableWorkSpeedFactor
        Recovery = if ($node.materialRecoveryChance) { [float]$node.materialRecoveryChance } else { 0.0 }
        Quality  = if ($node.qualityShiftChance) { [float]$node.qualityShiftChance } else { 0.0 }
    }
}

$expected = @{
    '标准 + 标准' = @(0.12, 0.00, 0.00)
    'I级 + I级'   = @(0.20, 0.00, 0.00)
    'II级 + II级' = @(0.30, 0.10, 0.00)
    'III级 + III级' = @(0.50, 0.20, 0.20)
    'I级 + III级' = @(0.35, 0.10, 0.10)
    'II级 + III级' = @(0.40, 0.15, 0.10)
}

function Format-Percent([double]$value) {
    return $value.ToString('P0', [Globalization.CultureInfo]::InvariantCulture)
}

function Assert-Near([double]$actual, [double]$wanted, [double]$tolerance, [string]$message) {
    if ([Math]::Abs($actual - $wanted) -gt $tolerance) {
        throw "$message Actual=$actual Expected=$wanted Tolerance=$tolerance"
    }
}

Write-Output '=== Deterministic two-cabinet aggregation ==='
$deterministicPass = $true
for ($left = 0; $left -lt $levels.Count; $left++) {
    for ($right = $left; $right -lt $levels.Count; $right++) {
        $a = $levels[$left]
        $b = $levels[$right]
        $name = "$($a.Name) + $($b.Name)"
        $speed = $a.Speed + $b.Speed
        $recovery = [Math]::Min(0.20, $a.Recovery + $b.Recovery)
        $quality = [Math]::Min(0.20, $a.Quality + $b.Quality)
        $status = 'PASS'

        if ($expected.ContainsKey($name)) {
            $wanted = $expected[$name]
            try {
                Assert-Near $speed $wanted[0] 0.000001 "$name speed mismatch."
                Assert-Near $recovery $wanted[1] 0.000001 "$name recovery mismatch."
                Assert-Near $quality $wanted[2] 0.000001 "$name quality mismatch."
            }
            catch {
                $status = 'FAIL'
                $deterministicPass = $false
            }
        }

        [pscustomobject]@{
            Combination = $name
            SpeedBonus = Format-Percent $speed
            FinalBenchFactor = Format-Percent (1.0 + $speed)
            Recovery = Format-Percent $recovery
            QualityShift = Format-Percent $quality
            Status = $status
        }
    }
}

if (-not $deterministicPass) {
    throw 'One or more deterministic aggregation checks failed.'
}

Write-Output ''
Write-Output "=== Monte Carlo ($Trials trials per effect, seed $Seed) ==="
$random = [Random]::new($Seed)
$fullLevel = $levels[3]
$recoveryChance = [Math]::Min(0.20, $fullLevel.Recovery * 2.0)
$qualityChance = [Math]::Min(0.20, $fullLevel.Quality * 2.0)
$recovered = 0
$qualityShifted = 0

for ($i = 0; $i -lt $Trials; $i++) {
    if ($random.NextDouble() -lt $recoveryChance) { $recovered++ }
    if ($random.NextDouble() -lt $qualityChance) { $qualityShifted++ }
}

$observedRecovery = $recovered / [double]$Trials
$observedQuality = $qualityShifted / [double]$Trials
$recoverySigma = [Math]::Sqrt($recoveryChance * (1.0 - $recoveryChance) / $Trials)
$qualitySigma = [Math]::Sqrt($qualityChance * (1.0 - $qualityChance) / $Trials)

Assert-Near $observedRecovery $recoveryChance (5.0 * $recoverySigma) 'Recovery simulation fell outside five standard deviations.'
Assert-Near $observedQuality $qualityChance (5.0 * $qualitySigma) 'Quality simulation fell outside five standard deviations.'

[pscustomobject]@{
    Effect = '两台III级：材料回收'
    Expected = Format-Percent $recoveryChance
    Triggered = $recovered
    Trials = $Trials
    Observed = $observedRecovery.ToString('P3', [Globalization.CultureInfo]::InvariantCulture)
    FiveSigmaRange = ('{0:P3} - {1:P3}' -f ($recoveryChance - 5 * $recoverySigma), ($recoveryChance + 5 * $recoverySigma))
    Status = 'PASS'
}

[pscustomobject]@{
    Effect = '两台III级：品质提升'
    Expected = Format-Percent $qualityChance
    Triggered = $qualityShifted
    Trials = $Trials
    Observed = $observedQuality.ToString('P3', [Globalization.CultureInfo]::InvariantCulture)
    FiveSigmaRange = ('{0:P3} - {1:P3}' -f ($qualityChance - 5 * $qualitySigma), ($qualityChance + 5 * $qualitySigma))
    Status = 'PASS'
}

Write-Output ''
Write-Output 'ALL SIMULATION CHECKS PASSED'
