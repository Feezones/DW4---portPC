# Mecha Rogue (chefe da dng308b3)

Pedido do usuário (2026-11-03): "mecha rogue type 3". Nos textos do jogo só existem "Mecha Rogue 04" (e_mecha4, chefe da
dungeon 3, Vine Tunnel B3 = dng308b3) e "Mecha Rogue 05" (e_mecha5, dng411b3). HIPÓTESE: o pedido é o chefe da dungeon 3,
que são três Mecha Rogue 04 juntos. Bot: `DW4_FASE=dng308b3 DW4_INICIO=-680,12,-340 DW4_BOT=1 DW4_NIVEL=35`.

## FATO

- matg19.lua (sala −775..−585 × −495..−305): demo "sbossbefore", fecha g_d3door 7 e liga doorwall08, `ENEMY e_mecha4 OFCTRL`,
  GENERATER START "GENE-19" (3 de uma vez: −700/−660, −420 e −680, −390; HPMAX 1675 cada), apresentação, `MODELCTRL e_mecha4 ONCTRL`.
  Sem barra de HP de chefe (nenhum i_bosshp no matg19 nem no hit_e_mecha4.lua).
- IA: MAP00 perto SQ_ATCK1 / SQ_ATCK3; meio SQ_ATCK2 (recuo para os lados ±30°), passeio; longe SQ_FIRE e passeio.
  SQ_ATCK2 = recuo (ACTN05), AIMAP MAP01, LUACALL "wh1" "WHit", CTRL "toWait" e corrida sem fim (ACTN02).
  MAP01: perto SQ_ATCK4 (giro com 10 caixas 70048, depois AIMAP MAP02, "wh2" e outra corrida). MAP02: perto SQ_ATCK4, longe SQ_SWOF
  (volta ao MAP00). wh1_e_mecha4.lua → SQ_ATCK5 (giro parado); wh2_e_mecha4.lua → SQ_SWOF; bck_e_mecha4.lua → SQ_BEND (preso).
- Queda: rótulo BOSS (SQ_ENTR) → LUACALL "ded" em cada um; ded_e_mecha4.lua usa g_bossRest e só no último faz a demo
  "sbossafter", BT_WIN03, SQ_ENJOY, g_d3door 7 e 8 SQ_SWON, doorwall08/09 OFFHIT e FLGset.

## Port

- Faltava o CTRL "toWait": sem ele a corrida do SQ_ATCK2 só acabava e voltava ao SQ_ATCK2, e o SQ_ATCK4 nunca saía.
  HIPÓTESE implementada: depois do CTRL "toWait", a IA do mapa atual pode cortar a sequência com um golpe diferente (passeio e espera
  não cortam). Nos inimigos comuns o "toWait" só aparece no SQ_WALK.
- O resto já funcionava com as correções do Blossomon/Mammothmon.
- Conferido pelo bot: SQ_ENTR ×3, SQ_ATCK1/2/3/4, SQ_FIRE, SQ_GUARD, os três caem pelo SQ_DFORMC com LUACALL "ded"; no terceiro,
  vitória e portas 7 e 8 abertas. Captura que fecha sozinha mostra os três em luta.

## DESCONHECIDO

- SQ_ATCK5 e SQ_SWOF por batida na parede durante a corrida não aconteceram no bot (arena grande); o caminho é o "WHit" já usado.
- Se o pedido era o Mecha Rogue 05 (dng411b3) em vez deste.
