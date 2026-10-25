# Fase 5: primeira luta de chefe (stagbs01, Apocalymon f_apocaly)

Entrada pela dng103f2: o matz02 chama `funcStageChange("stagBS01", 0)`. Exportar com
`dw4 fase-exportar <ndmw.afs> <area.afs> stagbs01 <fases>` e abrir com `DW4_FASE=stagbs01`.

## Levantamento

### Fase e arena

- **FATO** IGB principal #730, sons #729, geradores #731–733, usa_pack area #534, lh/ls_pack #734/735. Entrada 0 =
  (80, 20, 10), rotação −90.
- **FATO** start.lua gera: 16 `g_b01base1` + `g_b01base2` (cenário), 5 colunas `g_b01cpole` na borda do pedestal (y 20),
  5 pisos `g_b01dmgf1` no topo do pedestal (y 30,625) e 5 `g_b01dmgf2` no anel de baixo (y 11,25).
- **FATO** O chefe é a linha 106 da tabela enemy (HPMAX 666, TSUYOSA 224), gerado pelo GENE-00 em (0, 30, 0), em cima
  do pedestal (topo y 30, chão da arena y 20).
- **FATO** first.lua: abre o painel e a barra (`i_bosshp01` GENERATE `V<0,205,0>/SQ_OPEN`, `METERCTRL bshp01 SETMODELHP
  "f_apocaly/I<0>"`), liga os g_b01dmgf1 (SQ_SWON) 690 quadros depois e os g_b01dmgf2 entre 630 e 798.

### Pisos de dano

- **FATO** g_b01dmgf1: SQ_SWON → OFF2ON → SQ_ATCK1. O SQ_ATCK1 tem dois blocos: `modea` com 14 caixas 70117 (0..30
  quadros, em laço) e `modeb` sem caixas. SQ_MODEA/SQ_MODEB só trocam o rótulo (LABELCTRL) e nenhum script manda esses
  dois. SQ_ATCK2 pisca (caixas dos quadros 20 a 70, OFF2ON/ONSW01/ON2OFF/OFSW02). SQ_SWOF desliga.
- **FATO** eneatk 117: TYPE 2, TSUYOSA 15 (15% do HP máximo, 22 no Guilmon nv 1), MUTEKIHIT 1.
- **HIPÓTESE** Sem LABELCTRL, a sequência começa no primeiro rótulo (`modea`). Na prática o topo do pedestal machuca
  sem parar até as colunas caírem. É o mesmo padrão dos outros objetos com rótulos.

### Colunas (g_b01cpole, KIND_ENEMY)

- **FATO** hit_g_b01cpole.lua: técnicas não fazem nada; cada golpe físico tira 10; abaixo de 10 vai a SQ_DDMG. Cada coluna
  quebrada manda um g_b01dmgf1 para o SQ_ATCK2 (pisca). Quebrar as 5 faz: g_b01dmgf1 SQ_SWOF, f_apocaly SQ_PHASE2 e
  f_bit GENERATE.
- **HIPÓTESE** (não lido) HP inicial 50 (5 golpes) e caixa de corpo 14 × 40 × 14.

### Chefe

- **FATO** IA: MAP00 na fase 1 (WAIT, ATCK1, ATCK2, FIRE). O SQ_PHASE2 faz `AIMAP MAP01` e põe o rótulo `FLOAT` em todas
  as sequências; o MAP01 só tem WAIT, MAGIC e WALK.
- **FATO** SQ_ATCK1: ACTN02 com 70066 (quadros 25..55, à frente) e uma onda 70067 que avança de z 20 a z 60. SQ_ATCK2:
  `LUACALL "bit"` → bit_f_apocaly.lua, que gera o f_bit. SQ_MAGIC: 6 caixas 70070 em volta do corpo, dos quadros 24 a 118,
  agendadas **antes** do ACTN04 (animação curta) e do BLST03.
- **FATO** hit_f_apocaly.lua:
  - "PCBA" (projétil) faz o jogador ir para SQ_GUARDR.
  - Golpe: BLTisHitCheck; BLTisCritical → `crit`; `dano = BLTgetDamageValue(crit)`; os tempos de estado (sono, prender,
    veneno…); BLTcalcSkillUp(1, crit); MDLaddHp(−dano); MDLdspDamage(dano).
  - Faixa: `BLTgetDamageRank(MDLgetHp(), crit)`. O 1º argumento é o **HP que sobrou**, não o dano. 0..3 →
    SETLABEL `SQ_SDMG|MDMG|LDMG|DDMG/<atributo>` + MDLsetSeq.
  - Na morte (faixa 3): BLTcalcEXPUp, OFCTRL, i_bosshp01 SQ_CLOSE, cpole DDMG, f_bit SQ_EXIT, pisos desligados, câmera de
    demo "dead", MOVETRANS do chefe e dos jogadores, BT_WIN02, PLAYER SQ_ENJOY, g_dreturngate GENERATE `V<-60,20,0>` SQ_ENTR
    e SQ_POWERON, GBOX-01, FLGset 67 e COUNTERset(2, 0).
  - Em todo golpe: se `i_bosshp01` está vivo, `METERCTRL bshp01 SETPERCENT F<hp*100/MHp>`.

## Na fatia

`src/DW4.Client/Fatia.Boss.cs`:

- O golpe do Guilmon num chefe `f_*` que tem `hit_<modelo>.lua` roda esse script (`StrikeWithScript`). Os `be:BLT*` são
  respondidos pela `DamageFormula` no contexto do golpe (`Fight`): BLTisHitCheck → PlayerHits, BLTisCritical,
  BLTgetDamageValue → Physical, BLTgetDamageRank → Rank(dano deste golpe, HP do 1º argumento), BLTcalcSkillUp → skillup
  da arma, BLTcalcEXPUp → EXP, MDLdspDamage → número na tela. Tempos de estado e o resto: neutro (0) com aviso no
  BeHost. BLTgetAttributeStr = "common" (golpe físico; **HIPÓTESE**: técnicas dariam fire/ice/…).
- Rótulos por sequência (LABELCTRL da própria sequência, SETLABEL do script), AIMAP troca o mapa de IA e MOVETRANS move.
- Barra: `i_bosshp01` GENERATE/SQ_CLOSE e `METERCTRL bshp01` SETMODELHP/SETPERCENT. **DECISÃO DA FATIA**: o ator
  i_bosshp01 ainda não foi exportado, então a barra é uma ProgressBar vermelha no topo da tela.
- Colunas: `g_*` gerados por script com `hit_<nome>.lua` ganham corpo e HP (`MakeHittable`).
- `SequenceRunner.CarryHitBoxes` (ligado nos inimigos, desligado no jogador): caixas que ainda não acabaram passam para a
  ação seguinte, no tempo dela. Sem isso, as caixas do SQ_MAGIC se perdiam quando o ACTN04 terminava. Teste em
  `tools/DW4.Tests` ("caixas agendadas antes de uma ação curta…").

### Atalhos de depuração (só no build Debug; não são do jogo)

- **F8**: leva o Guilmon para perto do próximo alvo vivo (as colunas, depois o chefe), virado para ele, e mantém o cursor
  do mouse em cima dele para o golpe mirar ali.
- **F9**: deixa os chefes `f_*` com 1 de HP, para testar a cena da derrota.

## Conferido na janela (2026-10-25)

- 5 colunas quebradas → pisos do topo piscam e desligam, f_apocaly SQ_PHASE2, f_bit GENERATE (no log).
- Golpes no chefe: 6 de dano por golpe no nv 1, SQ_SDMG, a barra desce (99,1% → 94,1% em 6 golpes), HP 627/666 no HUD.
- O chefe acerta o Guilmon: eneatk 67 (onda do SQ_ATCK1), 191 de dano no nv 1 (o Guilmon tem 150).
- Pisos g_b01dmgf1: 22 por acerto (15%), uma vez por laço de 30 quadros.
- Derrota (com F9): SQ_DDMG, barra fecha, pisos desligam, BT_WIN02, SQ_ENJOY, +228 EXP, portal de volta e as caixas do
  GBOX-01 aparecem.
- Correção feita no caminho: o BLTgetDamageRank tratava o HP como dano, e o chefe morria no primeiro golpe.
- **FATO: conferido pelo usuário** (2026-10-25, jogando na janela, sem atalhos): venceu o Apocalymon e entrou pelo portal
  de volta, que levou ao lobby (`NEXTSTAGE "stagML11"`).

## Pendências

- f_bit: ver a seção abaixo.
- FLGset 67 / COUNTERset depois da derrota não foram conferidos na volta à dng.
- A câmera de demo "dead" e o MOVETRANS dos jogadores não foram conferidos quadro a quadro.
- Tempos de estado (BLTgetSleepTime, BLTgetPoisonTime…) seguem neutros.

## f_bit, os ataques da fase 2 (2026-10-25)

- **FATO** (scripts):
  - SQ_ATCK2 do chefe faz `LUACALL "bit"`, que roda bit_f_apocaly.lua. Se o f_bit não está vivo, o script manda
    `MODELCTRL f_bit 0 GENERATE "f_apocaly/I<0>/V<0,30,0>/V<0,90,0>/SQ_ENTR"` (dono, índice do dono, posição, rotação,
    sequência); se está vivo, manda o chefe de volta ao SQ_WAIT.
  - Quebrar as 5 colunas gera o f_bit já com SQ_PHASE2.
  - hit_f_bit.lua: −50 por golpe; com 50 ou mais de HP vai a SQ_SDMG, senão SQ_DDMG.
- **FATO** (#730):
  - O f_bit é KIND_OBJECT, sem esqueleto: um objeto rígido animado por JOINTSET.
  - Tem 24 sequências: SQ_ATK01..15, SQ_ATKALL, SQ_ATKROT, SQ_SMN01/02, SQ_ENTR (TRNS01), SQ_EXIT (TRNS02 → SQ_ERAS),
    SQ_PHASE2 (AIMAP MAP01 → SQ_ENTR), SQ_WAIT e SQ_WALK.
  - Os ataques são caixas 70069 (quadros 24..30, tamanhos 4..10) em leque a 50..70 unidades, em várias direções.
  - Mapas de IA: MAP00 (faixa 0: SQ_SMN01/SQ_SMN02) e MAP01 (faixa 0: SQ_ATK01..15 e SQ_ATKROT, sorteio uniforme). As faixas
    são de 1000², ou seja, sempre a faixa 0.
- No port:
  - O exportador inclui os KIND_OBJECT f_*.
  - O LUACALL roda "<nome>_<modelo>.lua" no contexto do modelo.
  - O GENERATE com dono cria o objeto preso ao chefe.
  - O ObjectSequencer agora respeita o quadro inicial das caixas (antes ligava tudo no quadro 0) e entrega o AIMAP.
  - O f_bit ganhou IA de objeto (EnemyBrain com os mapas dele, só parado em SQ_WAIT).
  - As caixas 70069 acertam o jogador com os atributos do chefe dono (eneatk 69).
  - O golpe do Guilmon roda o hit_f_bit.lua.
- **HIPÓTESES:**
  - A posição do GENERATE é no mundo (a do chefe no pedestal); assim as caixas, com y −3..−6, alcançam o jogador no topo.
  - O f_bit acompanha o chefe.
  - HP inicial 50 (cai num golpe, e o próximo LUACALL gera outro).
  - Não gira para o alvo.
