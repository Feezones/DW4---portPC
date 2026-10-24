# Fase 3: levantamento de movimento, câmera, ações e inimigos

Fonte: `dw4 igb-types` e `dw4 igb-dump` (commit d0b25f0) e leitura das listagens do `lua-scan`, rodados no PC em 2026-10-24 sobre a cópia de trabalho. Também foram lidas as strings do `main.dol`, só leitura, sem executar nada. Saídas brutas em `D:\dw4-work\fase3\`, fora do repositório.

Confiança: **FATO** = valor lido do arquivo. **HIPÓTESE** = leitura pelo nome ou pelo padrão. Nenhum valor foi comparado ainda com o jogo rodando.

## Onde está cada coisa

| Assunto | Onde | Confiança |
|---|---|---|
| Ações, movimento e hitboxes do Guilmon | ndmw #857 (`p_girumon`, `KIND_PLAYER`) | FATO |
| Fase dng1: cenário, colisão, atores, IA, scripts | ndmw #179 | FATO |
| Câmera da fase | ndmw #180 e #181 (`beCameraCtrlInfo` → `beCameraBoxData`), #182 (`beCameraDemoData`, 3,016 s) | FATO (classes); papel é HIPÓTESE |
| Geradores de inimigos e caixas | ndmw #183 (`beGeneraterInfo`, 12 `beGeneraterData`) | FATO |
| Nome interno do Guilmon | `girumon` | FATO |

O `hit_p_girumon.lua` do jogador é o mesmo chunk do `hit_p_agumon.lua`, ou seja, todos os jogáveis compartilham o script de acerto. **FATO**

## Guilmon (#857): classes de controle

| Classe | Qtd. | Campos lidos | Leitura |
|---|---|---|---|
| `beModelCtrlData` | 28 | String@2 = sequência (`SQ_WAIT`, `SQ_WALK`, `SQ_JUMP`, `SQ_ATCK1`–`3`, `SQ_GUARD`, `SQ_WAZA`, `SQ_ITEM`, `SQ_DEAD`, `SQ_SDMG`/`MDMG`/`LDMG`/`DDMG`…) | Máquina de estados por sequência (HIPÓTESE forte) |
| `beModelCtrlACTION` | 179 | String@2 = ação (`WAITG1`, `RUNNG1`, `JUMPG1`, `GURDD1`, `KATC*`, `HATC*`, `BATC*`, `VATC*`…) | Passos das sequências |
| `beAction2Data` | 183 | String@2 = nome (`DEADD1`, `GURDD3`…), ObjectRef@3 → `igNamedObjectList` | Definição de cada ação |
| `beAction2MOTIONSET` | 94 | String@2 animação, String@3 `RR`, Float@4 = 8, String@5 `OS` | Float@4 = quadros de mistura, `OS` = toca uma vez (HIPÓTESE) |
| `beModelCtrlMOVE` | 16 | String@2 tipo, Float@3, Float@4 | Ver tabela abaixo |
| `beModelCtrlPDMOVE` | 2 | `Move` 0,7 / 6; `Stop` 1 / 4 | Movimento pelo direcional (HIPÓTESE forte) |
| `beModelCtrlHITBOX` | 236 | String@2 `Set`/`AllClear`, Int@3 (10000, 20000, 30000, 60000), Float@4, Float@5, Vec3f@6, Vec3f@7 | Int@3 = tipo; Float@4/5 = quadro inicial e duração; Vec3f@6 = posição; Vec3f@7 = tamanho (HIPÓTESE) |
| `beModelCtrlMUTEKI` | 5 | `ON` −1, 18, 30, 180, 120 | Invencibilidade em quadros (HIPÓTESE) |
| `beModelCtrlWCHECK` | 7 | `TurnY`, `Landing`, `Atck01`–`03`, `Moving`, `Action` | Condições de espera |
| `beModelCtrlCTRL` | 3 | `onWalk`, `toWalk`, `toWait` | Transições andar ↔ parado |
| `beModelCtrlJUMP` | 6 | destino: `SQ_GUARD2`, `SQ_WAIT`, `SQ_ERAS`, `SQ_ATCKTM`, `SQ_CMBK02`, `SQ_HIDE` | Desvio de sequência, não pulo físico |
| `beAction2WAITCHECK` | 8 | `Frame` 15, 51, 60, 7, 20, 1, 16; `Motion` 0 | Espera por quadro ou fim de animação |

`beModelCtrlMOVE` (todos os 16, na ordem do arquivo; Float@3 / Float@4):

| Tipo | Valores |
|---|---|
| Move | 0,7 / 6 |
| Stop | 1 / 4 |
| Jump | 1,44 / 6,3 · 1 / 3,9 · 0,1 / 3,9 |
| Fall | 1,44 / 6,3 |
| Push | 0,5 / 2 · 2 / 7,8 · 0,5 / 4 · 0,5 / 3 · 2,3 / 11 · 0,7 / 9 |
| Back | 0,5 / 2 · 1,6 / 5 · 1,6 / 6 · 1,7 / 9 |

**DESCONHECIDO:** a unidade e o papel exatos de Float@3 e Float@4 (aceleração e velocidade máxima? por quadro a 30 ou a 60 Hz?). Isso só se fecha medindo o jogo.

## Fase dng1

- Geradores (#183), todos com `direct` e `wipeout`, Y = 40 (**FATO**): GENE-01 (−300, 40, 240), GENE-02 (−180, 40, 335) rot 180, GENE-03 (−180, 40, 60), GENE-04 (−40, 40, −120) rot −90, GENE-05 (40, 40, 120) rot −90, GENE-06 (180, 40, −100), GENE-07 (200, 40, −320). GBOX-01 a 05 têm posição vinda do script.
- Por número de jogadores (1 a 4) há um `beGeneraterPlayerData`. **FATO**
- Tabela de itens (`beGeneraterItemDataOne`, 161): categorias `weapon`, `conItem`, `insItem`, `armor1`, `abChip`. **FATO**; o significado de Int@3 e Int@5 é HIPÓTESE (valor e chance).
- Inimigo: os scripts da fase incluem `hit_e_nume.lua` e o efeito `e_nume`. **HIPÓTESE forte:** Numemon. O tipo não aparece nos campos do gerador.
- Início do jogador: `start.lua` chama `funcPlayerSet()` e `be:sd("PLAYER", nil, -1, "SQ_DEMOWAIT")`; `func.lua` usa `PLAYERPOINT` e `RESTART`. **HIPÓTESE:** o ponto é um nó da cena. Ainda não localizado.

## Câmera por volumes (#180 e #181)

Segunda rodada de leitura, mesmas condições. **FATO** salvo onde indicado.

- Grafo: `igSceneInfo` → `igSwitch` "BG01" → 3 `igTransform` → `igCamera` "cameraShape00".
- `beCameraCtrlInfo` ("Scene Graph", Bool@4 = 1) → `beCameraBoxData`, que liga: R@2 `igSwitch` "BG01"; R@3 `igGeometryList` (18); R@4 `igTransformList` (1, translação (0, 1, 0)); R@5 `igTransformList` "A" (18); R@6 `igTransformList` "B" (18).
- As 18 `igGeometry` não têm malha: cada uma é só um `igAABox`, o volume de ativação. O volume i usa a transformação i de A e a i de B.
- A = só translação (0, 0, d). B = rotação em X (inclinação), às vezes com guinada e com translação em Y.
- **HIPÓTESE forte:** cadeia alvo → (0, 1, 0) → B (pivô e inclinação) → A (recuo d) → câmera. Quando o jogador está dentro de um volume, a câmera usa a distância e a inclinação daquele volume. O volume 0, centrado na origem e alto, é o padrão.
- `#180` e `#181` têm as mesmas caixas e inclinações. Só as distâncias mudam: no #181 os volumes 2–16 usam 120 (no #180, 100 ou 110). **HIPÓTESE:** `aaaCameraL.igb` = modo de câmera afastada, escolhido por `SYSgetCameraMode`.
- Transição entre volumes (corte seco ou interpolação) e velocidade de seguimento: **DESCONHECIDO**, só medindo.

| Vol. | Caixa min → max | d (#180) | d (#181) | Inclinação B |
|---|---|---|---|---|
| 0 | (−5, 0, −5) → (5, 200, 5) | 135 | 135 | 55°, pivô +10 em Y |
| 1 | (−320, 40, 420) → (−280, 240, 460) | 90 | 98 | 45°, guinada ≈ 10° |
| 2 | (−360, 40, 340) → (−240, 240, 420) | 100 | 120 | 65° |
| 3 | (−320, 40, 220) → (−200, 240, 340) | 110 | 120 | 70° |
| 4 | (−200, 40, 140) → (−160, 240, 380) | 110 | 120 | 70° |
| 5 | (−240, 40, −20) → (−120, 240, 140) | 100 | 120 | 65° |
| 6 | (−200, 40, −140) → (−120, 240, −20) | 110 | 120 | 70° |
| 7 | (−120, 40, −180) → (40, 240, −60) | 100 | 120 | 65° |
| 8 | (−40, 40, −60) → (0, 240, 100) | 110 | 120 | 60° |
| 9 | (−40, 40, 100) → (120, 240, 140) | 110 | 120 | 70° |
| 10 | (85, 40, −82,78) → (245, 240, 104,42) | 110 | 120 | 70° |
| 11 | (−40, 40, −380) → (0, 240, −180) | 110 | 120 | 60° |
| 12 | (−80, 40, −460) → (40, 240, −380) | 110 | 120 | 70° |
| 13 | (0, 40, −340) → (120, 240, −300) | 110 | 120 | 70° |
| 14 | (120, 40, −180) → (240, 240, −20) | 100 | 120 | 45°, pivô +5 em Y |
| 15 | (200, 40, −260) → (240, 240, −180) | 110 | 120 | 60° |
| 16 | (120, 40, −380) → (280, 240, −260) | 98 | 120 | 65° |
| 17 | (200, 0, −460) → (360, 200, −380) | 98 | 98 | 70°, guinada ≈ 10° |

Inclinação = acos(m11) da matriz B. Sinal da guinada e ordem exata da cadeia ainda a confirmar por captura.

## Numemon (`e_nume`)

- Não tem entrada própria: vem dentro das entradas de fase (#179, #187, #195, #433, #441, #614, #628…). No #179: `beModelCtrlInfo` "e_nume", `KIND_ENEMY`. **FATO**
- Caixa de corpo (`igAABox` do HIT): (−4, 0, −4) → (4, 14, 4). **FATO**
- Sequências: `SQ_ENTR`, `SQ_WAIT`, `SQ_WALK`, `SQ_SLEP`, `SQ_STUN`, `SQ_GUARD`, `SQ_GUARDR`, `SQ_PUSHED`, `SQ_SDMG`/`MDMG`/`LDMG`/`DDMG`, `SQ_ATCK1`, `SQ_ATCK2`, `SQ_FIRE`, `SQ_FALL`, `SQ_CHMOD0`/`1`, `SQ_SILENT`. **FATO**
- `beModelCtrlMOVE`: Stop 1/4, Move 0,24/6, Back 0,4/8 · 0,6/9,5 · 0,6/10, Jump e Fall 1,7/6,3. Com o Guilmon (Move 0,7/6), o segundo número é igual e o primeiro muda: **HIPÓTESE** de que um é aceleração e o outro velocidade máxima.
- Hitboxes de ataque (Int@3 = 70004): quadro 10 por 5 em (6,3, 1,5, 2,2) tamanho (2,5, 3, 2,5); 10/5 em (4,1, 1,5, 5,7) tamanho 3; 15/10 em (−0,4, 1,8, 11) tamanho (5, 3,5, 5); 25/10 em (−6,1, 2,8, 9,5) tamanho (4,5, 5,5, 4,5). Caixa de corpo do tipo 70000: (0, 7, 0) tamanho (9,5, 14, 9,5). **FATO** (valores); papel dos campos HIPÓTESE.
- IA (`beModelCtrlAIMap`): "MAP00" com 1296, 2025, 22500 (36², 45², 150²) e 0,0436 rad (2,5°); "MAP01" com 484, 1600, 22500 (22², 40², 150²) e 0,0524 rad (3°); cada mapa tem 4 `beModelCtrlAIDataList`, e `SQ_CHMOD0`/`1` troca de mapa. **HIPÓTESE forte:** faixas de distância ao quadrado (perto, médio, longe, fora) com uma lista de ações por faixa.
- `beModelCtrlAIData`: String@2 sequência, Float@3 ângulo em rad (1,7453 = 100°, 0,7854 = 45°), Float@4 duração ou distância, Int@5 peso. Ex.: `SQ_WALK` 1,7453/15/14, `SQ_FIRE` 0/0/6, `SQ_WAIT` 0/21/16. **HIPÓTESE:** escolha ponderada por Int@5.
- Modelo (exportado com `igb-gltf … --ator=e_nume`, a partir do `igAnimationDatabase` #5118 do #179): esqueleto "defaultSkeleton" com 40 ossos e 21 matrizes de pele, 1 pele (298 vértices, 330 triângulos, textura 64×64 formato 14) e 18 animações: `wait01`, `run01`, `natk01`, `blst01`, `actn01`, `actn02`, `guard01`, `trnsIN`, `stun`, `sleep`, `Sdmg01`–`02`, `Ldmg01`–`06`. **FATO**
- Altura visual na espera: Numemon 12,65, Guilmon 9,91. **FATO.** O "dobro" das caixas de corpo (14 contra 7) não vale para a malha.
- Gerador: no #183 todos os `GENE` têm Int@7 = 17 e o único inimigo do #179 é o Numemon; caixas usam 370; outras fases usam 27, 18, 28, 25, 34, 85. **HIPÓTESE forte:** Int@7 é o ID global do tipo gerado (17 = Numemon).

## Entrada do jogador

- O jogador não está no grafo da fase: `funcPlayerSet` (func.lua do #179) gera um `PLAYER` por jogador ativo com `be:sd("PLAYER", be:SYSgetPlayerName(i), i, "GENERATE")`. Sem jogadores, gera `p_agumon`. **FATO** (desmontagem).
- `funcStageChange` escolhe o ponto com `be:DMsetSD("PLAYERPOINT", nil, -1, "SETGENERATEPOS", "I<n>", 10)`. Nenhum IGB de #179 a #190 tem nó ou string `PLAYERPOINT`. **HIPÓTESE:** a tabela de pontos está no motor ou nas portas e gates. Para a fatia, o ponto inicial será medido no jogo.

## Unidades

**FATO (corrige a estimativa da Fase 2):** o Guilmon tem cerca de 10 unidades de altura. A pele High vai de y = 0 a 9,91, e a caixa de corpo mede 5 × 7 × 5. O Numemon tem caixa de 8 × 14 × 8. As salas têm de 100 a 300 unidades, e as paredes de colisão 100. Não há escala extra entre personagem e fase.
