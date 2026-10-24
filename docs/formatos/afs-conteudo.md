# Conteúdo das entradas dos AFS (classificação inicial)

Classificação pelos primeiros 16 bytes e pela entropia dos primeiros 64 KiB. Nenhuma entrada foi extraída nem decodificada ainda.

| Grupo | Onde | Entradas | Evidência | Hipótese | Confiança |
|---|---|---|---|---|---|
| Vídeo | area: AEU_, AJP_, AUS_ | 3 (~120 MB) | Começam com `00 00 01 BA` / `00 00 01 BB` (MPEG-PS); o executável tem Sofdec (MWSFD/GC 3.33b) | Vídeos de abertura Sofdec (.sfd) em três regiões | FATO para AUS_ (#2): MPEG-1 640×480 a 29,97 fps + áudio ADX 48 kHz estéreo de 1:04,0, separados com `dw4 sfd-demux` |
| Áudio | ndmw: soun | 79 (273 MB) | `80 00` + offset de cabeçalho + `03 12 04` + canais (01/02) + taxa `BB80` (48000) ou `5DC0` (24000) | CRI ADX (o executável tem ADXT/GC 9.31b) | HIPÓTESE forte |
| Mapa/área? | area: dng*, dngq, stag | ~600 | 1º u32 entre 0x12xx e 0x1Exx e padrão fixo `xx xx 00 00 Ax 00 00 00 1E 11 00 00 43 00 00 00`; entropia 4,1–4,7 | Arquivos IGB do Alchemy (ver `igb.md`) | HIPÓTESE |
| Load | area: Load | 686 | Assinaturas `C4 09 00 00` (460) e `A4 09 00 00` (184); entropia 5,40–5,48; tamanhos quase fixos | Desconhecido (talvez imagens de carregamento ou dados por cena) | DESCONHECIDO |
| Pequenos fixos | ndmw: dng1 etc. | 98 | `90 00 00 00 06 00 00 00 …`, sempre 3270 bytes | Tabela ou descritor por sala | DESCONHECIDO |
| Grandes de cenário | ndmw: dng*, dngq, stag | ~100 (2–9 MB) | 1º u32 entre 0x6000 e 0x9F00; entropia 3,0–4,2 | Candidatos a modelos/texturas de cenário | HIPÓTESE fraca |
| Diversos | ndmw | ~260 grupos | 1º u32 varia (parece tamanho de cabeçalho, não magic) | — | DESCONHECIDO |

Observação: entropia de 3 a 5,5 sugere que a maioria dos dados não está comprimida nem cifrada. Isso é só um indicador.

## Ferramentas candidatas (não instaladas)

- MPEG-PS: ffmpeg (já disponível na nuvem) para validar uma amostra.
- ADX: vgmstream (código aberto) e ffmpeg (tem decodificador ADX). Validar uma amostra antes de qualquer conversão em lote.
- Demais grupos: análise manual de estrutura (ImHex ou script próprio).

## Amostras extraídas (Fase 1, 2026-10-24)

Extraídas com `dw4 afs-extract` para `D:\dw4-work\amostras\` (fora do repositório). Lidas só por cabeçalho e strings.

| Amostra | Tamanho | SHA-256 | Resultado | Confiança |
|---|---|---|---|---|
| area #0 AEU_ | 39.522.304 | 8DED12DA…7D99D88E29 | Sofdec (MPEG-PS): strings "SofdecStream", "Sofdec CRAFT/Console Ver.2.91", nome original `dmwxop6M.sfd`, 08/03/2005 | FATO (formato do contêiner) |
| ndmw #0 soun | 4.401.152 | 7A531251…9F7B28F794 | ADX "(c)CRI", 2 canais, 48000 Hz, 3.908.784 amostras (~81,4 s), provável loop | FATO (assinatura); duração calculada do cabeçalho |
| ndmw #64 soun | 36.864 | BA6DF3AE…E15DDF04 | ADX "(c)CRI", 1 canal, 24000 Hz, 60.094 amostras (~2,5 s) | FATO (assinatura) |
| area #3 dng1 | 447.581 | AE4455F2…A4925369 | IGB do Alchemy | FATO |
| area #696 Load | 130.288 | 2AE48038…ED951A94449 | IGB do Alchemy | FATO |
| ndmw #79 BecT | 306.654 | 5C11136C…D23306B4C1 | IGB do Alchemy | FATO |
| ndmw #80 dng1 | 3.270 | 6855B17F…D15161F6F6 | IGB do Alchemy | FATO |

Evidência IGB: os bytes 0x20–0x3F são idênticos aos de `ndmw.igb` (`56 06 00 00 30 00 00 00 DA FA 00 00 06 00 00 F0|B0 15 00 00 00 01 00 00 …`) e o arquivo começa com a tabela de tipos `ig*MetaField`. O 1º u32, que parecia "assinatura" variada, é um campo de tamanho do cabeçalho IGB.

Correção da Fase 0: os grupos "Load", "pequenos fixos" e "grandes de cenário" acima, antes DESCONHECIDOS, são IGB nas amostras testadas. A contagem total por tipo sai do comando `dw4 afs-classify`, que usa o u32 0xFADA em 0x28 como marca de IGB.

## Classificação completa (`dw4 afs-classify`, 2026-10-24)

| AFS | IGB | ADX | Sofdec (MPEG-PS) | Não classificadas |
|---|---|---|---|---|
| area (1382) | 1379 (390.475.196 B) | 0 | 3 (119.926.784 B) | 0 |
| ndmw (973) | 894 (418.276.854 B) | 79 (273.086.732 B) | 0 | 0 |
| **Total** | **2273 (808.752.050 B)** | **79** | **3** | **0** |

Cada categoria de nome tem um único tipo: `soun` = ADX; `AEU_`/`AJP_`/`AUS_` = Sofdec; todas as outras (`Load`, `stag`, `dng1`–`dng4`, `dngq`, `_com`, `BecT`) = IGB. **FATO** para a marca de cabeçalho; o conteúdo de cada IGB ainda não foi lido.

Consequência: modelos, cenários, telas e dados do jogo estão todos em IGB. Entender o IGB é o passo que destrava quase todo o resto.
