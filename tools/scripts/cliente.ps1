# Compila e abre o cliente da fatia vertical (src/DW4.Client) sem baixar pacotes: usa só os .nupkg da Godot.
# Uso: .\tools\scripts\cliente.ps1 [-Editor] [-Fatia D:\dw4-work\fatia]
param(
    [string]$Godot = "D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64",
    [string]$Fatia = "D:\dw4-work\fatia",
    [switch]$Editor
)
$ErrorActionPreference = "Stop"
$client = Join-Path $PSScriptRoot "..\..\src\DW4.Client" | Resolve-Path
$env:DW4_GODOT_NUPKGS = Join-Path $Godot "GodotSharp\Tools\nupkgs"
$env:DW4_FATIA = $Fatia
if (-not (Test-Path $env:DW4_GODOT_NUPKGS)) { throw "pasta de pacotes da Godot não encontrada: $env:DW4_GODOT_NUPKGS" }
dotnet build (Join-Path $client "DW4.Client.csproj") -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$exe = Get-ChildItem $Godot -Filter "Godot_v4.7.2-stable_mono_win64*.exe" | Where-Object { $_.Name -notmatch "console" } | Select-Object -First 1
if ($Editor) { & $exe.FullName --editor --path $client } else { & $exe.FullName --path $client }
