$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $modRoot 'Patches\GravEngine_Upgradeable.xml'
$recipePath = Join-Path $modRoot 'Defs\RecipeDefs\Recipes_Modules.xml'

[xml]$patch = Get-Content -Raw $patchPath
[xml]$recipes = Get-Content -Raw $recipePath

$levels = @($patch.SelectNodes('//upgradeLevels/li'))
if ($levels.Count -ne 4) {
    throw "Expected four grav engine states, found $($levels.Count)."
}

$expected = @(
    @{ Extenders = 2; Small = 2; Large = 0; Power = 0; Capacity = 0; Recharge = 0; Offset = 0.00 },
    @{ Extenders = 3; Small = 3; Large = 0; Power = 1000; Capacity = 100; Recharge = 20; Offset = 0.25 },
    @{ Extenders = 4; Small = 4; Large = 4; Power = 2000; Capacity = 250; Recharge = 50; Offset = 0.50 },
    @{ Extenders = 6; Small = 4; Large = 6; Power = 3000; Capacity = 500; Recharge = 100; Offset = 0.75 }
)

for ($i = 0; $i -lt $levels.Count; $i++) {
    $level = $levels[$i]
    $actual = @{
        Extenders = [int]$level.gravFieldExtenderLimit
        Small = [int]$level.smallThrusterLimit
        Large = [int]$level.largeThrusterLimit
        Power = if ($level.powerOutput) { [int]$level.powerOutput } else { 0 }
        Capacity = if ($level.energyCapacity) { [float]$level.energyCapacity } else { 0 }
        Recharge = if ($level.energyRechargePerDay) { [float]$level.energyRechargePerDay } else { 0 }
        Offset = if ($level.maxFuelOffsetFraction) { [float]$level.maxFuelOffsetFraction } else { 0 }
    }

    foreach ($key in $expected[$i].Keys) {
        if ($actual[$key] -ne $expected[$i][$key]) {
            throw "Level $i $key expected $($expected[$i][$key]), found $($actual[$key])."
        }
    }

    if ($actual.Capacity -gt 0 -and [math]::Abs(($actual.Capacity / $actual.Recharge) - 5) -gt 0.001) {
        throw "Level $i does not take five days to charge."
    }
}

$expectedResearch = @('BasicGravtech', 'StandardGravtech', 'AdvancedGravtech')
for ($i = 1; $i -lt $levels.Count; $i++) {
    if ([string]$levels[$i].requiredResearch -ne $expectedResearch[$i - 1]) {
        throw "Level $i has the wrong research prerequisite."
    }
}

$gravRecipes = @($recipes.SelectNodes('/Defs/RecipeDef[starts-with(defName, "PU_MakeGrav")]'))
$multiphase = $recipes.SelectSingleNode('/Defs/RecipeDef[defName="PU_MakeMultiphaseGravCore"]')
if ($gravRecipes.Count -ne 2 -or $null -eq $multiphase) {
    throw 'Expected all three grav engine module recipes.'
}

function Get-FuelResult([float]$FuelCost, [float]$Energy, [float]$OffsetFraction) {
    $offset = [math]::Min($Energy, $FuelCost * $OffsetFraction)
    return @{ Offset = $offset; Fuel = $FuelCost - $offset }
}

$checks = @(
    @{ Level = 1; Energy = 100; ExpectedOffset = 50; ExpectedFuel = 150 },
    @{ Level = 2; Energy = 250; ExpectedOffset = 100; ExpectedFuel = 100 },
    @{ Level = 3; Energy = 500; ExpectedOffset = 150; ExpectedFuel = 50 }
)

foreach ($check in $checks) {
    $result = Get-FuelResult 200 $check.Energy $expected[$check.Level].Offset
    if ($result.Offset -ne $check.ExpectedOffset -or $result.Fuel -ne $check.ExpectedFuel) {
        throw "Fuel simulation failed for level $($check.Level)."
    }
}

Write-Output 'GRAV ENGINE CONFIGURATION AND FUEL SIMULATION PASSED'
