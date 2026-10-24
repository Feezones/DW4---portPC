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

Auditado em 2026-10-24 via Remote Control, somente leitura.

| Item | Resultado | Comando | Confiança |
|---|---|---|---|
| SO | Windows 11 Pro 10.0.26200, x64 | `systeminfo` | FATO |
| CPU | AMD Ryzen 5 5600GT, 6 núcleos / 12 threads, 3,6 GHz | `Win32_Processor` | FATO |
| RAM | 23,8 GB (4,4 GB livres no momento da medição) | `Win32_ComputerSystem`, `systeminfo` | FATO |
| GPU | NVIDIA GeForce RTX 2060, driver 32.0.16.1088, 1920x1080 | `Win32_VideoController` | FATO (VRAM informada ~4 GB pode estar truncada pelo campo de 32 bits) |
| Disco livre | C: 56,04 GB; D: 48,41 GB | `Get-PSDrive` | FATO |
| Godot | não encontrado (PATH, pastas comuns, C:\Jogos, D:\, registro) | `Get-Command`, busca de `Godot*.exe` | FATO |
| .NET SDK | 10.0.401 | `dotnet --list-sdks` | FATO |
| Git | 2.55.0.windows.2 | `git --version` | FATO |
| Python | não instalado (só o atalho da Microsoft Store) | `py`, `python --version` | FATO |
| 7-Zip | não instalado; só cópias embutidas em outros programas (NVIDIA App, Omnissa Horizon, BlueStacks), que não serão usadas | busca | FATO |
| Blender | portáteis em `D:\dscs_tools\` (2.91.2 e 4.5.14) | busca | FATO |
| Dolphin / DolphinTool | 3 cópias: `C:\Jogos\RE-DIGITAL\Digimon World 4\Dolphin\`, `D:\Servidor\DW4\pacote\Dolphin\`, `D:\Servidor\DW4\tools\Dolphin-x64\`. Versão não obtida (`--version` retornou código 1) | busca | FATO |
| PCSX2 | não encontrado | busca | FATO |

Observações:
- A pasta `C:\Jogos\RE-DIGITAL\Digimon World 4\` contém o `.rvz`, um Dolphin portátil (Qt 6.8.3), uma pasta `.redigital\` (installed.json, manifest.json, manifest.json.sig) e `MMO\LEIA-ME.txt`, que descreve uma futura "ponte .NET" para ler/escrever memória do Dolphin, não implementada. Origem desse pacote: DESCONHECIDO (perguntar ao responsável).
- O DolphinTool em modo portátil pode ter escrito em `Dolphin\User`. Não verificado. O `.rvz` não mudou (hash igual).

## Skills, plugins e conectores (2026-10-24)

- Skills na conta: docs, docx, pdf, pptx, xlsx, google-workspace, computer-use, chrome-browser, built-in-browser, deep-research, skill-creator, import-memory, morning. Nenhuma de engenharia reversa, formatos de console, 3D, áudio ou Godot.
- Plugins: nenhum. Conectores de organização: nenhum.
- Skills locais propostas (a criar após aprovação): `iso-safe-inventory`, `binary-format-probe`, `console-asset-pipeline`, `console-audio-pipeline`, `godot-dotnet-client`, `fidelity-compare`.
