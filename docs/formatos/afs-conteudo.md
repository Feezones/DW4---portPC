# Conteúdo das entradas dos AFS (classificação inicial)

Classificação pelos primeiros 16 bytes e pela entropia dos primeiros 64 KiB. Nenhuma entrada foi extraída nem decodificada ainda.

| Grupo | Onde | Entradas | Evidência | Hipótese | Confiança |
|---|---|---|---|---|---|
| Vídeo | area: AEU_, AJP_, AUS_ | 3 (~120 MB) | Começam com `00 00 01 BA` (pack header MPEG-PS) seguido de `00 00 01 BB` (system header); entropia ~7,05 | Vídeos de abertura em três regiões (EU/JP/US), MPEG-PS | HIPÓTESE forte |
| Áudio | ndmw: soun | 79 (273 MB) | `80 00` + offset de cabeçalho + `03 12 04` + canais (01/02) + taxa `BB80` (48000) ou `5DC0` (24000) | CRI ADX | HIPÓTESE forte |
| Mapa/área? | area: dng*, dngq, stag | ~600 | 1º u32 entre 0x12xx e 0x1Exx e padrão fixo `xx xx 00 00 Ax 00 00 00 1E 11 00 00 43 00 00 00`; entropia 4,1–4,7 | Um único formato com cabeçalho de tamanho variável; provável dado de cenário | HIPÓTESE fraca |
| Load | area: Load | 686 | Assinaturas `C4 09 00 00` (460) e `A4 09 00 00` (184); entropia 5,40–5,48; tamanhos quase fixos | Desconhecido (talvez imagens de carregamento ou dados por cena) | DESCONHECIDO |
| Pequenos fixos | ndmw: dng1 etc. | 98 | `90 00 00 00 06 00 00 00 …`, sempre 3270 bytes | Tabela ou descritor por sala | DESCONHECIDO |
| Grandes de cenário | ndmw: dng*, dngq, stag | ~100 (2–9 MB) | 1º u32 entre 0x6000 e 0x9F00; entropia 3,0–4,2 | Candidatos a modelos/texturas de cenário | HIPÓTESE fraca |
| Diversos | ndmw | ~260 grupos | 1º u32 varia (parece tamanho de cabeçalho, não magic) | — | DESCONHECIDO |

Observação: entropia de 3 a 5,5 sugere que a maioria dos dados não está comprimida nem cifrada. Isso é só um indicador.

## Ferramentas candidatas (não instaladas)

- MPEG-PS: ffmpeg (já disponível na nuvem) para validar uma amostra.
- ADX: vgmstream (código aberto) e ffmpeg (tem decodificador ADX). Validar uma amostra antes de qualquer conversão em lote.
- Demais grupos: análise manual de estrutura (ImHex ou script próprio).
