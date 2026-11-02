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
