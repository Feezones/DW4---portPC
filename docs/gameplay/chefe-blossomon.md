# Blossomon (chefe da dng101b2)

Relato do usuário (2026-11-03): o Blossomon não se move nem bate, e ao derrotá-lo as portas não abrem (jogador preso).
Os logs do pacote não tinham mais essa luta (os registros giram); reproduzi com o bot sem janela:
`DW4_FASE=dng101b2 DW4_INICIO=600,12,-240 DW4_BOT=1 DW4_NIVEL=30`.

## FATO (scripts e sequências do jogo)

- matg08.lua (sala 545..735 × −335..−145): demo "sbossbefore", fecha a porta g_d1door 4 (SQ_SWOF) e liga o doorwall05,
  `ENEMY e_blosso OFCTRL`, GENERATER START "GENE-08" (o Blossomon nasce em 640, 10, −240), janela de apresentação, e só depois
  `MODELCTRL e_blosso 0 ONCTRL` solta o chefe; barra i_bosshp10 com METERCTRL bshp10 SETMODELHP "e_blosso/I<0>".
- e_blosso: SQ_ENTR põe `LABELCTRL "BOSS"` nas SQ_DFORMC/F/I/T/D. SQ_DDMG/common → JUMP SQ_DFORMC; no rótulo BOSS:
  LDMG01, LDMG03, `LUACALL "ded"`, FADE01, ERDE03, SQ_ERAS.
- ded_e_blosso.lua: demo "sbossafter", BT_WIN03, SQ_ENJOY, g_d1door 4 e 5 SQ_SWON, doorwall05 e doorwall06 OFFHIT, volta o controle.
- hit_e_blosso.lua manda SETPERCENT ceil(HP/MHP × 100) na bshp10 a cada golpe e i_bosshp10 SQ_CLOSE quando o chefe morre.

## Causas no port e correções

1. Travado: o port tratava `ENEMY <modelo> OFCTRL` como "trava todos os inimigos" e ignorava `MODELCTRL <modelo> <n> ONCTRL`
   (só o ONCTRL sem nome destravava). Agora ENEMY/MODELCTRL com nome travam e soltam só aquele modelo (`_frozenModels`).
2. Portas: com HP 0 o port não seguia o JUMP do SQ_DDMG para o SQ_DFORMC e o chefe sumia sem o LUACALL "ded". Agora, com HP 0,
   segue para as SQ_DFORM* (e o ded_e_blosso.lua roda).
3. Barra: o golpe num chefe e_* não roda o hit_e_blosso.lua (só os f_* rodam). O port agora faz o que ele faria: medidor bshp10 da
   arte i_bosshp10 pelo HP, e SQ_CLOSE quando o chefe morre.
4. Só depuração: o bot não mira pelo cursor do mouse (no bot sem janela o golpe ia para o lado e nunca acertava).

Conferido pelo bot: o chefe anda (SQ_WALK/SQ_WALKB), bate (SQ_ATCK1/ATCK2, derrubou o bot no nível 10), morre pelo SQ_DFORMC,
roda o ded_e_blosso.lua e as portas 4 e 5 abrem. Capturas que fecham sozinhas: luta com a barra caindo e a saída aberta na demo.

## HIPÓTESE

- O medidor da barra usa a mesma conta do HUD do jogador (Hud.MeterTime); a fórmula do SETPERCENT no DOL não foi lida.

## DESCONHECIDO

- Se outros chefes e_* (ex.: e_mecha4bs) têm algo no hit_<modelo>.lua além da barra que o port não faz.
