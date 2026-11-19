# Paredes invisíveis (2026-11-02)

Relato do usuário: parede invisível no meio da ponte suspensa da stag0101 (F3: pos (462,4; 46,9; 95,7), câmera ndmw_0614).

## Auditoria (`dw4 colisao-auditoria <ndmw.afs> <fases> <saída>`, 98 fases, só leitura)

- **FATO:** toda peça com nome da colisão fica sob um `beHitLandModelInfo` com dois igSwitch: @7 com o filho 0 desligado e
  @8 com os filhos ligados. Isso vale até para as portas (doorwall) e as paredes de sala (wallP/R), que seguram o jogador no
  jogo. **HIPÓTESE:** o @7 é a exibição do polígono (SHOWPOLYGON/HIDEPOLYGON) e não diz se a colisão vale; o bit desligado
  não serve para achar parede falsa.
- **FATO:** `beHitLandModelInfo` @10: 1 = pisos (hitP/hitR), 2 = paredes e portas (wallP/R, doorwall), 4 = `limit_Circle`,
  1026 = objetos (g_box, g_d1bkwall), 2050 = peças que aparecem ou somem por script (stag0001: `hithideliftz00`, a parede
  em z = 75 no começo da ponte que surge quando os inimigos caem; stag0101: polySurfaceShape9276..9278).
- **FATO:** `limit_Circle` é um cilindro de 32 triângulos com a **mesma caixa** (403..517, 0..250, 93..207) em dng101b1,
  dng101b2, stag0001, stag0101 e stagis10 (tela de menu), sem cenário perto. Na stag0101 ele corta a ponte (pisos 38055 e
  38018, x 420..500) exatamente em z ≈ 93, onde o jogador parava. Era também a "parede cilíndrica de raio ~57 em volta de
  (460, 160)" da troca para a Jolt (decisoes-port-pc.md, 2026-10-30).
- **Correção:** o cliente carrega `limit_Circle` desligado; `HITLANDMODEL limit_Circle ONHIT` de um script liga
  (`Fatia.cs`, MakeStaticBody). **HIPÓTESE:** o jogo só o liga por script (ou o posiciona em outro lugar); a caixa idêntica
  em fases diferentes indica que não é parede do mapa.
- Degraus (paredes de até 6 de altura) só em dng205b3 (4) e dngq4b2e/h/v (1); rampas de 45° a 70° só em stag0103 (8).
- Fora da curva, a olhar: stag0203 tem 112 triângulos enormes (x −1500..2400, z −1900..300) sem cenário perto.

## Também no cliente

- O jogador sobe degraus de até 6 (`Fatia.Step.cs`; HIPÓTESE do valor). A cápsula sozinha parava em bordas de ~0,7.
- Objeto transparente (alpha 0) não segura o jogador.
- Build Debug: linha "parede:" no F3 e log `[parede]` com a peça da colisão ou o corpo que está segurando.

## Correção no arquivo exportado (2026-11-02, teste sem janela)

- **FATO:** nos `colisao.gltf` exportados o nome `limit_Circle` não aparece; o cilindro é o nó `colisao-colonly` (a malha do
  nó raiz da colisão): 32 triângulos na caixa 403..517 × 0..250 × 93..207 em 83 fases (403..517 × 20..220 × 93..207 em
  stagis08/10/20). Com o teste só pelo nome, o bot ainda parava em (461, 47, 96) na ponte da stag0101.
- O cliente reconhece também pela caixa (`IsLimitCircle`, Fatia.cs). Os outros `colisao-colonly` ficam: stag0203 (420
  triângulos de −2500 a 4300) e as telas stagis07/09/11/12/13 e stagml14 (caixa de 24 triângulos).
- Bot sem janela (DW4_ANDAR=462,40 a partir da entrada 0): z 230 → 40 sem parede.

## Parapeitos e paredes de inimigo (2026-11-04)

Relato: não dava para pular para os pilares na beira do penhasco da stag0102. F3 (build de depuração): segura a peça
`Stage0102_polySurfaceShape292`, contato em (−30,3; 120,5; −13,5).

- **FATO:** a 292 é de um `beHitLandModelInfo` com @10 = 2050 (0x800 | 2), junto com 289..291: caixas altas (até y 254..320) nas
  bordas da área jogável. A 292 cobre a mesma área do piso 239 (o alto do penhasco, y 118) e sobe até 320. Nenhum script da
  fase cita essas peças (os HITLANDMODEL da stag0102 só mexem em hitbrossgatez010/011 e hitwall02z01).
- **FATO:** @10 em todas as fases: 1 = 92, 2 = 153, 4 = 98, 1026 = 324, 2050 = 36, 8 = 14, 1025 = 6, 5123 = 2, 5125 = 1. O
  2050 aparece em 36 HitLands de cidades, masmorras e arenas de chefe. O 8 é sobretudo `wallE*` (dng103f2, dng206f2, dng310f3,
  dng412b4) e `hitR32x` (dng205b3, dngq4b2*). Os grupos 2050 com nome `hit*` também têm pisos (hitmvbox*, hitflislandz04,
  hitmagnetflore*) e portões (hitcirclegatez03).
- **HIPÓTESE:** 2050 é parapeito de borda: segura quem anda (não se cai andando) e não segura o pulo; 8 é parede só de inimigo
  (wallE = enemy).
- **No cliente** (`CollisionKinds.cs`, tabela por fase): as faces de parede (|normal.y| < 0,5) das peças 2050 sem `hit*` no nome
  vão para a camada 2, que o jogador só olha fora do pulo (`UpdatePlayerMask`). As faces de parede das peças 8 vão para a camada 3,
  que só os inimigos olham. O chão das mesmas peças continua segurando todo mundo.
- A conferir no original: se o pulo passa mesmo pelo parapeito e se o jogador atravessa as `wallE`.

## Tiro do jogador e paredes invisíveis (2026-11-06)

Relato: o tiro (Shot Pistol, `x_100gunv01`) não seguia o trajeto todo ao atirar para fora do mapa. Desde a 0.6.17 os tiros com
LUACALL de batida somem ao cruzar a camada 1 da colisão, e a colisão de todas as fases tem muita parede sem cenário.

- **FATO (main.dol):** `"BHit"` vai para o campo 0x30 do beModelCtrl e roda pela mensagem `CHKBULLET` (tiro contra modelo,
  0x8031029c), não pelo cenário. `"WHit"` (0x34) e `"FHit"` (0x38) rodam no contato com a colisão (0x80309c74); as checagens de
  parede (0xd8, `ONWALL`/`OFFWALL`) e de queda (0xda, `onFall`/`noFall`) começam ligadas (0x8030ea00).
- **FATO:** `x_100gunv01` arma BHit, WHit e FHit com `bht_x_100gunv01.lua` = `MDLsetSeq "SQ_ERAS"`.
- **HIPÓTESE (relato do usuário):** o tiro do jogador passa pelas paredes invisíveis. No cliente (`Fatia.ShotScenery.cs`) ele só
  some na colisão da fase com cenário a menos de 6 do ponto (o critério da auditoria); passa pelo chão de veículo e pelas caixas,
  e o acerto em inimigo/objeto vem antes da batida (a `g_box` tinha corpo na camada 1 e o tiro batia nela sem dar dano).
- `DW4_DIAG_TIRO=1` mostra no log onde o tiro do jogador bateu ou passou.
