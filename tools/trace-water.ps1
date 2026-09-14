param([string]$Godot, [string[]]$EngineArgs = @())

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$gamePath = Join-Path $repoPath 'game'
if (-not $Godot) {
    $Godot = Get-ChildItem (Join-Path $env:USERPROFILE 'Documents/Godot') -Filter '*mono*console.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Godot -or -not (Test-Path -LiteralPath $Godot)) {
    throw 'Pass -Godot with the path to the Godot .NET console executable.'
}
$artifactPath = Join-Path $gamePath '.artifacts'
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$consoleLog = Join-Path $artifactPath "water-crash-$stamp-console.log"
$engineLog = Join-Path $artifactPath "water-crash-$stamp-engine.log"
$previousTrace = $env:FT_SURFACE_TRACE
try {
    $env:FT_SURFACE_TRACE = '1'
    Write-Host "Fly the free camera toward water. Console and camera breadcrumbs: $consoleLog"
    $ErrorActionPreference = 'Continue'
    & $Godot --path $gamePath --verbose --log-file $engineLog @EngineArgs 2>&1 | Tee-Object -FilePath $consoleLog
    $result = $LASTEXITCODE
    "Godot exit code: $result" | Tee-Object -FilePath $consoleLog -Append
} finally {
    $env:FT_SURFACE_TRACE = $previousTrace
}
