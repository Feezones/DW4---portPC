# Ferramentas

| Projeto | Conteúdo |
|---|---|
| `DW4.Formats` | Leitores de formato, sem dependências externas (.NET 10) |
| `DW4.Tools` | CLI `dw4` |
| `DW4.Tests` | Verificador `dw4-testes`, sem pacotes externos |
| `scripts/` | Scripts PowerShell da Fase 0 (inventário independente) |

## Uso

```powershell
dotnet build tools/DW4.Tools
dotnet run --project tools/DW4.Tools -- afs-info    D:\dw4-work\extracted\files\area.afs
dotnet run --project tools/DW4.Tools -- afs-verify  D:\dw4-work\extracted\files\area.afs docs\inventario\afs_area.csv
dotnet run --project tools/DW4.Tools -- afs-extract D:\dw4-work\extracted\files\ndmw.afs D:\dw4-work\amostras 0 79
```

`afs-verify` compara o leitor C# com o inventário gerado pelo script PowerShell, que foi escrito de forma independente. É o teste de aceitação do leitor AFS: precisa terminar com 0 diferenças nos dois arquivos.

`afs-extract` grava no máximo 20 entradas por chamada e recusa qualquer pasta dentro de um repositório Git.

## Testes

```powershell
dotnet run --project tools/DW4.Tests                                  # só testes sintéticos
dotnet run --project tools/DW4.Tests -- D:\dw4-work\extracted\files    # também confere os dados do jogo
```

Os testes sintéticos montam em memória pequenos AFS, chunks Lua 4.0, PNG, imagens indexadas e programas MPEG. Não usam nada do jogo e podem rodar em qualquer máquina. Com a pasta do jogo, o verificador confere os resultados já medidos:
- contagens dos AFS;
- 2274 IGB sem problemas;
- 5291 chunks Lua;
- a geometria de area #598 e #3;
- os SHA-256 dos fluxos do vídeo AUS_.

Ele só lê e não grava nada.

Sem pacotes externos por decisão do responsável (2026-10-24). Se um dia o xUnit for aprovado, os mesmos casos podem ser portados para ele.
