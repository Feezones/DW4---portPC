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

- Céu: fundo lilás liso nos campos (stag0102); o céu/fundo da fase não é desenhado.
- Os inimigos novos não foram conferidos de perto na janela (pose, escala, ataques). A IA e o corpo usam as mesmas hipóteses
  do Numemon.
