# Fase 5: fases do caminho da dng1 (exportação em lote e passada no cliente)

Fases: dng102b1, dng102b2, dng102b3, dng103f1, dng103f2, stag0102 e stag0104 (e de novo dng101b1, dng101b2, stag0101 e
stagbs01, para levar os ajustes desta etapa).

## Exportação (FATO)

`dw4 fase-exportar` em todas: 47 a 65 passos por fase, 0 problemas. Inimigos exportados, todos com malha, textura e
animações:

| Fase | Geradores | Inimigos |
|---|---|---|
| dng102b1 | 4 | e_nume, e_scum |
| dng102b2 | 9 | e_scum, e_ogre |
| dng102b3 | 16 | e_nume, e_scum, e_ogre, e_mecha1, e_mammos |
| dng103f1 | 8 | e_scum, e_ogre, e_mecha1 |
| dng103f2 | 12 | e_scum, e_ogre, e_mecha1 |
| stag0102 | 4 | e_goburi, e_ogre, e_blosso (subchefe) |
| stag0104 | 2 | e_goburi, e_cockatri |

O e_goburi, que antes saía sem malha, agora sai com a pele rígida por osso (igModelViewMatrixBoneSelect, ver
fase5-lobby-be.md).

## Passada no cliente (FATO)

Cada fase aberta por 22 s, com o log lido. O cliente agora também escreve no log os avisos do HUD ("[problema] …").

- Nenhum erro de script e nenhum "não implementado" nas 7.
- Único aviso, em todas: "nZ1nume1 não está no *_drops.json". O cliente usava a lista de drop do Numemon da fatia.

## Corrigido: drop por gerador

- **FATO** (#200): beGeneraterData ObjectRef@14 → beGeneraterPlayerDataList; cada beGeneraterPlayerData (1..4 jogadores)
  tem @4 → beGeneraterItemDataList (drop por inimigo) e @5 → a do drop final. Cada ItemDataList lista 3 objetos com nome,
  um por dificuldade (nZ1…, hZ1…, vZ1…). Ex.: GENE-01 da dng102b1 → nZ1scum1 / hZ1scum1 / vZ1scum1.
- `IgbDrops.GeneratorLists` lê essa ligação, e o `*_drops.json` ganhou "generators". O cliente sorteia na lista do gerador
  do inimigo, pelo número de jogadores e pela COMMON.DIFFICULTY. A lista do Numemon só vale para o Numemon fixo, sem gerador.
- Teste: "Jogo: geradores da dng102b1 (#200)…".

## Pendente

- Os inimigos novos não foram conferidos de perto na janela (pose, escala, ataques). A IA e o corpo usam as mesmas hipóteses
  do Numemon.

## Céu dos campos: neblina (2026-10-25)

- **FATO:**
  - Os campos não têm domo de céu. O IGB principal tem um igFogAttr (modo 0, início 60, fim 300, densidade 0,005) com cor
    por fase: stag0101 (0,9 0,8 0,9), stag0102 (0,8 0,7 0,8), stag0104 (0,7 0,6 0,7). Tem também um igFogStateAttr ligado.
  - O "plano lilás" era a camada de nuvens (P10G02n, quase branca com alfa) sem neblina nem fundo. O fundo do cliente era
    preto.
- O exportador grava "neblina" no fase.json; o cliente usa a cor como fundo e liga a neblina por profundidade de "inicio" a
  "fim".
- **HIPÓTESES:**
  - O modo 0 é linear.
  - A cor de fundo é a da neblina.
  - A neblina vale para a fase inteira (o igFogStateAttr desligado não foi localizado na árvore).
- Conferido na janela (stag0102): fundo lilás, o castelo ao longe some na neblina, nuvens mais claras.
