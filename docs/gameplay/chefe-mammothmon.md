# Mammothmon (chefe da dng102b3)

Pedido do usuário (2026-11-03): copiar o funcionamento do Mammothmon do jogo original. Bot sem janela:
`DW4_FASE=dng102b3 DW4_INICIO=-600,12,120 DW4_BOT=1 DW4_NIVEL=15` (perto) e `DW4_ALVO=longe` (fica a ~55 sem atacar).

## FATO (scripts e dados)

- matg11.lua (sala −755..−565 × 25..215): demo "sbossbefore", fecha a porta g_d1door 8 e liga o doorwall09,
  `ENEMY e_mammos OFCTRL`, GENERATER START "GENE-12" (nasce em −660, 10, 120), janela de apresentação, `ENEMY e_mammos 0 ONCTRL`
  e `ENEMY e_mammos 0 SQ_WALKIN` (começa pela investida); barra i_bosshp11 / bshp11.
- Mapas de IA: MAP01 → SQ_ATCK2 (rugido, põe MAP00). MAP00: perto (< 35) SQ_ATCK1 (presas) ou espera; meio (35..65)
  SQ_FIRE (sopro, caixas 70015 até 72 à frente) ou espera; longe (65..120) SQ_FIRE ou SQ_ATON; muito longe SQ_ATON.
  SQ_ATON põe MAP02 → SQ_WALKIN (ACTN02) → SQ_WALK (corrida RUNN02, invencível, caixas 70016, LUACALL "wht" "WHit") →
  SQ_ATCK3 (freada, MAP01). wht_e_mammos.lua (bater na parede na corrida): SQ_ATCK3 e CAMERA SHAKE.
  SQ_ATCK1 e SQ_FIRE terminam em SQ_SWON (volta ao MAP01). MAP03 existe mas nenhuma sequência põe ele.
- Queda: SQ_ENTR põe o rótulo BOSS nas SQ_DFORM*; LUACALL "ded" → ded_e_mammos.lua: demo "sbossafter", BT_WIN03, SQ_ENJOY,
  g_d1door 8 e 9 SQ_SWON, doorwall09/10 OFFHIT, FLGset, volta o controle.

## Port

- Já funcionava com as correções do Blossomon (trava por modelo, queda pelo SQ_DFORMC, barra pelo HP): anda, bate,
  ao vencer toca a vitória, abre as portas 8 e 9 e tira as paredes; a barra fecha.
- Faltava: `ENEMY <modelo> <n> SQ_…` era ignorado (só MODELCTRL valia), então a investida de abertura não saía. Agora ENEMY com nome
  vale como MODELCTRL para aquele inimigo.
- Conferido pelo bot: SQ_ENTR, SQ_WALKIN → SQ_WALK → SQ_ATCK3, SQ_ATCK2, SQ_FIRE (8 vezes de longe), SQ_ATCK1 e SQ_GUARD de
  perto, SQ_SWON; vitória com as portas abertas. Captura que fecha sozinha mostra a luta e a barra.

## HIPÓTESE

- O sopro não tem efeito próprio na ação BLST01 (só animação e caixas); se o original mostra gelo, vem de outro lugar ainda não lido.

## DESCONHECIDO

- SQ_ATON (mais de 120 do chefe) não apareceu no bot; o caminho até ele é o mesmo das outras escolhas da IA.
- Quais flags da história o FLGset do ded_e_mammos.lua liga (o número não sai nas strings).
