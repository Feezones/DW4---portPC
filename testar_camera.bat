@echo off
rem Teste local da camera suave (pacote-0.6.22 + claude/project-thread-7xje00), build Debug, jogador forte (DW4_NIVEL=99).
rem Ajuste em Configuracoes > "Transicao da camera" (0 = corte seco). Log em %APPDATA%\Godot\app_userdata\DW4 - fatia vertical\logs.
setlocal
set DW4_NIVEL=99
set DW4_GODOT_NUPKGS=D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64\GodotSharp\Tools\nupkgs
cd /d "%~dp0src\DW4.Client"
dotnet build DW4.Client.csproj -c Debug -v q -nologo
start "" "D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0src\DW4.Client"
endlocal
