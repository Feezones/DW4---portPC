# Fase 4: áudio (levantamento)

Leitura estática em 2026-10-24. FATO = lido nos dados ou no código; HIPÓTESE marcada.

## Onde está

- Músicas e ambientes: ndmw.afs #0..#78, 79 CRI ADX v4 soltos (sem nomes). #0..#63 e #78 estéreo 48 kHz; #64..#77 mono (quase todos 24 kHz). 61 têm loop no cabeçalho (0x24 ligado, 0x28 início, 0x30 fim, em amostras). area.afs só tem o áudio dos 3 Sofdec.
- Efeitos curtos: 527 bancos .sfp (86 nomes: allse, COMMON_BLST, dng00, e_nume…), guardados como memória (beCriSfpData) nos IGB de fase e no _com (#898 = allse). Formato CRI SNDF 2.09a "gcax" (blocos DTPK, TBLD, PCMD) com amostras DSP ADPCM do GameCube a 24000 Hz. Falta um leitor (estrutura da TBLD e índice do cue).

## Como o jogo escolhe

- Lua: be:sd("SOUND", nil, −1, "PLAY", "NOME") (também PLAYatPAUSE e BGMSTOP). dng1: first.lua toca "AM_DUNGE" e "DG_CAVE1"; torre: "DG_TOWER".
- O IGB da fase tem um beSoundInfo com os arquivos ("sound/sfp/GC/x.sfp" e "[afs.índice]", ex.: "[0.35]" = ndmw #35). Cada beSoundData: nome, S4 = tipo (1 música, 2 ambiente, 3/4 efeito por stream), S5 = índice na lista, Int6 = cue (−1 = stream ADX; senão 0xA9GGCC00, GG = banco, CC = cue), S7 = canais, S8 = volume (unidade DESCONHECIDA).
- main.dol 0x802A9828 resolve o nome ("[p.f]" → patid/fid; senão igAdxAfsFileList do ndmw.igb; senão ADXT_StartFname) e chama ADXT_StartAfs (0x8029BDDC).
- Pares nome → ADX: 81 (tabela na cópia de trabalho). dng1: DG_CAVE1 = #35, AM_DUNGE = #6. Chefe BT_SBOSS = #30; vitória BT_WIN03 = #33. #31 e #64 não aparecem em fase (talvez título ou carregamento).
- Sons por ação do personagem: beSeInfoData("p_girumon") → beSeData(ação, som, atraso) no IGB da fase (VATCS1 = PC_SUBURI01, KATCS1 = plCHARGE, RUNNS1 = foot_lp1 com 6, SDMG* = PC_DMG01, DEADA1 = EFX_DOWN_02 com 90). Unidade do atraso: HIPÓTESE quadros.

## Port

- `dw4 adx-wav` (decodificador próprio em `tools/DW4.Formats/Audio/Adx.cs`) grava WAV com o loop num chunk smpl. A fatia toca #6 e #35 em loop.
- `.sfp` (FATO, `tools/DW4.Formats/Audio/Sfp.cs`): DTPK (+0C tamanho, +1C PCMD); TBLD com 9 ponteiros em 0x58 ([3] cues, [4] vozes, [8] amostras). Amostras: registros de 0x44 (offset relativo ao PCMD, endereços de loop e fim em nibbles, 16 coeficientes, ps/hist, flags com 0x400 = loop, taxa). Cues: u8 nº de grupos − 1; por grupo {0xA9GG, offset}; registros "c0 df VV vol pan ?" → voz VV → amostra (+03 do registro de voz). Decodificação DSP ADPCM idêntica ao vgmstream em 7 amostras.
- beSoundData por slot: [nome, Int3, S4 tipo, S5 índice na lista do beSoundInfo (campo 7), Int6 cue, S7 canais, S8 volume, S9]. beSeData: [ação, som, atraso].
- `dw4 igb-sons` grava um WAV por cue (A9GG_CC.wav) e o JSON das tabelas; a fatia toca os efeitos por ação. Pendente: quem para os sons em loop (passos, carga), volume S8 e o volume/pan do cue.
