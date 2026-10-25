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

## Câmera da entrada (2026-10-25)

- **FATO** first.lua (entrada 0): `CAMERA SETDEMOCAMERA "entry"`, espera `ISDEMOCAMERA "==0"`, música DG_CAVE1 com 120 quadros
  de atraso, espera `ISDEMOCAMERA "!=0"` e manda `SETBOXCAMERA`. Dados: beCameraCtrlInfo "entry" do acmr_pack (ndmw #182),
  181/60 s, fov horizontal 54,43°, exportados por `dw4 igb-camera-demo ndmw.afs 182 <fatia>\camera-demo`.
- No port: `src/DW4.Gameplay/CameraDemo.cs` lê os quadros e interpola; a fatia toca a demo no SETDEMOCAMERA e volta às câmeras
  por volume no SETBOXCAMERA. **HIPÓTESES**: ISDEMOCAMERA = 0 enquanto toca e 1 no fim; interpolação linear entre quadros
  (o recuo do original usa Bézier, diferença de poucas unidades); fov horizontal mantido em tela 16:9 (largura fixa).
- **FATO** pontos de entrada: bePoint01Info #115 → beGroupKeepList #114 → 2 beGroupKeep, cada um com igAABox e 4 igTransform
  (uma vaga por jogador). Grupo 0 (junto do matz01): (−300, 40, 431..452), olhando para −Z; grupo 1 (junto do matz02):
  (324..345, 24..16, −440), olhando para −X. PLAYERPOINT é a classe de mensagem desse objeto (GETGENERATEPOS/SETGENERATEPOS).
  **HIPÓTESE forte**: o n do funcStageChange vira SETGENERATEPOS n (grupo n, vaga k = jogador k); 0 = do campo/lobby.
  A fatia começa no grupo `DW4_ENTRADA` (padrão 0) lido de `pontos_entrada.json` (`dw4 igb-entradas`).
- **Pendente**: o SQ_DEMOWALK do jogador ainda não anda.

## Gravador e minimapa (2026-10-25)

- **FATO** (ndmw #857) a sonda de conversa é o HITBOX `Set 60000 1 1 (0,4,7) (3,3,3)` da ACTION `TALKWF`, primeiro passo de
  todo bloco de estilo do SQ_ATCK1: conversa sai do botão de ataque. O `hit_n_recorderdng.lua` usa `BLTgetShootRamNo`.
- **FATO** texto: `"TALKFONT01/font00/I<k>"` → k = posição na beUnicodeObjList do pacote (area #9: 113 itens; 113 dos 136
  pares mswk dos scripts conferem). `mswk65000` = item 9 = "Open Map / Humid Cave B1". Exportação: `dw4 igb-textos`.
- **FATO** minimapa: j_map101b1 (KIND_MAPBASE) e j_mapcur (KIND_MAPCURSOR) em unidades de mundo no plano XZ; projeção
  ortográfica 360 × 360 unidades num viewport 120 × 120 em (500, 52) da tela 640 × 480. MAPBASE (`0x803B3A18`) usa a média
  das posições dos jogadores (y = 0); MAPCURSOR (`0x803B3EE8`) soma a posição do jogador à do mapa. Nenhum script manda
  SQ_CLOSE ao mapa; SQ_OPEN termina em SQ_WAIT em laço.
- No port (`src/DW4.Client/Fatia.Recorder.cs`): a sonda é conferida na troca de ação do jogador; o gravador roda o script
  original; PLAYER/ENEMY OFCTRL/ONCTRL e GENERATER ONFREEZE/OFFREEZE travam e soltam; TEXTCTL TEXTSET/SWITCH mostram o texto;
  o minimapa aparece no canto com o mapa centrado no jogador.
- **HIPÓTESES** do port: volume de conversa 10 × 20 × 10 no gravador; ISEND = confirmar (ataque) apertado; ISSEQNAME = 0
  quando o modelo está na sequência; vista de cima com −Z para cima; o mapa fica até sair da fase.
- **DECISÃO DA FATIA**: janela de mensagem com fonte da Godot até exportar i_messwn e a fonte do jogo.
- **FATO** start.lua só gera j_map101b1 e j_mapcur com FLGisOn(291) == 1 (volta à fase depois de usar o gravador).
  `MDLisLive` dos dois responde pelo minimapa.

## Saídas e troca de fase (2026-10-25)

- **FATO** matz01.lua / matz02.lua: com `MATisAllPlayer() == 1` e a fila do DM vazia, `funcStageChange("stag0101", 2,
  "STCHGFONT00/font00/I<10>")` ("Goblin Pass") / `funcStageChange("dng101b2", 0)`. `g_StChangeKind = "stay"` nas duas.
- **FATO** funcStageChange (func.lua main.4): trava jogador e inimigos, `i_fadewn01 SQ_BHLFOUT30`, janela `i_zzstchgwb` +
  seleção `i_zzstchgwn` (texto em STCHGFONT00 com a cor do jogador), espera ISEND/ISSELECTNOW, `DMswtSD GETSELECT`;
  caso 0: PADINPUT DISABLE, fade `SQ_STCHANGE01` até `SQ_STCHANGELP`, `PLAYERPOINT SETGENERATEPOS "I<n>"` e
  `STAGECTL NEXTSTAGE "<fase>"`; caso 1 e padrão (−1): volta o fade, `MODELCTRL nil −1 ONCTRL`, PADINPUT ENABLE.
- **FATO** NEXTSTAGE é do beNDMWStageCtlInfo (`0x804E2124`); tratador não achado. **HIPÓTESE**: guarda nome e entrada,
  descarrega a fase e roda boot/start/first da nova (índice igAdxAfsFile do ndmw.igb). Fases destino: dng101b2 = ndmw #187
  (usa_pack area #16), stag0101 = ndmw #614 (usa_pack area #422).
- No port (`src/DW4.Client/Fatia.StageChange.cs`): DMswtEnd e DMcase(−1) como padrão (**HIPÓTESE**); janela e fade
  provisórios (DECISÃO DA FATIA); GETSELECT 0 = sim (**HIPÓTESE** pela ordem dos casos); NEXTSTAGE para dng101b1 recarrega a
  cena na entrada pedida, outras fases ficam para a Fase 5. A GAMERAM sobrevive à recarga.
- **FATO** menus: o menu de status/pausa é código nativo (classes beNDMWStatus* do main.dol, atores i_st*); a Lua só põe os
  textos (txt_status.lua). Seleção de técnica (i_wzslbase00) também é nativa.
