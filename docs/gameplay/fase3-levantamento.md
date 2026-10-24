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

## Câmera

- O executável tem `beCameraCtrl`, `beCameraMode`, `beCameraBoxData`, `beCameraDemoData` e `beCameraCtrlInfoRamShake`, e os comandos `SETBOXCAMERA`, `SETDEMOCAMERA`, `ISDEMOCAMERA` e `CAMERAMODE`. **FATO**
- `beCameraBoxData` liga um `igSwitch`, um `igGeometryList` e três `igTransformList`. **HIPÓTESE:** volumes de ativação e posições de câmera e alvo por volume (câmera por zona). Falta decodificar.

## Unidades

A fase usa Y = 40 no chão dos geradores, e as paredes da colisão têm cerca de 40 unidades. O Guilmon exportado mede cerca de 4. **HIPÓTESE:** o personagem é posto em cena com escala 10, ou as unidades do modelo são outras. `beModelCtrlNode2` traz matriz identidade e Float@13 = 1, então a escala não está ali.
