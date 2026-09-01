param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path (Split-Path -Parent $projectRoot) '.tools\dotnet\dotnet.exe'
$pluginProject = Join-Path $projectRoot 'src\AICompanion.Plugin\AICompanion.Plugin.csproj'
$testProject = Join-Path $projectRoot 'tests\AICompanion.Tests\AICompanion.Tests.csproj'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$distRoot = Join-Path $projectRoot 'dist\AICompanion'
$pluginDist = Join-Path $distRoot 'BepInEx\plugins\AICompanion'
$bepInExSource = Join-Path (Split-Path -Parent $projectRoot) '.tools\bepinex-5.4.23.5'

if (-not (Test-Path -LiteralPath $dotnet)) { throw "Portable .NET SDK was not found at $dotnet" }
if (-not (Test-Path -LiteralPath $bepInExSource)) { throw "BepInEx staging files were not found at $bepInExSource" }

& $dotnet restore $pluginProject --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { throw 'Plugin restore failed.' }
& $dotnet build $pluginProject -c $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& $dotnet restore $testProject --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
& $dotnet run --project $testProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }

New-Item -ItemType Directory -Force -Path $pluginDist | Out-Null
$output = Join-Path $projectRoot "src\AICompanion.Plugin\bin\$Configuration\netstandard2.1"
foreach ($name in 'AICompanion.Core.dll','AICompanion.Infrastructure.dll','AICompanion.Unity.dll','AICompanion.Plugin.dll') {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $pluginDist $name) -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'manifest.json') -Destination (Join-Path $pluginDist 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $distRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.md') -Destination (Join-Path $distRoot '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\AICompanion.Unity\Assets\THIRD_PARTY_NOTICES.md') -Destination (Join-Path $pluginDist 'THIRD_PARTY_NOTICES.md') -Force

foreach ($name in '.doorstop_version','changelog.txt','doorstop_config.ini','winhttp.dll') {
    Copy-Item -LiteralPath (Join-Path $bepInExSource $name) -Destination (Join-Path $distRoot $name) -Force
}
Copy-Item -LiteralPath (Join-Path $bepInExSource 'BepInEx\core') -Destination (Join-Path $distRoot 'BepInEx') -Recurse -Force

$zip = Join-Path $projectRoot 'dist\ChillWithYou-AICompanion-1.0.2.zip'
Compress-Archive -Path (Join-Path $distRoot '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host "Built and tested: $zip"
