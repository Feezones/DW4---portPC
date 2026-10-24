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

## Pendente

1. Marcar o `.rvz` como somente leitura (`attrib +R`).
2. Listar o sistema de arquivos do disco (FST) com offset, tamanho e hash de cada arquivo → `arquivos.csv`.
3. Classificar arquivos por assinatura e entropia.
4. Analisar `boot.bin`, `bi2.bin`, `apploader.img` e o executável `main.dol` só por leitura de cabeçalho.
