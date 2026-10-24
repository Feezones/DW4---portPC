# Inventário do disco

Auditado em 2026-10-24 via Remote Control, somente leitura. Hash do arquivo igual antes e depois.

## Arquivo

| Campo | Valor | Origem | Confiança |
|---|---|---|---|
| Caminho | `C:\Jogos\RE-DIGITAL\Digimon World 4\Digimon World 4 (USA).rvz` | responsável | FATO |
| Tamanho | 571.417.712 bytes | `Get-Item` | FATO |
| SHA-256 (.rvz) | `70669476EA96E34C79E1CD9634BE02D534E1B2E1D64AB07004984CE2C35BC10D` | `Get-FileHash` | FATO |
| MD5 (.rvz) | `0611747E70130BF9DAB3578494B8E079` | `Get-FileHash` | FATO |
| Assinatura | `52 56 5A 01` ("RVZ\x01") | leitura dos primeiros 64 bytes | FATO |
| Datas | criação 25/12/1996 (provavelmente herdada do pacote), modificação 18/10/2026 | `Get-Item` | FATO |
| Atributos | Archive, não somente leitura | `Get-Item` | FATO |

## Disco (DolphinTool header)

| Campo | Valor | Confiança |
|---|---|---|
| Plataforma | GameCube (Game ID começa com `G`; Dolphin leu como disco GC) | FATO |
| Game ID | `GDJEB2` | FATO |
| Nome interno | Digimon World 4 | FATO |
| Região / país | NTSC-U / USA | FATO |
| Revisão | 0 | FATO |
| Compressão RVZ | Zstandard nível 19, bloco 131072 | FATO |

## Hashes do disco descomprimido (DolphinTool verify)

| Algoritmo | Valor |
|---|---|
| CRC32 | `9e152cbc` |
| MD5 | `65c7b4887d21efd526a69ff060f631b5` |
| SHA-1 | `6a2aa8272538521531c803abf7f86dd430f9ad90` |

DolphinTool: "Problems Found: No". Comparação com o Redump: **pendente** (o site bloqueia leitura automática; conferir manualmente na página do disco GDJEB2).

## Comandos usados

`Get-Item`, `Get-FileHash -Algorithm SHA256/MD5`, leitura de 64 bytes com `[System.IO.File]::Open(f,'Open','Read','ReadWrite')`, `DolphinTool header -i`, `DolphinTool verify -i` e `verify -a md5`. DolphinTool usado: o do pacote em `C:\Jogos\RE-DIGITAL\Digimon World 4\Dolphin\`.

## Conteúdo do disco (extração de 2026-10-24)

Comando: `DolphinTool extract -i <rvz> -o D:\dw4-work\extracted -q` (exit 0). Cópia de trabalho em `D:\dw4-work\extracted\` no PC; nada disso vai para o Git. O `.rvz` foi marcado somente leitura (`attrib +R`) e o SHA-256 conferiu antes e depois.

**FATO:** o sistema de arquivos tem só 4 arquivos (`fst.bin` de 99 bytes). Total extraído: 9 arquivos, 1.209.321.634 bytes. Quase todo o jogo (1.203.879.936 bytes, 99,5%) está dentro de dois contêineres AFS.

| Arquivo | Bytes | 4 primeiros bytes | Interpretação | Confiança |
|---|---|---|---|---|
| `files/ndmw.afs` | 692.326.400 | `AFS\0` | Contêiner AFS; ver `formatos/afs.md` | HIPÓTESE forte |
| `files/area.afs` | 511.553.536 | `AFS\0` | Contêiner AFS; nome sugere áreas/mapas | HIPÓTESE |
| `sys/main.dol` | 5.196.512 | `00 00 01 00` | Executável DOL (PowerPC); 1ª seção de texto no offset 0x100 | HIPÓTESE forte |
| `sys/apploader.img` | 121.784 | `2004/05/20` | Apploader do SDK Nintendo; a data é do apploader, não do jogo | FATO (formato documentado) |
| `files/ndmw.igb` | 107.527 | `00 01 00 00` | Possível formato IGB (Intrinsic Alchemy); ver `formatos/igb.md` | HIPÓTESE fraca |
| `sys/bi2.bin` | 8.192 | `00 00 00 00` | Informações de boot (bi2) | FATO (estrutura padrão GC) |
| `files/opening.bnr` | 6.496 | `BNR1` | Banner GameCube NTSC (ícone/título no menu do console) | FATO (assinatura documentada) |
| `sys/boot.bin` | 1.088 | `GDJE` | Cabeçalho do disco, Game ID GDJEB2 | FATO |
| `sys/fst.bin` | 99 | `01 00 00 00` | Tabela do sistema de arquivos (4 entradas) | FATO |

Lista completa com SHA-256, 16 bytes de cabeçalho e entropia: [`arquivos.csv`](arquivos.csv).

## Pendente

1. Ler as tabelas internas dos dois AFS (offset, tamanho e nome de cada entrada) e validar a hipótese de formato.
2. Classificar as entradas dos AFS por assinatura e entropia.
3. Investigar `ndmw.igb`.
4. Analisar o cabeçalho de `main.dol` (seções, endereços) só por leitura.
