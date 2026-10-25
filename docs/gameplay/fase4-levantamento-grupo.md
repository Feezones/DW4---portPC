# Fase 4: regras de grupo do original (levantamento)

Leitura estática do main.dol e dos scripts em 2026-10-25. Base de fidelidade para a party online (fase 6). FATO = lido; HIPÓTESE marcada.

## Por jogador × compartilhado (FATO)

- Mensagens: GETCOMMON(0), GETPLAYER(1), GETPLAYER1..4(2..5), GAMESTART(7, 0x8036EFF4: nome, ISUSE, RECALC, medidores), GAMEREMOVE(8, 0x8036F178), DEADRESTART(16), BKCHIPBITEXP(20).
- Lua: SYSisJoinPlayer(p) para 0..3; SYSgetPlayerNinzu() = GETCOMMON "NINZU". Jogador 1 = PLAYER; os outros = PLAYER2 (beNDMWMdlPlayer2).
- Por jogador: LEVEL, EXP, BIT, SKILL, UPCNT, DIGIMONNO/BASE_*, HP/MP, equipamento e pastas.
- Do grupo (GETCOMMON): FLAG (FLGisOn lê um vetor único; FLGisOn(1000) = NINZU ≠ 0), COUNTER, DIFFICULTY, NINZU, CAMERAMODE, chips de backup e GATESTAGENAME. Missão e progresso são do grupo.
- Lobby: até 4 jogadores (callshop.lua, mswkSpecial001).

## Escala (FATO)

- Gerador: PlayerData[nº de jogadores − 1] (0x802FD16C) dá _geneCount (total) e _geneMaxCount (vivos ao mesmo tempo). dng101b1: GENE-03/04 = 4/6/6/8 com máximo 4; GENE-06/07 = 5/7/9/12 com máximo 5; os outros iguais. Mais jogadores = mais inimigos no total, mesmo máximo vivo.
- HP e força do inimigo não dependem do NINZU (0x802FD118 lê HPMAX direto); a dificuldade troca a linha (Numemon 13/17/21).
- No FIGHTCALC, NINZU só entra no P2EEXPUP.

## EXP (FATO, P2EEXPUP 0x80373B48)

- Para cada jogador com ISUSE, HP ≠ 0 e nível ≠ 999: e = EXP·(EXPRITSU + 100)/100; NINZU 1 → ·100/2, 2 → ·140/3, 3 → ·160/4, 4 → ·200/5; ÷50 para quem matou, ÷100 para os outros. Caídos não recebem. Subir de nível enche HP e MP.

## Morte (FATO)

- Equipe inteira caída: todos voltam à lobby, chips de backup ficam onde caíram (textos do tutorial mswk67043..67050).
- BKCHIPBITEXP (0x80377F70): BIT inteiro vai para o chip; fora dos níveis 1 e 999, perde (NE(L−1) − NE(L−2))/5 de EXP sem descer de nível; o chip guarda a perda. Raise (técnica ou disco) no chip devolve o BIT e parte da EXP (quanto: DESCONHECIDO). Morrer de novo sem pegar o chip apaga o antigo.
- DEADRESTART: HP e MP cheios para os 4 slots ao voltar à lobby.

## Desconhecido

- Câmera com vários jogadores (alvo, limite de distância).
- Onde o jogo testa "todos caídos"; quanto o Raise devolve.
- Quem chama GAMESTART (tela de entrada); se FLAG/COUNTER são tocados ao entrar no meio da partida.

## Port

`src/DW4.Gameplay/Party.cs` (chip de backup, índice do PlayerData) e `DamageFormula.ExpShare`. Para a fase 6: o servidor guarda o estado do grupo (FLAG, COUNTER, DIFFICULTY) e cada cliente o seu jogador.
