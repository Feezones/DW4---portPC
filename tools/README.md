# Ferramentas

| Projeto | Conteúdo |
|---|---|
| `DW4.Formats` | Leitores de formato, sem dependências externas (.NET 10) |
| `DW4.Tools` | CLI `dw4` |
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
