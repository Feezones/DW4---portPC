# Fase 2: resultados (2026-10-24)

Objetivo do roadmap: um Digimon com esqueleto e animações e um mapa com colisão. Os detalhes de formato estão em `formatos/personagens-e-fases.md`.

| Item | Resultado | Situação |
|---|---|---|
| Localização | Os 16 personagens jogáveis estão em `ndmw.afs` #834–897, em 4 variantes de cada. As fases estão em `stag`/`dng*`/`dngq` | feito |
| Esqueleto | Guilmon (#857): 31 ossos, hierarquia e translações de repouso, 26 inversas de bind | feito |
| Pele | Rígida, com 1 osso por vértice pela paleta do `igBlendMatrixSelect`. Há 3 níveis: High, Low e Kage (sombra) | feito |
| Textura do personagem | Formato 14 = DXT1 de PC com linhas de baixo para cima. O atlas do Guilmon sai correto | feito |
| Animação | Decodificador Enbaya adaptado do enbrip (MIT): as 7554 fontes decodificam sem erro, todas a 30 Hz. Quatérnio conjugado e translação do esqueleto, exceto na raiz | feito; 280 fontes têm deltas sobrando depois da duração (motivo desconhecido) |
| Exportação | `dw4 igb-gltf`: glTF com esqueleto, pele, textura e 60 animações (82 clipes de origem). A Godot importa sem erro nem aviso | feito |
| Poses | waitS, runD, guardD e deadD ficam naturais em capturas na Godot | conferido visualmente; comparação com o jogo adiada |
| Colisão | A camada "BG01" da fase dng1 (#179) é a malha de colisão: 36 geometrias e 458 triângulos, e coincide com o cenário visual | feito |
| Colisão na Godot | `dw4 igb-colisao` gera um glTF com nó `-colonly`, que vira StaticBody3D. Na queda, a cápsula para no piso com erro de 0,0001. Andando, para na parede a 1 raio de distância | feito |
| Durações | Long@9 do `igAnimation` (ns) é igual à duração das fontes Enbaya nas 60 animações | conferido internamente; contra o jogo, adiado |
| Testes | `DW4.Tests` ganhou 3 testes com dados do jogo (Guilmon, 7554 fontes, colisão) | ver diário |

## Em aberto

- **Escala:** o Guilmon tem cerca de 4 unidades de altura, e as salas têm mais de 200 unidades, com paredes de colisão de 100. Nas fases, os personagens devem passar por `igTransform` ou `beModelCtrl*` com escala. Isso ainda não foi lido.
- **Raiz animada:** não se sabe se a translação de JointRoot substitui ou soma a do esqueleto.
- **Texturas das fases:** as imagens de formato 14 (DXT1) já são decodificadas no `igb-export`, mas a captura da sala usou o OBJ antigo, sem elas.
- **Comparação com o jogo rodando:** adiada por decisão do responsável.
