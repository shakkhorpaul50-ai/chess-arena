$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $Root 'Stockfish'
$TargetFile = Join-Path $TargetDir 'stockfish.exe'
$Version = '17.1'
$Zip = Join-Path $env:TEMP 'stockfish-win.zip'
$DownloadUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_$Version/stockfish-windows-x86-64-avx2.zip"
$FallbackUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_$Version/stockfish-windows-x86-64.zip"

if (Test-Path -LiteralPath $TargetFile) {
    Write-Host "Stockfish already present at $TargetFile"
    exit 0
}

New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

function Download([string]$Url) {
    Write-Host "Downloading $Url ..."
    Invoke-WebRequest -Uri $Url -OutFile $Zip -UseBasicParsing
}

try {
    Download $DownloadUrl
}
catch {
    Download $FallbackUrl
}

$UnzipDir = Join-Path $env:TEMP 'stockfish-unzip'
if (Test-Path -LiteralPath $UnzipDir) { Remove-Item -LiteralPath $UnzipDir -Recurse -Force }
Expand-Archive -LiteralPath $Zip -DestinationPath $UnzipDir -Force

$exe = Get-ChildItem -Path $UnzipDir -Filter 'stockfish*.exe' -Recurse | Select-Object -First 1
if ($null -eq $exe) {
    throw 'Could not find stockfish executable in the downloaded archive.'
}

Copy-Item -LiteralPath $exe.FullName -Destination $TargetFile
Remove-Item -LiteralPath $UnzipDir -Recurse -Force
Remove-Item -LiteralPath $Zip -Force

Write-Host "Stockfish installed at $TargetFile"