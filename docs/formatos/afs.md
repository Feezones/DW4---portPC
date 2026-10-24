# AFS (`files/area.afs`, `files/ndmw.afs`)

**Confiança: FATO** para a estrutura do contêiner (validada em 2026-10-24, leitura em PowerShell puro na cópia de trabalho). O conteúdo das entradas é tratado em documentos próprios.

## Estrutura validada

| Campo | area.afs | ndmw.afs |
|---|---|---|
| Tamanho do arquivo | 511.553.536 | 692.326.400 |
| SHA-256 | 2280CFE9…2BD0D371 | C5C2B015…8CC0BCCC |
| Magic (0x0) | `AFS\0` | `AFS\0` |
| N entradas (u32 LE em 0x4) | 1382 | 973 |
| Tabela (offset, tamanho) a partir de 0x8 | termina em 0x2B38 | termina em 0x1E70 |
| 1ª entrada | 0x3000 | 0x2000 |
| Fim da última entrada | 0x1E7CA4F0 | 0x294350D2 |
| Par extra após a tabela → tabela de nomes | 0x1E7CA800, 66.336 B (= 1382 × 0x30) | 0x29435800, 46.704 B (= 973 × 0x30) |

Verificações (todas passaram nos dois arquivos):

- Offsets estritamente crescentes.
- Todos alinhados a 0x800.
- Nenhuma entrada passa do fim do arquivo, nenhuma sobreposição, nenhuma entrada de tamanho 0.
- Lacunas entre entradas: 0 a 2042 bytes, só preenchimento até o próximo múltiplo de 0x800.

## Tabela de nomes (registros de 0x30 bytes)

- 32 bytes de nome ASCII, 6 × u16 (ano, mês, dia, hora, minuto, segundo), 1 × u32.
- Datas todas válidas, entre jan/2004 e mar/2005.
- O u32 final repete a palavra de índice i do cabeçalho (registro 0 = N, registro 1 = offset0, …). Variante conhecida de AFS. **FATO** (observado); significado: sem uso aparente.
- **Os nomes têm só 4 caracteres** (resto zerado) e não são únicos. Funcionam como prefixo de categoria, não como nome de arquivo. **FATO.**

## Categorias (prefixo de nome)

| AFS | Prefixo | Entradas | Bytes |
|---|---|---|---|
| area | Load | 686 | 84.591.365 |
| area | stag | 294 | 126.937.400 |
| area | dngq | 168 | 76.149.240 |
| area | dng3 | 84 | 37.374.308 |
| area | dng1 / dng2 / dng4 | 49 cada | ~21,8 MB cada |
| area | AEU_ / AJP_ / AUS_ | 1 cada | ~40 MB cada |
| ndmw | soun | 79 | 273.086.732 |
| ndmw | stag | 273 | 143.362.179 |
| ndmw | dngq | 194 | 107.263.171 |
| ndmw | _com | 139 | 26.521.407 |
| ndmw | dng3 | 103 | 49.867.229 |
| ndmw | dng1 / dng2 / dng4 | 61 / 62 / 61 | 27,8 / 30,1 / 33,0 MB |
| ndmw | BecT | 1 | 306.654 |

HIPÓTESE (pelos nomes): `dng1`–`dng4` = quatro dungeons, `dngq` = dungeons de missão, `stag` = estágios/áreas, `soun` = som, `_com` = recursos comuns, `Load` = telas ou dados de carregamento. Nada disso foi confirmado.

## Inventário por entrada

`docs/inventario/afs_area.csv` e `afs_ndmw.csv`: índice, offset, tamanho, nome, 16 bytes de cabeçalho, ASCII, entropia dos primeiros 64 KiB. Gerados por `afs.ps1` (scratchpad da sessão no PC).

## Próximo passo

Um leitor de AFS em `tools/` (C#) com teste automatizado que reproduza exatamente estas contagens, offsets e somas.
