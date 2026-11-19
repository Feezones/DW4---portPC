# Fase 4: missões e progressão (levantamento)

Leitura estática dos IGB e dos scripts Lua em 2026-10-25. FATO = lido; HIPÓTESE marcada.

## Fases

- Índice: ndmw.igb (igAdxAfsFile: nome, AFS, índice e fases a que pertence), 98 fases. Bloco por fase no ndmw: [som/estático, principal (cena e Lua), câmera, câmera afastada, acmr_pack, generatorh/n/v]. Ex.: dng101b1 = #178 som, #179 principal, #180/#181 câmera, #182, #183..#185 geradores.
- Textos e tabelas por idioma no area.afs (eng/fra/ger/ita/jpn/spn/usa_pack por fase; dng101b1: #3..#9, o #9 é o USA). Tela de carregamento: area "loading*".
- Lobby principal: stagML11 (#821), destino de todas as saídas de masmorra.
- Campos: stag0001 (#607), stag0101..0104, stag0201..0205, stag0301..0304, stag0401/0403/0404, stagBS01..06, stagGF01..04, stagIS07..13/IS20, stagML02/11/12/13/14.
- Masmorras: dng1 (dng101b1 #179, dng101b2 #187, dng102b1..b3, dng103f1/f2), dng2 (dng204..206), dng3 (dng307..310), dng4 (dng411..412), dngq1..8 (variantes e/h/v).
- Transições: mat*.lua (ex.: matz00 no stag0101 → dng101b1). Saída: gatedisc.lua (SYSsetGateStage, NEXTSTAGE "stagML11"); restart.lua.

## Dificuldade

- SYSgetDifficulty() = 0/1/2 (N/H/V); cada fase tem generatorn/h/v. hard.lua e veryhard.lua ligam FLGs 406/407 e 501..507.
- gameStart.lua (#807) copia as FLGs permanentes da dificuldade (Normal 700.., Hard 741.., VHard 771..) para as de trabalho (1..6, 19.., 67..). HIPÓTESE: progresso guardado à parte por dificuldade.

## Missões

- quest01..06.lua (lobby, NPC n_digilfm): ligam FLGs, COUNTERset(2, 0), SYSctlFlagFolder("CLR", 11). questNNstop cancela.
- Objetivo: objeto g_000bcaps na masmorra (end_g_000bcaps.lua): lê COUNTER 2, desliga as FLGs da missão e volta à lobby.
- Recompensa: treasure01..08.lua pelo COUNTER 0 e a dificuldade (SYSctlBit "ADD" 100/500/1500, SYSctlDeviceFolder "SET" arma) e libera a evolução (ver fase4-levantamento-evolucao.md).
- Estado: FLGset/FLGisOn, COUNTERset/get, SYSctlFlagFolder. COUNTER 2 = missão ativa; COUNTER 0 = faixa da recompensa.

## Textos

- Lua: mswkNNNNN = "TALKFONTxx/font00/I<k>"; funcOpenMessage/funcOneMessage/funcCloseMessage.
- Falas nos *_pack.igb como beUnicodeObj (String@2 = chave, memória UTF-16LE); o usa_pack tem o inglês (ex.: mswk25387 "Obtained Long Sword a!").

## Desconhecido

- Significado de cada FLG; quem libera cada campo e masmorra; ordem exata da história; papel de stagIS/GF/BS/ML12..14; o que é o g_000bcaps.

## Consequência para o port

A progressão está quase toda nos scripts Lua, que chamam a API be:* do motor. Ver a decisão em docs/formatos/lua.md (executar os scripts originais numa VM Lua 4.0 ou traduzi-los).

## Começo de jogo e dificuldade (2026-10-25, FATO)

- Cadeia do original: stagIS08 (logos; logo.lua → NEXTSTAGE stagis09) → stagIS09 (filme de abertura; nextStage.lua → stagis10)
  → stagIS10 (título: TITLE2 RUN, telas do motor).
- gameStart.lua (stagIS10) roda a cada começo de jogo:
  - zera COUNTER 2 e as flags de trabalho (23..34, 36..66, 71..73, 82, 84..85, 100..319, 328, 334, 336..391, 409, 415..425,
    457..468, …);
  - liga itens da FLAGFOLDER pelas flags 518+;
  - copia as flags permanentes da dificuldade escolhida (blocos 700+) para as de trabalho (ex.: FLG 49 ← FLG 803 no Normal).
  - Ou seja, o progresso fica nas permanentes, e as de trabalho são refeitas a cada sessão.
- hostLoad.lua: FLG 77 → dificuldade máxima 2 (Very Hard); FLG 711 → 1 (Hard); senão 0 (só Normal).
- No port:
  - `Session.Begin` marca o começo de jogo, e a primeira fase roda a gameStart.lua na GAMERAM da vaga antes do boot.
  - A dificuldade vem de `GameRam.DifficultyMax`: personagem novo só pode Normal; ao entrar numa vaga com flags, o menu
    pergunta a dificuldade até o máximo.
  - Teste: "Jogo: gameStart.lua da stagIS10 …".
