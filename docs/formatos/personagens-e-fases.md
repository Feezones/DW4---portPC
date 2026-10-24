# Personagens, fases e colisão (levantamento da Fase 2)

Fonte: `dw4 igb-survey` em 2026-10-24 sobre a cópia de trabalho (só leitura). Os CSV completos ficam em `D:\dw4-work\inventario\survey_*.csv`, fora do repositório.

## Digimon jogáveis em `ndmw.afs`, categoria `_com`

**FATO:** as entradas #834 a #897 são 4 blocos de 16 personagens, sempre na mesma ordem. O nome vem das texturas `P_<nome>01.dds`:

agumon, alpha, angelr, beelzeb, bwargrey, dorumon, dukecrim, girumon (Guilmon), hekabut, impdrafm, impdrapm, metalgaru, susanoo, vmon (Veemon), wargrey, weregaru.

| Bloco | Geometrias | Imagens | Animações | Tamanho | Hipótese |
|---|---|---|---|---|---|
| #834–849 | 3 | 2 | 24 (waitS/A/D/G, jumpD, runD…) | 260–373 KB | conjunto reduzido |
| #850–865 | 3 | 2 | 82 | 510–640 KB | conjunto completo de ações |
| #866–881 | 2 | 1 | 82 | ~420 KB | variante (a identificar) |
| #882–897 | 1 | 1 | 2 | 140–250 KB | menu ou evolução (a identificar) |

Exemplos: Agumon #834/#850/#866/#882; Guilmon #841/#857/#873/#889; Veemon #847/#863/#879/#895; Dorumon #839/#855/#871/#887.

**Amostra escolhida para a Fase 2:** Guilmon #857, com 82 animações.

## Esqueleto e animação

**FATO:** 158 IGB do `ndmw.afs` têm esqueleto (`igSkeleton`, sempre com o nome "defaultSkeleton"), pele (`igSkin`, `igBlendMatrixSelect`) e animação. As animações vêm em `igAnimation` → `igAnimationTrack` → `igEnbayaTransformSource` → `igEnbayaAnimationSource` e são comprimidas em Enbaya. São 7554 fontes e 215.322 trilhas no total.

## Fases

**FATO:** as entradas `stag`, `dng1`–`dng4` e `dngq` do `ndmw.afs` são fases completas. Cada uma tem centenas de geometrias, inimigos (`E_*.dds`), efeitos (`x_*.png`), interface e scripts Lua de geração, baús e eventos.

**FATO:** no `area.afs` estão principalmente interface, texto e telas de carregamento. As classes com nome parecido com colisão (`igColorAttr`, `igGraphPathList`) eram falso positivo.

## Colisão

**HIPÓTESE:**
- `beHitLandModelInfo` / `beHitLandModelInfoRam` (726 objetos, em 92 fases) guardam a malha ou o modelo de chão e paredes.
- `beHitLandDelivInfo*` guardam a distribuição dessa malha.
- `beModelCtrlHITBOX` (27.530 objetos) guarda as hitboxes de personagem.
- `igGraphPath*` pode ser caminho de IA ou de câmera.

Nada disso foi interpretado ainda.

## Guilmon (#857): esqueleto, pele e animação (conferido em 2026-10-24)

| Item | Fato observado | Confiança |
|---|---|---|
| Ossos | 31 ossos, em ordem dada por `igSkeletonBoneInfoList` (lista de objetos `igSkeletonBoneInfo`): raiz "", RotateRoot, JointRoot, Back, Abdomen, chest, Head, Chin01/02, L/REar01, L/RArm01–04, LegRoot, L/RLeg01–04, Tail01–03 | alta |
| Hierarquia | `igSkeletonBoneInfo` Int@3 = índice do pai; Int@4 = matriz de pele (0–25, −1 em 5 ossos) | alta: a árvore fecha sem ciclos |
| Repouso | `igSkeleton` MemoryRef@3: 31 × Vec3f, translação local de cada osso | alta |
| Bind | `igSkeleton` MemoryRef@5: 26 × Matrix44f, inversas de bind só com translação. A diferença entre matrizes de pai e filho bate com as translações locais, exceto na cadeia da raiz | alta |
| Peles | 3 `igSkin`: "HighAppearance", "LowAppearance" (níveis de detalhe) e "KageAppearance" (sombra, sem normal e com UV zerada) | alta |
| Paleta | `igBlendMatrixSelect` → `igIntList` com 29 entradas: índice local do vértice → matriz de pele | alta: os vértices nunca usam as 3 entradas que apontam para 0 sem motivo |
| Pele por vértice | `igVertexArray1_1` MemoryRef@7 = 1 float por vértice (sempre 1,0), MemoryRef@8 = 1 byte por vértice (índice local) | alta: pele rígida, um osso por vértice |
| Animações | 60 `igAnimation` (waitS/D/A/G, runD/S/A/G, guard*, dead*, item*, stun*, sleep*, ldmg*, sdmg*, jump*…); Long@9 = duração em nanossegundos | alta: confere com a duração das fontes |
| Trilhas | `igAnimationTrack` String@2 = osso, ObjectRef@3 → `igEnbayaTransformSource` (Int@2 = trilha dentro da fonte, ObjectRef@3 = fonte) | alta |
| Enbaya | 7554 fontes, todas a 30 Hz e com 31 trilhas por fonte no Guilmon. 7274 consomem os dados exatamente. Em 280 sobram deltas para mais 1 ou 2 passos depois da duração | o motivo das sobras é desconhecido; as poses dentro da duração não mudam |

## Colisão da fase dng1 (#179), conferida em 2026-10-24

**FATO (conferido visualmente):** a raiz de cena #1729 ("BG01", 36 geometrias sem nome e sem material, 458 faces) é a malha de colisão da fase. A visão de cima sobreposta ao cenário visual (#3372 "BG02", 26.352 faces) coincide nas salas, nos corredores e nas escadas. A colisão é feita de volumes simples (piso e paredes de cerca de 40 unidades de altura). Há também um cilindro separado fora do mapa, provavelmente um gatilho (geometria sob `igTransform`, ligada ao `beHitLandModelInfo` #42).

**FATO:** os `igSwitch` apontados por `beHitLandModelInfo` (ObjectRef@7 e @8) são justamente os grupos dessa malha, além de "g_box" (caixas quebráveis).

**HIPÓTESE:** `beHitLandModelInfo` Int@10 (4, 2, 1, 2, 1026) é um tipo ou uma máscara de superfície. `g_ddamagefloor` (165 geometrias) é piso que causa dano.
