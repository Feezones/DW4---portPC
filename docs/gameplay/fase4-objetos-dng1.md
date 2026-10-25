# Fase 4: objetos de cenário da dng1 gerados pelos scripts

Levantamento estático em 2026-10-25 (exportações em `D:\dw4-work\fatia\objetos\`, fora do repositório).

## Modelos (FATO)

- Ficam no IGB da fase (#179) como `beModelCtrlInfo` KIND_OBJECT: `g_d1door`, `g_ddamagefloor`, `n_recorderdng`, `g_box`
  (caixa dos geradores GBOX), `x_box` (efeito), `g_dbutton1`. Nada no `_common`.
- Sem esqueleto nem `beAction2Info igAnimationDatabase`: são modelos rígidos animados por `igTransformSequence`, como os
  atores do HUD; exportados pelo mesmo `IgbHud` (`dw4 igb-objetos`). A raiz de `n_recorderdng` (#23790, igGroup
  "Z0SY90a0") e de `g_dbutton1` (#22939, "d_button1") é o igSwitch do `beAction2Info` (@7), não um igAttrSet com o nome.
- O `beModelCtrlNode2` de cada instância vem com T(15·n, 0, 0) no arquivo; a posição real vem do `GENERATE "V<…>"`.
- Sequências (igb-seq): `g_d1door` (SQ_SWON/SWOF, SQ_SILENTON/OF, labels BUTTON/CLEAR/BOSS), `g_ddamagefloor` (SQ_WAIT com
  labels OFF/ON/ON2/ON3, SQ_INTVL…), `g_box` (SQ_ENTR com FADESET IN 20 e JOINTSET 130–150; SQ_WAIT JOINTSET 30–150 LP;
  SQ_SDMG; SQ_DDMG com MUTEKI ON e DEAD01 FADESET OUT 15, JOINTSET 180–195, depois SQ_ERAS), `n_recorderdng` (SQ_WAIT, SQ_TALK).

## Geradores GBOX (FATO, generatorN #184 = H #183 = V #185)

GBOX-01..05 com posição global (−180, 40, 360), (−55, 40, −435), (−55, 40, −405), (15, 40, −435), (15, 40, −405), rotação 0;
inimigo linha 370 da tabela enemy (MODEL `g_box`, HPMAX 100, TSUYOSA 100, EXP 0); "direct"/"wipeout"; `_callLua`
gbox01..05.lua, que só fazem `FLGset(106 + n, 1)` (107..111). O start.lua não gera de novo uma caixa com a flag ligada.

## Colisão

- `g_box`: HIT igAABox (−7, 0, −7)..(7, 10, 7). `n_recorderdng`: (−3,5, 0, −3,5)..(3,5, 4, 3,5). Porta, piso, x_box e
  botão: HIT sem caixa. **FATO**
- Quem bloqueia a passagem nas portas é o **doorwall**: `beHitLandModelInfo` #192 → lista #200 [doorwall01..03] ↔ geometrias
  de colisão #8, #204, #207 sob o igSwitch #132 da raiz de colisão #1862 "BG01". Caixas 34 × 100 × 10 nas três portas.
  `sd HITLANDMODEL doorwallNN 0 "OFFHIT"` desliga o filho do switch. **FATO**; **HIPÓTESE**: a colisão exportada da raiz 1862
  hoje tem as três paredes sempre ativas.
- Piso de dano: sem colisão; o dano é o HITBOX "Set" 70112 (17 × 1 × 17) no SQ_WAIT/ON (**HIPÓTESE**: eneatk linha 112).

## Scripts (FATO, conferidos no lua-trace)

- `hit_g_box.lua`: `MDLsetDMGTurn(0)`; se `funcTechWazaHitCheck() ~= 1` (golpe fora de 40040..40099): `MDLaddHp(-100)` e
  `MDLsetSeq("SQ_DDMG")`. A caixa quebra com um golpe físico.
- Portas por gatilho de área + onda: `matg03` (jogador na área e FLG 102 desligada → `GENERATER START "GENE-03"`, saiu →
  `STOP`); `gene03.lua` ao vencer a onda: porta 0 `SQ_SWON` com atraso 30, `HITLANDMODEL doorwall01 OFFHIT` 45, `FLGset(102, 1)`.
  `matg07`/`gene07.lua`: portas 1 e 2 e FLG 106. O start.lua abre direto (SQ_SILENTON + OFFHIT) as portas cujas flags já estão ligadas.
- Piso: `matt01`/`matt02` ligam (`SETLABEL "SQ_WAIT/ON"`) os pisos 0–1 / 2–3 com o jogador na área e desligam ao sair.
  Ciclo ON: aviso (JOINTSET 40–70), HITBOX 70112 por 120 quadros, 180 desligado, repete.
- `hit_n_recorderdng.lua` (gravador): conversa, trava controle/inimigos, `FLGset(291, 1)`, gera o mapa `j_map101b1` e a
  mensagem "Open Map / Humid Cave B1".

## No port (2026-10-25)

`src/DW4.Client/Fatia.Objects.cs`: GENERATE cria o objeto na posição/rotação do script; comandos `SQ_*` e `SETLABEL` tocam a
primeira ACTION (JOINTSET) da sequência; GBOX gera a caixa com corpo 14 × 10 × 14 e HP 100; golpe físico roda o
`hit_g_box.lua` original e, com HP ≤ 0, o `gboxNN.lua`. Ainda faltam: gatilhos de área (`mat*`, `MATisPlayer`), ondas dos
geradores, doorwall separado na colisão, dano do piso e o gravador.

## Gatilhos de área e geradores (2026-10-25)

- **FATO** Volumes `beMatCtrlInfo` → `beMatCtrlData` (nome, igAABox) no #179: `matt01/02` (#429), `matz01/02` (#6595),
  `matg01..07` (#6605). O script é `"<volume>.lua"` (`0x80303870`); o motor o roda quando muda quem está dentro (entrada ou
  saída), não a cada quadro (`0x803037D0..78`). **HIPÓTESE forte**: `MATisPlayer()` = 1 na entrada; `MATsetRun(0)` desliga.
- matg01..07 disparam GENE-01..07 (`START`, `STOP` na saída das salas com onda, `STOPREVERS` nas salas que ficaram para trás);
  o número extra em `START "GENE-07" 1` é atraso de 1 quadro. matt01/02 ligam os pisos; matz01/02 abrem a janela de troca de
  fase (`NEXTSTAGE "stag0101"` / `"dng101b2"`).
- **FATO** GENE-01..07 (#184): todos Numemon (linha 13), "direct"/"wipeout"; GENE-03 e 04 = 4 no total e 4 ao mesmo tempo
  (1 jogador), GENE-05 = 2, GENE-06 e 07 = 5; posição por vaga (ponto + deslocamento, sem raio aleatório).
- **HIPÓTESES** na fatia (`src/DW4.Client/Fatia.Enemies.cs`): START repõe um por quadro até o máximo vivo e o total; STOP guarda
  o progresso; STOPREVERS some com os vivos e zera o progresso; o `_callLua` roda quando o total foi gerado e todos morreram.
- Dados para a fatia: `geradores.json` (`dw4 igb-geradores`) e `gatilhos.json` (`dw4 igb-gatilhos`), fora do repositório.

## Sequências dos objetos e piso de dano (2026-10-25)

- `src/DW4.Gameplay/ObjectSequencer.cs` executa as sequências dos objetos inteiras (antes só a primeira ACTION). Comandos e
  valores são **FATO** (igb-seq #179). **HIPÓTESES** de semântica, pelo fluxo dos dados:
  - cada sequência guarda um rótulo (LABEL); começar a sequência executa só o bloco desse rótulo (sem rótulo: o primeiro);
  - `LABELCTRL <rótulo>` troca o rótulo guardado de SQ_WAIT (SQ_SWON do piso → ON; SQ_SWOF → OFF; porta CLEAR → ON2);
  - `WAITCHECK "Frame" n` e a duração do `HITBOX` usam a mesma unidade (quadros de 60 Hz): no piso, HITBOX 120 ↔ espera 120;
  - `JUMP` vai para outra sequência no rótulo guardado dela, o que fecha o ciclo SQ_WAIT/ON → SQ_INTVL → SQ_WAIT;
  - `FADESET IN/OUT n` muda a transparência em n quadros; `HIDE` esconde (porta aberta).
- **FATO** eneatk linha 112 (do código 70112 do HITBOX do piso): TYPE 2, TSUYOSA 5, sem elemento → E2PDAMAGE caso 2 = 5% do
  HP máximo do jogador, sem teste de acerto. **HIPÓTESES**: um acerto por caixa ligada (uma vez por ciclo), caixa nos eixos do
  objeto contra o corpo 5 × 7 × 5 do Guilmon; a reação usa a mesma faixa de dano dos golpes do Numemon.
- Teste sintético: "Objetos: ciclo do piso de dano" em `tools/DW4.Tests`.
- `MDLisLive(nome, índice)`, `MDLgetRamNo` e `BLTgetShootRamNo` (este, HIPÓTESE: sempre o jogador 0) ligados para o
  `hit_n_recorderdng.lua`.
