# Fase 5: carregar qualquer fase

## Exportação (`dw4 fase-exportar`)

- **FATO** O ndmw.igb tem 2080 `igAdxAfsFile` [@2 nome, @3 lista, @4 AFS (0 ndmw, 1 area), @5 entrada]. `<fase>.igb` aponta
  para um esboço com um `beFileListInfo` ("[0.178]/s" = banco de som, "[0.179]" = IGB principal); o resto vem da lista
  "separate" (aaacamera, aaacameral, acmr_pack, generatorh/n/v, usa_pack, lh/ls_pack).
- Raízes de cena pelo nome: cenário "BG02"; colisão "BG01" com mais geometrias.
- Saída `<fases>\<fase>` com o layout da fatia e um `fase.json` (entradas, raízes, arquivos de câmera, mapa, objetos, menu,
  músicas nome → ADX, inimigos por linha da tabela enemy).
- Conferência: a dng101b1 exportada é idêntica à fatia (md5) nos arquivos que já existiam.

## No cliente

- `src/DW4.Client/Fatia.Stage.cs`: `DW4_FASE` escolhe a fase; câmeras, demo, minimapa e músicas vêm do `fase.json`. O
  Guilmon, a arma e o skillup.json ficam na pasta comum (não são da fase).
- `NEXTSTAGE` recarrega a cena com a fase e a entrada pedidas, se a fase foi exportada. A GAMERAM continua.
- Inimigos por tipo (`EnemyKind`): modelo pela coluna MODEL da linha do gerador, cena/sequências/IA por modelo e lutador por
  linha. Caixas de ataque 7xxxx de qualquer inimigo. **HIPÓTESES**: corpo 8 × 14 × 8 do Numemon para todos; só o Numemon
  tem lista de drop conhecida; objetos destrutíveis `g_*` de gerador com o corpo do g_box.

## Pendências conhecidas (FATO pela exportação)

- stag0101: texturas do cenário estão no lh_pack/ls_pack (beTextureCtrlData por nome), ainda não resolvidas; sem câmera
  por volumes (a fatia usa a de seguir), sem minimapa; e_goburi com pele `igSkin` sem `igBlendMatrixSelect` (formato novo).
- dng101b2: objetos novos (g_d1bkwall, g_dcap, g_dhpheal, g_dreturngate, g_security) e o subchefe e_blosso.
