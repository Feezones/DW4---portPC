@echo off
rem Build de depuracao do DW4 (producao 0.6.8 + F3 do PR #17), OFFLINE, com os dados do pacote de producao.
rem F3 mostra posicao/camera/"parede:". O log fica em %APPDATA%\Godot\app_userdata\DW4 - fatia vertical\logs.
set DW4_DADOS=D:\Servidor\DW4\pacote-pc\dados
set DW4_GODOT_NUPKGS=D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64\GodotSharp\Tools\nupkgs
cd /d "%~dp0src\DW4.Client"
dotnet build DW4.Client.csproj -c Debug -v q -nologo
start "" "D:\dw4-tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0src\DW4.Client"
