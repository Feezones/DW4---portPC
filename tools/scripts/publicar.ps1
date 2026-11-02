# Publica uma versão nova do port no launcher RE:DIGITAL (jogo "dw4"): exporta o DW4.exe, monta o pacote com os dados
# extraídos e gera o manifesto assinado no CDN local. Os launchers acham a versão nova ao abrir ou a cada 30 minutos e
# baixam só o que mudou.
#
# Os dados extraídos (fatia, fases, area.afs) vão no pacote do CDN por decisão do responsável (2026-11-02); nunca no git.
# Este script não sobe nada para a internet: o envio do CDN local para a produção é um passo separado, com OK.
#
# A exportação .NET autocontida usa os runtime packs win-x64 do nuget.org (cache em %USERPROFILE%\.nuget\packages).
# Uso:  .\tools\scripts\publicar.ps1 -Versao 0.5.0 [-Opcional] [-SemExportar]
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Godot = "D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64",
    [string]$Trabalho = "D:\dw4-work",
    [string]$Pacote = "D:\Servidor\DW4\pacote-pc",
    [string]$Cdn = "C:\RE-DIGITAL-CDN\games\dw4",
    [string]$Chave = "C:\Users\felip\Documents\RE-DIGITAL-chaves\publisher.private.xml",
    [string]$Launcher = "D:\Servidor\RE-DIGITAL-Launcher",
    [switch]$Opcional,
    [switch]$SemExportar
)
$ErrorActionPreference = "Stop"
$repo = Join-Path $PSScriptRoot "..\.." | Resolve-Path
$client = Join-Path $repo "src\DW4.Client"

if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw "Versão no formato 1.2.3: $Versao" }
$commit = (git -C $repo rev-parse --short HEAD).Trim()
if (git -C $repo status --porcelain --untracked-files=no) { Write-Warning "Há mudanças não commitadas; a versão publicada não vai bater com o commit $commit." }

$publisher = Get-ChildItem (Join-Path $Launcher "src\ReDigital.Publisher\bin") -Recurse -Filter "redigital-publish.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $publisher) { throw "redigital-publish.exe não encontrado; compile o launcher (dotnet build RE-DIGITAL.slnx -c Release)." }
$exclude = Join-Path $Launcher "cdn-template\dw4pc.exclude.txt"
if (-not (Test-Path $exclude)) { throw "falta $exclude" }
if (-not (Test-Path $Chave)) { throw "chave de assinatura não encontrada: $Chave" }

New-Item -ItemType Directory -Force -Path $Pacote | Out-Null

# 0. Ícone (janela e DW4.exe) a partir do ícone do jogo no launcher; fica fora do git (arte do jogo).
function Escrever-Icone([string]$png, [string]$dir) {
    Add-Type -AssemblyName System.Drawing
    $src = [Drawing.Image]::FromFile($png)
    try {
        $imagens = @()
        foreach ($s in 256, 64, 48, 32, 16) {
            $bmp = New-Object Drawing.Bitmap $s, $s
            $g = [Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.Clear([Drawing.Color]::Transparent)
            $k = [Math]::Min($s / $src.Width, $s / $src.Height)
            $w = [int]($src.Width * $k); $h = [int]($src.Height * $k)
            $g.DrawImage($src, [int](($s - $w) / 2), [int](($s - $h) / 2), $w, $h)
            $g.Dispose()
            $ms = New-Object IO.MemoryStream
            $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
            if ($s -eq 256) { $bmp.Save((Join-Path $dir "icone.png"), [Drawing.Imaging.ImageFormat]::Png) }
            $bmp.Dispose()
            $imagens += , @($s, $ms.ToArray())
        }
    } finally { $src.Dispose() }
    # ICO com as imagens em PNG (aceito desde o Windows Vista).
    $out = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter $out
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$imagens.Count)
    $offset = 6 + 16 * $imagens.Count
    foreach ($i in $imagens) {
        $s = $i[0]; $b = $i[1]
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$b.Length); $bw.Write([uint32]$offset)
        $offset += $b.Length
    }
    foreach ($i in $imagens) { $bw.Write([byte[]]$i[1]) }
    $bw.Flush()
    [IO.File]::WriteAllBytes((Join-Path $dir "icone.ico"), $out.ToArray())
}
$iconeLauncher = Join-Path $Launcher "cdn-template\art\dw4\icon.png"
if (Test-Path $iconeLauncher) { Escrever-Icone $iconeLauncher $client }
else { Write-Warning "ícone não encontrado ($iconeLauncher); o DW4.exe sai com o ícone da Godot." }

# 1. DW4.exe (release, sem janela de console). O .NET vai junto, na pasta data_DW4.Client_windows_x86_64.
if (-not $SemExportar) {
    $console = Get-ChildItem $Godot -Filter "Godot_v4.7.2-stable_mono_win64_console.exe" | Select-Object -First 1
    if (-not $console) { throw "Godot (console) não encontrada em $Godot" }
    $env:DW4_GODOT_NUPKGS = Join-Path $Godot "GodotSharp\Tools\nupkgs"
    # Os nós do MSBuild e o servidor do compilador (VBCSCompiler) que ficam vivos herdam a saída da Godot e o
    # _console.exe nunca termina.
    $env:MSBUILDDISABLENODEREUSE = "1"
    $env:UseSharedCompilation = "false"
    Remove-Item (Join-Path $Pacote "DW4.exe"), (Join-Path $Pacote "data_*") -Recurse -Force -ErrorAction SilentlyContinue
    & $console.FullName --headless --path $client --export-release "Windows" (Join-Path $Pacote "DW4.exe")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $Pacote "DW4.exe"))) { throw "a exportação da Godot falhou (código $LASTEXITCODE)" }
    # Sem o .sln a Godot exporta "com avisos" e devolve 0, mas o C# fica de fora e o DW4.exe não abre.
    if (-not (Get-ChildItem (Join-Path $Pacote "data_*") -Recurse -Filter "DW4.Client.dll" -ErrorAction SilentlyContinue)) {
        throw "a exportação saiu sem o C# (falta data_*\...\DW4.Client.dll); veja os erros da Godot acima"
    }
}

# 2. Dados: espelha a fatia e as fases (robocopy /MIR só regrava o que mudou) e o area.afs da abertura.
function Espelhar($de, $para, [string[]]$semPastas = @()) {
    $rc = @($de, $para, "/MIR", "/NJH", "/NJS", "/NFL", "/NDL", "/NP", "/R:1", "/W:1")
    if ($semPastas.Count) { $rc += "/XD"; $rc += $semPastas }
    robocopy @rc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy falhou ($LASTEXITCODE): $de" }
}
Espelhar (Join-Path $Trabalho "fatia") (Join-Path $Pacote "dados\fatia") @("analise", "colisao_antiga_*")
Espelhar (Join-Path $Trabalho "fases") (Join-Path $Pacote "dados\fases")
New-Item -ItemType Directory -Force -Path (Join-Path $Pacote "dados\arquivos") | Out-Null
Copy-Item (Join-Path $Trabalho "extracted\files\area.afs") (Join-Path $Pacote "dados\arquivos\area.afs") -Force

# 3. Versão que o jogo mostra no título da janela e no log.
Set-Content -Path (Join-Path $Pacote "versao.txt") -Value "$Versao ($commit)" -Encoding UTF8 -NoNewline

# 4. Manifesto assinado + blocos no CDN local (o build sobe sozinho; só os blocos novos são gravados).
$pubArgs = @("publish", "--game", "dw4", "--version", $Versao, "--source", $Pacote, "--out", $Cdn, "--key", $Chave, "--exclude", $exclude)
if ($Opcional) { $pubArgs += "--optional" }
& $publisher.FullName @pubArgs
if ($LASTEXITCODE -ne 0) { throw "redigital-publish falhou ($LASTEXITCODE)" }

Write-Host ""
Write-Host "Publicado dw4 $Versao ($commit) em $Cdn."
Write-Host "Conferir: $($publisher.FullName) verify --game dw4 --out $Cdn --pub <publisher.public.xml> --hash"
