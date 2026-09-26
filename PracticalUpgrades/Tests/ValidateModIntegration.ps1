param(
    [string]$RimWorldDir = 'F:\SteamLibrary\steamapps\common\RimWorld'
)

$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$gameData = Join-Path $RimWorldDir 'Data'
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        $failures.Add($Message)
    }
}

function Read-Xml([string]$Path) {
    try {
        return [xml](Get-Content -LiteralPath $Path -Raw)
    }
    catch {
        $failures.Add("Invalid XML: $Path ($($_.Exception.Message))")
        return $null
    }
}

Write-Host '=== XML and definition validation ==='
$modXmlFiles = Get-ChildItem -LiteralPath $modRoot -Recurse -Filter '*.xml' -File
$modDocuments = @{}
foreach ($file in $modXmlFiles) {
    $document = Read-Xml $file.FullName
    if ($null -ne $document) {
        $modDocuments[$file.FullName] = $document
    }
}

$definitionNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$definitionKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$allDefinitionFiles = @(Get-ChildItem -LiteralPath $gameData -Recurse -Filter '*.xml' -File) + $modXmlFiles
foreach ($file in $allDefinitionFiles) {
    $document = if ($modDocuments.ContainsKey($file.FullName)) { $modDocuments[$file.FullName] } else { Read-Xml $file.FullName }
    if ($null -eq $document -or $null -eq $document.Defs) {
        continue
    }

    foreach ($definition in $document.Defs.ChildNodes) {
        if ($definition.NodeType -ne [System.Xml.XmlNodeType]::Element -or $null -eq $definition.defName) {
            continue
        }

        $name = [string]$definition.defName
        $null = $definitionNames.Add($name)
        if ($file.FullName.StartsWith($modRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $key = $definition.Name + ':' + $name
            Assert-Check ($definitionKeys.Add($key)) "Duplicate mod definition: $key"
        }
    }
}

$referenceXpaths = @(
    '//moduleDef',
    '//requiredResearch',
    '//researchPrerequisite',
    '//prerequisites/li',
    '//recipeUsers/li',
    '//ingredients/li/filter/thingDefs/li',
    '//fixedIngredientFilter/thingDefs/li'
)
foreach ($document in $modDocuments.Values) {
    foreach ($xpath in $referenceXpaths) {
        foreach ($node in $document.SelectNodes($xpath)) {
            $reference = $node.InnerText.Trim()
            if ($reference) {
                Assert-Check ($definitionNames.Contains($reference)) "Missing referenced definition: $reference"
            }
        }
    }

    foreach ($products in $document.SelectNodes('//products')) {
        foreach ($product in $products.ChildNodes) {
            if ($product.NodeType -eq [System.Xml.XmlNodeType]::Element) {
                Assert-Check ($definitionNames.Contains($product.Name)) "Missing product definition: $($product.Name)"
            }
        }
    }
}

Write-Host '=== Texture and translation validation ==='
foreach ($document in $modDocuments.Values) {
    foreach ($node in $document.SelectNodes('//texPath')) {
        $texturePath = $node.InnerText.Trim()
        if ($texturePath.StartsWith('PracticalUpgrades/', [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $texturePath.Substring('PracticalUpgrades/'.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
            $filePath = Join-Path (Join-Path $modRoot 'Textures\PracticalUpgrades') ($relative + '.png')
            Assert-Check (Test-Path -LiteralPath $filePath -PathType Leaf) "Missing texture: $texturePath"
        }
    }
}

$languageKeys = @{}
foreach ($language in @('English', 'ChineseSimplified')) {
    $languageFile = Join-Path $modRoot "Languages\$language\Keyed\PracticalUpgrades.xml"
    $document = Read-Xml $languageFile
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    if ($null -ne $document) {
        foreach ($node in $document.LanguageData.ChildNodes) {
            if ($node.NodeType -eq [System.Xml.XmlNodeType]::Element) {
                $null = $keys.Add($node.Name)
            }
        }
    }
    $languageKeys[$language] = $keys
}

$usedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sourceText = (Get-ChildItem (Join-Path $modRoot 'Source') -Recurse -Filter '*.cs' -File | Get-Content -Raw) -join "`n"
foreach ($match in [regex]::Matches($sourceText, '"(?<key>PU_[A-Za-z0-9_]+)"\.Translate')) {
    $null = $usedKeys.Add($match.Groups['key'].Value)
}
foreach ($document in $modDocuments.Values) {
    foreach ($node in $document.SelectNodes('//labelKey|//reportString')) {
        $key = $node.InnerText.Trim()
        if ($key.StartsWith('PU_', [System.StringComparison]::Ordinal)) {
            $null = $usedKeys.Add($key)
        }
    }
}
foreach ($key in $usedKeys) {
    foreach ($language in $languageKeys.Keys) {
        Assert-Check ($languageKeys[$language].Contains($key)) "Missing $language keyed translation: $key"
    }
}

Write-Host '=== Upgrade notification validation ==='
foreach ($sourceFile in @(
    'Source\PracticalUpgrades\UpgradeableFacility.cs',
    'Source\PracticalUpgrades\GravEngineUpgrades.cs'
)) {
    $notificationSource = Get-Content -LiteralPath (Join-Path $modRoot $sourceFile) -Raw
    Assert-Check ($notificationSource -notmatch 'PU_UpgradeComplete"\.Translate\(parent\.LabelCap') "Upgrade notification reads the transformed post-upgrade label in $sourceFile"
    Assert-Check ($notificationSource -match '(?s)string previousLabel = CurrentLevel.+?upgradeLevel\+\+;.+?PU_UpgradeComplete"\.Translate\(previousLabel, CurrentLevel\.Label\)') "Upgrade notification does not preserve the pre-upgrade label in $sourceFile"
}

Write-Host '=== Vanilla patch target validation ==='
$coreBuildings = Read-Xml (Join-Path $gameData 'Core\Defs\ThingDefs_Buildings\Buildings_Misc.xml')
$odysseyBuildings = Read-Xml (Join-Path $gameData 'Odyssey\Defs\ThingDefs_Buildings\Buildings_Gravship.xml')
Assert-Check ($coreBuildings.SelectNodes('//ThingDef[defName="ToolCabinet"]/comps/li[@Class="CompProperties_Facility"]').Count -eq 1) 'ToolCabinet patch target changed or is ambiguous.'
Assert-Check ($odysseyBuildings.SelectNodes('//ThingDef[defName="GravEngine"]/comps').Count -eq 1) 'GravEngine patch target changed or is ambiguous.'

$gravPatch = $modDocuments[(Join-Path $modRoot 'Patches\GravEngine_Upgradeable.xml')]
Assert-Check ($gravPatch.SelectNodes('//Operation[@Class="PatchOperationFindMod"]/mods/li[text()="Odyssey"]').Count -eq 1) 'Grav engine patch is not gated behind Odyssey.'
foreach ($fileName in @('Defs\RecipeDefs\Recipes_Modules.xml', 'Defs\ThingDefs_Items\Items_Modules.xml')) {
    $document = $modDocuments[(Join-Path $modRoot $fileName)]
    foreach ($definition in $document.Defs.ChildNodes) {
        if ($definition.NodeType -eq [System.Xml.XmlNodeType]::Element -and ([string]$definition.defName) -match 'Grav') {
            Assert-Check ([string]$definition.MayRequire -eq 'Ludeon.RimWorld.Odyssey') "Odyssey definition lacks MayRequire: $($definition.defName)"
        }
    }
}

Write-Host '=== Research tree collision validation ==='
$researchFile = $modDocuments[(Join-Path $modRoot 'Defs\ResearchProjectDefs\ResearchProjects.xml')]
foreach ($project in $researchFile.Defs.ResearchProjectDef) {
    $x = [double]$project.researchViewX
    $y = [double]$project.researchViewY
    foreach ($file in Get-ChildItem -LiteralPath $gameData -Recurse -Filter '*.xml' -File) {
        $document = Read-Xml $file.FullName
        if ($null -eq $document -or $null -eq $document.Defs) {
            continue
        }
        foreach ($vanillaProject in $document.Defs.ResearchProjectDef) {
            if ($null -eq $vanillaProject.researchViewX -or $null -eq $vanillaProject.researchViewY) {
                continue
            }
            $dx = [math]::Abs($x - [double]$vanillaProject.researchViewX)
            $dy = [math]::Abs($y - [double]$vanillaProject.researchViewY)
            Assert-Check (-not ($dx -lt 0.75 -and $dy -lt 0.50)) "Research node collision: $($project.defName) and $($vanillaProject.defName)"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "`nVALIDATION FAILED ($($failures.Count))" -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "`nALL MOD INTEGRATION CHECKS PASSED" -ForegroundColor Green
