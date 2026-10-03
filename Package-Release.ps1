param(
    [Parameter(Mandatory = $true)]
    [string]$GameBuild,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$gameRoot = (Resolve-Path -LiteralPath $GameBuild).Path
$outputRoot = [System.IO.Path]::GetFullPath($Output)
$gameExecutable = Join-Path $gameRoot 'Vector 2.exe'
$gameData = Join-Path $gameRoot 'Vector 2_Data'

if (-not (Test-Path -LiteralPath $gameExecutable -PathType Leaf)) {
    throw "The selected game build does not contain Vector 2.exe: $gameRoot"
}
if (-not (Test-Path -LiteralPath $gameData -PathType Container)) {
    throw "The selected game build does not contain Vector 2_Data: $gameRoot"
}
if (Test-Path -LiteralPath $outputRoot) {
    if ((Get-ChildItem -LiteralPath $outputRoot -Force | Select-Object -First 1)) {
        throw "The release output must be empty: $outputRoot"
    }
} else {
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
}

dotnet publish (Join-Path $projectRoot 'Vector2LevelEditor.Wpf.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outputRoot
if ($LASTEXITCODE -ne 0) {
    throw "Editor publish failed with exit code $LASTEXITCODE."
}

$bundledGame = Join-Path $outputRoot 'Vector 2 Game'
New-Item -ItemType Directory -Path $bundledGame | Out-Null
& robocopy $gameRoot $bundledGame /E `
    /XD 'Vector 2_BackUpThisFolder_ButDontShipItWithYourGame' `
    /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
$copyExit = $LASTEXITCODE
if ($copyExit -ge 8) {
    throw "Game build copy failed with robocopy exit code $copyExit."
}

Get-ChildItem -LiteralPath $projectRoot -Filter '*CHANGELOG.md' -File |
    Copy-Item -Destination $outputRoot

$releaseGame = Join-Path $bundledGame 'Vector 2.exe'
if (-not (Test-Path -LiteralPath $releaseGame -PathType Leaf)) {
    throw "Release verification failed: bundled Vector 2.exe is missing."
}

Write-Output "Release created: $outputRoot"
Write-Output "Editor: $(Join-Path $outputRoot 'Vector2LevelEditor.exe')"
Write-Output "Game: $releaseGame"
