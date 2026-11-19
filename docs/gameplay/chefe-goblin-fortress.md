# Mecha Rogue da Goblin Fortress (staggf03, Rotating Hall)

Pedido do usuário (2026-11-03): "mecha rogue type 3 em goblin fortress". A Goblin Fortress é stagGF01..04 (o matz01 da
stag0101 leva ao stagGF01; "You need two ID Keys to enter the Goblin Fortress"). O Mecha Rogue fica na staggf03: é o Leomon
convertido (e_mecha4, linha 57 da enemy, HPMAX 333). Bot: `DW4_FASE=staggf03 DW4_BOT=1 DW4_ALVO=objetos DW4_DEPOIS=5,155`.

## FATO (scripts)

- Entrada 0 em (5, 10, −140). matf00 fecha o portão de trás (g_breakinggatez05). matg00: n_leomon SQ_DEMOWAIT e GENE-00
  (o Mecha Rogue nasce em 5, 12.5, 35, atrás do portão g_covergatez05).
- Console g_covercnsz05 (corpo −16..−6 em X da raiz): golpe → mensagem mswk64006/7, SQ_SWOF e o portão abre (hitcovergatez05 OFFHIT).
- mate00: ENEMY OFCTRL, demo "MRogue1", campo de energia SQ_SWON, `MODELCTRL e_mecha4 SQ_DMENTR` (rótulos NORM, queda sem
  "ded"), ONCTRL, barra i_bosshp18 / bshp18.
- Vitória: o GENE-00 vencido roda o gene00.lua: elevador g_liftz05 SQ_SWON, hitliftz05 OFFHIT, FLGset.
- math00 (no elevador): demo "leomon"/"leomon2", Leomon volta a si (SQ_KYRO, falas mswk30101..), NOGATEDISK 0, SQ_EXIT,
  NEXTSTAGE stagML02 entrada 1 (sala do comandante).

## Port

- Funciona com as correções desta rodada (trava por nome, ordem ENEMY/MODELCTRL por nome, CTRL "toWait" do SQ_ATCK2/ATCK4,
  barra pelo HP). Conferido pelo bot do começo ao fim: console → portão → apresentação → luta (SQ_ATCK1/2/3, SQ_GUARD,
  SQ_SDMG) → queda (SQ_DFORMC) → barra fecha → elevador → cena do Leomon → stagML02 entrada 1.
- Só o bot mudou: bate também parado a menos de 25 de objetos com caixa de corpo fora da raiz, ignora objetos já com HP 0, e
  `DW4_DEPOIS="x,z"` anda até um ponto quando não há inimigo (para chegar à cena depois da luta).
- Capturas que fecham sozinhas: luta com a barra pela metade e a cena do Leomon no elevador.

## DESCONHECIDO

- Qual flag o FLGset do gene00.lua liga.
- O texto das falas do Leomon não ficou numa captura (o bot passa as falas a cada 0,5 s).
