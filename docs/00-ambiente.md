# Relatório do ambiente

Data: 2026-10-24.

## Ambiente de nuvem (sessão do Claude Code)

| Item | Resultado | Comando | Confiança |
|---|---|---|---|
| SO | Ubuntu 24.04.5 LTS, kernel 6.18, x86_64 | `/etc/os-release`, `uname -a` | FATO |
| CPU / RAM | 4 núcleos, 15 GiB | `nproc`, `free -h` | FATO |
| Disco livre | 31 GB (cota da sessão) | `df -h` | FATO |
| Godot | ausente | `command -v godot` | FATO |
| .NET SDK / Mono | ausente | `command -v dotnet` | FATO |
| Git | 2.43.0 | `git --version` | FATO |
| Python | 3.13.17 (numpy, Pillow) | `python3 --version`, `pip list` | FATO |
| Compiladores | gcc, clang, cmake, Node 22 | `command -v` | FATO |
| ffmpeg | 6.1.1 | `ffmpeg -version` | FATO |
| Ferramentas de imagem de disco | nenhuma (7z, bsdtar, xorriso, isoinfo, pycdlib ausentes) | `command -v` | FATO |
| Inspeção binária | só `file` e `sha256sum` | `command -v` | FATO |
| Blender, emuladores | ausentes | `command -v` | FATO |

A sessão de nuvem é efêmera. O disco do jogo não é enviado para ela.

## PC Windows do responsável

Ainda não auditado. Pendente de sessão de Remote Control (somente leitura).

| Item | Comando previsto | Situação |
|---|---|---|
| Windows, CPU, RAM, GPU | `systeminfo`; `Get-CimInstance Win32_VideoController` | DESCONHECIDO |
| Godot e edição .NET | procurar `Godot_v4*` | DESCONHECIDO |
| .NET SDK | `dotnet --list-sdks` | DESCONHECIDO |
| Git, Python, 7-Zip, Blender | `--version` / `where` | DESCONHECIDO |
| Dolphin / DolphinTool | procurar instalação | DESCONHECIDO |
| Espaço livre | `Get-PSDrive -PSProvider FileSystem` | DESCONHECIDO |

## Skills, plugins e conectores (2026-10-24)

- Skills na conta: docs, docx, pdf, pptx, xlsx, google-workspace, computer-use, chrome-browser, built-in-browser, deep-research, skill-creator, import-memory, morning. Nenhuma de engenharia reversa, formatos de console, 3D, áudio ou Godot.
- Plugins: nenhum. Conectores de organização: nenhum.
- Skills locais propostas (a criar após aprovação): `iso-safe-inventory`, `binary-format-probe`, `console-asset-pipeline`, `console-audio-pipeline`, `godot-dotnet-client`, `fidelity-compare`.
