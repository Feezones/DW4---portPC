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
