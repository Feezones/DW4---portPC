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

## Dano e dificuldade (relato de 2026-11-04: "2 tapas e morre" com Dorumon nível 4)

FATO (dados do jogo, conferidos byte a byte):
- A dng101b2 tem três geradores (ndmw #191, #192, #193) que só diferem na linha da tabela enemy: o GENE-08 (Blossomon) é 85 no
  #191, **82 no #192** e 88 no #193. O exportador e o port usam o generatorN (#192), então o port já está no **Normal**.
- Linha 82 (Normal): TSUYOSA 156, MAMORI 91, HP 275, EXP 88. Hard (85): TSUYOSA 3054, HP 2640. Very Hard (88): TSUYOSA 22475.
  Para comparar, o Numemon da mesma sala (linha 13) tem TSUYOSA 17: o chefe bate 9× mais forte, nos próprios dados.
- Golpes: SQ_ATCK1 = HITBOX 70012 (eneatk 12: TSUYOSA 130, crítico 10); SQ_ATCK2 (giro) = 70013 (eneatk 13: TSUYOSA 160, crítico 5).
- E2PDAMAGE TYPE 0 (DamageFormula.EnemyDamage) = TSUYOSA do inimigo × TSUYOSA do golpe / MAMORI do jogador, ±5% e crítico.
  Dorumon nível 4 sem armadura: MAMORI 110 + 90·30/100 = 137 e HP 170 + 110·30/100 = 203. Tapa: 156·130/137 ≈ 148; giro:
  156·160/137 ≈ 182; com crítico (~16% no tapa) quase o dobro. **Dois golpes matam no jogo original também.**
- O port não aplica multiplicador nenhum por dificuldade nem por grupo (a party só muda a quantidade de inimigos, PartyRewards.cs).

HIPÓTESE forte: código 7xxxx do HITBOX = linha xxxx da eneatk (o mesmo do Numemon, 70004).

## Movimento e fúria (corrigido)

FATO (sequências e hit_e_blosso.lua):
- Dois mapas de IA. MAP00 (calmo): perto (< 30) SQ_ATCK1 8/16 ou SQ_WALKB (recua); andar a 0,24. MAP01 (fúria): perto (< 32)
  o giro SQ_ATCK2 12/16; o SQ_ATCK2 termina com LABELCTRL NORM e AIMAP MAP00.
- O MAP01 só entra pela fúria: hit_e_blosso.lua conta golpes (g_blossoHitCount); do 3º em diante, o golpe que passa uma marca de
  10% do HP zera o contador e manda SQ_AGRDMG → LABELCTRL "SWON" "SQ_WAIT" → SQ_LDMG → SQ_WAIT/SWON → JUMP SQ_SWON (MAP01,
  rótulos ANGER: RUNN02 a 0,4, MUTEKI, explosão de contato 70000 por 30 quadros).

Erros no port:
1. A IA começava no 1º mapa do arquivo (MAP01): o Blossomon já abria a luta no modo fúria, com o giro de 160.
   HIPÓTESE (EnemyBrain): sem AIMAP no SQ_ENTR o motor começa no MAP00 (e_skulgrey, e_mecha4 e e_diabolo põem MAP00 no SQ_ENTR).
2. O golpe num chefe e_* não rodava o hit_e_<modelo>.lua: a fúria nunca vinha. Agora os e_* com LABELCTRL "BOSS" no SQ_ENTR
   (e_blosso, e_mammos, e_mecha4/5, e_skulgrey, e_anomalo, e_ldknight, e_diabolo) usam o script, como os f_*; o drop do gerador
   continua saindo.
3. O salto do rótulo guardado do SQ_WAIT (bloco SWON = JUMP SQ_SWON) não acontecia porque a IA do cliente não roda o SQ_WAIT.

DESCONHECIDO: onde o motor escolhe o mapa inicial (nenhum campo achado no beModelCtrlInfo).
