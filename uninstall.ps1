param(
    [string]$GameDirectory = 'D:\Chill with You\Chill with You',
    [switch]$RemoveEncryptedData
)

$ErrorActionPreference = 'Stop'
$resolvedGame = [IO.Path]::GetFullPath($GameDirectory)
$gameExe = Join-Path $resolvedGame 'Chill With You.exe'
if (-not (Test-Path -LiteralPath $gameExe)) { throw "Not a Chill With You installation: $resolvedGame" }

$pluginPath = [IO.Path]::GetFullPath((Join-Path $resolvedGame 'BepInEx\plugins\AICompanion'))
$allowedRoot = $resolvedGame.TrimEnd('\') + '\'
if (-not ($pluginPath + '\').StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe plugin removal target.' }
if (Test-Path -LiteralPath $pluginPath) { Remove-Item -LiteralPath $pluginPath -Recurse -Force }

if ($RemoveEncryptedData) {
    $dataPath = [IO.Path]::GetFullPath((Join-Path $resolvedGame 'BepInEx\data\AICompanion'))
    if (-not ($dataPath + '\').StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe data removal target.' }
    if (Test-Path -LiteralPath $dataPath) { Remove-Item -LiteralPath $dataPath -Recurse -Force }
}

Write-Host 'AI Companion plugin removed. BepInEx itself was retained so other mods are not affected.'
