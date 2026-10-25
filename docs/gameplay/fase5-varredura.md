# Fase 5: varredura de carga das 98 fases

## Situação (2026-10-25, interrompida a pedido do usuário para liberar o PC)

- **Exportação: 98 de 98 fases exportadas** (`dw4 fase-exportar`, D:\dw4-work\fases). Todos os passos ok, exceto o esperado:
  - stagis07..20 e stagml14 são telas de interface/menu, sem cenário (BG02);
  - stagis07/09 não têm usa_pack no índice.
- Corrigido em lote na exportação:
  - fases sem lista de drop (chefes, alguns campos) não contam mais como problema;
  - inimigos sem esqueleto (f_luce1/f_luce2 do stagbs03) saem como objeto rígido, e o cliente usa o ator como modelo
    (`LoadRigidEnemyKind`).
- Personagens: os 37 modelos de inimigo exportados têm malha e animação.
- **Carga no cliente: 62 de 98 fases varridas** (24 com janela; 38 com --headless, interrompido porque o console abria uma janela; o
  usuário pediu para não abrir nada sem avisar). As 62 abrem e rodam os scripts. 48 limpas; pendências:
  - **14× fila de demo segura em "SELECTCTRL i_messky ISEND"** (as dngq*): a fase começa com uma mensagem que espera o jogador
    confirmar. Sem entrada (headless) ela não anda. **Não é erro.**
  - **4–5× fase sem geradores** (dngq5b1*, stag0000, stag0103): o cliente punha o Numemon fixo da fatia e avisava "falta
    geradores/drops". Corrigido: o Numemon fixo e esses avisos agora só valem para a fatia antiga.
- be: implementados antes da varredura (os mais usados pelos scripts das fases restantes, lidos nos nativos):
  - MATisRidePlayer/MATisRideOffPlayer (entrou/saiu do volume);
  - MDLgetInfoName, MDLisSeqName, MDLgetKyoriSquare, MDLisStatus (CHECKSTATUS), MDLdspExp e PLAYERgetPosStr;
  - SYSctlDiskFolder (HIPÓTESE do efeito, como o DEVICE);
  - a família BLP* (neutra, como BLT*).
- Chefes: LUACALL agora também nas peças f_* (ObjectSequencer) e, sem "<nome>_<modelo>.lua", o único "<nome>_*.lua" da fase
  (HIPÓTESE; FATO: f_devl1f2 chama "bmh" e a fase tem bmh_f_devl1h.lua). Nos chefes 02..06, 40 LUACALL: 38 com o script do
  nome exato e 2 resolvidos pelo fallback.

## Falta

- Carga das outras 36 fases (as que vêm depois de stag0302 na lista): só com autorização do usuário, porque abrir o Godot,
  mesmo headless, mostra janela/console.
- MOVEEX e MOVERL: lidos e implementados (ver fase5-chefe-bs01.md); falta conferir na janela.
- Conferir na janela os chefes do enredo (stagbs02..06) e o f_luce rígido.

## Tabela (62 fases varridas)

| Fase | Abre | Cmds de script | Erros | be: sem resposta | Fila parada | Janelas/LUACALL/sons | Avisos do cliente | Exportação | Inimigos |
|---|---|---|---|---|---|---|---|---|---|
| dng101b1 | sim | 46 |  |  |  |  |  |  | e_nume, g_box |
| dng101b2 | sim | 95 |  |  |  |  |  |  | e_nume, e_scum, e_blosso, g_box |
| dng102b1 | sim | 78 |  |  |  |  |  |  | e_nume, e_scum, g_box |
| dng102b2 | sim | 94 |  |  |  |  |  |  | e_scum, e_ogre, g_box |
| dng102b3 | sim | 121 |  |  |  |  |  |  | e_nume, e_scum, e_ogre, e_mecha1, e_mammos, g_box |
| dng103f1 | sim | 78 |  |  |  |  |  |  | e_scum, e_ogre, e_mecha1, g_box |
| dng103f2 | sim | 100 |  |  |  |  |  |  | e_scum, e_ogre, e_mecha1, g_box |
| dng204b1 | sim | 78 |  |  |  |  |  |  | e_mummy, g_box |
| dng204b2 | sim | 158 |  |  |  |  |  |  | e_mummy, e_minotaru, e_skulgrey, g_box |
| dng205b1 | sim | 103 |  |  |  |  |  |  | e_mummy, e_minotaru, g_box |
| dng205b2 | sim | 132 |  |  |  |  |  |  | e_mecha1, e_mummy, e_minotaru, e_pharaoh, g_box |
| dng205b3 | sim | 141 |  |  |  |  |  |  | e_mecha1, e_mummy, e_mecha2, e_pharaoh, e_anomalo, g_box |
| dng206f1 | sim | 136 |  |  |  |  |  |  | e_mecha1, e_mecha2, e_minotaru, e_pharaoh, g_box |
| dng206f2 | sim | 129 |  |  |  |  |  |  | e_mecha1, e_mecha2, e_minotaru, e_pharaoh, g_box |
| dng307b1 | sim | 47 |  |  |  |  |  |  | e_otama, g_box |
| dng307b2 | sim | 76 |  |  |  |  |  |  | e_otama, e_geko, e_tonogeko, g_box |
| dng308b1 | sim | 44 |  |  |  |  |  |  | e_otama, g_box, e_geko |
| dng308b2 | sim | 71 |  |  |  |  |  |  | e_mecha2, e_otama, e_geko, e_mecha3, g_box |
| dng308b3 | sim | 73 |  |  |  |  |  |  | e_mecha4, e_mecha2, e_otama, e_geko, e_mecha3, g_box |
| dng309b1 | sim | 78 |  |  |  |  |  |  | e_otama, e_mecha3, g_box, e_geko |
| dng309b2 | sim | 56 |  |  |  |  |  |  | e_otama, e_maridevi, e_mecha3, g_box, e_geko |
| dng309b3 | sim | 91 |  |  |  |  |  |  | e_otama, e_maridevi, e_mecha3, g_box, e_geko |
| dng309b4 | sim | 74 |  |  |  |  |  |  | e_otama, e_maridevi, e_mecha3, e_diabolo, g_box, e_geko |
| dng310f1 | sim | 114 |  |  |  |  |  |  | e_maridevi, e_mecha3, g_box, e_otama, e_geko |
| dng310f2 | sim | 108 |  |  |  |  |  |  | e_maridevi, e_mecha3, g_box, e_otama, e_geko |
| dng310f3 | sim | 166 |  |  |  |  |  |  | e_mecha3, g_box, e_otama, e_geko |
| dng411b1 | sim | 60 |  |  |  |  |  |  | e_megadra, g_box |
| dng411b2 | sim | 64 |  |  |  |  |  |  | e_infermo, e_megadra, g_box |
| dng411b3 | sim | 200 |  |  |  |  |  |  | e_infermo, e_megadra, e_rare, e_mecha5, g_box |
| dng412b1 | sim | 102 |  |  |  |  |  |  | e_infermo, e_megadra, e_rare, g_box |
| dng412b2 | sim | 140 |  |  |  |  |  |  | e_mecha4, e_infermo, e_megadra, e_rare, g_box |
| dng412b3 | sim | 264 |  |  |  |  |  |  | e_mecha4, e_infermo, e_megadra, e_rare, g_box |
| dng412b4 | sim | 157 |  |  |  |  |  |  | e_infermo, e_megadra, e_rare, e_mecha5, e_ldknight, g_box |
| dngq1b1e | sim | 54 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_nume, e_scum |
| dngq1b2e | sim | 61 |  |  |  |  |  |  | e_mammos, g_box, e_nume, e_scum, e_ogre |
| dngq2b1e | sim | 104 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_mecha1, e_mummy, e_minotaru, e_pharaoh |
| dngq2b2e | sim | 69 |  |  |  |  |  |  | e_mecha2, g_box, e_mummy, e_pharaoh |
| dngq2b2h | sim | 32 |  |  |  |  |  |  | e_mecha2, g_box, e_mummy, e_pharaoh |
| dngq3b1e | sim | 69 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | e_otama, e_geko, e_maridevi |
| dngq3b2e | sim | 51 |  |  |  |  |  |  | e_mecha3, e_otama, e_geko, e_maridevi |
| dngq4b1e | sim | 78 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_mecha1, e_mummy, e_pharaoh |
| dngq4b1h | sim | 92 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_mecha1, e_mummy, e_pharaoh |
| dngq4b1v | sim | 105 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_mecha1, e_mummy, e_pharaoh |
| dngq4b2e | sim | 98 |  |  |  |  |  |  | e_anomalo, g_box, e_mummy, e_mecha2, e_minotaru, e_pharaoh |
| dngq4b2h | sim | 112 |  |  |  |  |  |  | e_anomalo, g_box, e_mummy, e_mecha2, e_minotaru, e_pharaoh |
| dngq4b2v | sim | 156 |  |  |  |  |  |  | e_anomalo, g_box, e_mummy, e_mecha2, e_minotaru, e_pharaoh |
| dngq5b1e | sim | 55 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  | falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\drops): sem drops / falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo / falta inimigos\e_nume\personagem.gltf (dw4 fase-exportar) |  |  |
| dngq5b1h | sim | 141 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  | falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\drops): sem drops / falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo / falta inimigos\e_nume\personagem.gltf (dw4 fase-exportar) |  |  |
| dngq5b1v | sim | 248 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  | falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\drops): sem drops / falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo / falta inimigos\e_nume\personagem.gltf (dw4 fase-exportar) |  |  |
| dngq6b01 | sim | 66 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | e_mecha4, g_box, e_infermo, e_megadra, e_rare |
| dngq7b1e | sim | 106 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_otama, e_geko, e_maridevi |
| dngq7b2e | sim | 104 |  |  |  |  |  |  | e_mecha3, e_tonogeko, g_box, e_otama, e_geko, e_maridevi |
| dngq8b1e | sim | 88 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_box, e_mecha4, e_infermo, e_megadra, e_rare |
| dngq8b2e | sim | 142 |  |  |  |  |  |  | g_box, e_mecha4, e_infermo, e_megadra, e_rare |
| dngq8b3e | sim | 161 |  |  |  |  |  |  | e_mecha5, e_ldknight, g_box, e_mecha4, e_infermo, e_megadra, e_rare |
| dngq8b3h | sim | 162 |  |  |  |  |  |  | e_mecha5, e_ldknight, g_box, e_mecha4, e_infermo, e_megadra, e_rare |
| dngq8b3v | sim | 163 |  |  |  |  |  |  | e_mecha5, g_box, e_mecha4, e_infermo, e_megadra, e_rare, e_ldknight |
| stag0000 | sim | 41 |  |  |  |  | falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\drops): sem drops / falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo / falta inimigos\e_nume\personagem.gltf (dw4 fase-exportar) |  |  |
| stag0001 | sim | 31 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  |  |  | g_boxfield, e_goburi, e_ogre, e_nume, e_cockatri |
| stag0101 | sim | 141 |  |  |  |  |  |  | e_goburi, e_nume, e_ogre, g_boxfield, g_bbarrelsz01, g_woodencasez01 |
| stag0102 | sim | 114 |  |  |  |  |  |  | e_goburi, e_ogre, e_blosso, g_boxfield, g_bbarrelsz01, g_woodencasez01 |
| stag0103 | sim | 75 |  |  | CheckZero SELECTCTRL i_messky 0 ISEND |  | falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\drops): sem drops |  | e_nume |

## Varredura das 36 fases restantes e dos chefes 02..06 (2026-10-25, com janela, autorizado)

- Método: uma janela da Godot por vez.
  - Cada janela fechou sozinha (`--quit-after` ou tempo limite), com `--log-file` por fase.
  - Prints de depuração pelo `DW4_PRINT` (só no build Debug).
  - Nenhuma janela ficou aberta no fim.
- **Resultado:** as 36 abrem e saem sem travar.
  - 0 erros de script em todas, menos stagml14 (5, corrigidos) e stagis11/12/13 (colisão vazia; são telas de menu que o
    port trata no lugar, ver docs/decisoes-port-pc.md).
- **Corrigido nesta varredura:**
  - Inimigos cujo modelo fica num IGB próprio da fase ("<modelo>.igb"): e_arachne (stag0202), e_torta (stag0302),
    e_skulgrey, e_tonogeko. O `fase-exportar` agora os exporta.
  - Inimigos cujo IGB não está entre os da fase: e_mummy (stag0204), e_mecha3 e e_megadra (stag0403). O cliente usa o
    modelo exportado em outra fase (dng204, dng308, dng411). HIPÓTESE: é o mesmo modelo.
  - stagml14 (cena da evolução: caixa de 24 triângulos, i_evoline/i_evowipe):
    - a colisão saía vazia porque a raiz "BG01" escolhida (#766) só tem marcadores de entrada; o exportador agora tenta as
      outras raízes BG01 (#1589);
    - o boot.lua pedia be:SYSgetPlayerDigimon (FATO 0x802EC3B4: DIGIMONNO do jogador), agora implementado.
  - Barra de HP dos chefes: cada chefe usa i_bosshp0N e bshp0N (stagbs02: i_bosshp02); o cliente só tratava o 01. Agora
    trata todos (não revisto na janela).
- **Chefes na janela** (sem ninguém jogando: o Guilmon parado no ponto de entrada):
  - stagbs02, Belphemon: carrega com a câmera de entrada; ficou em SQ_WAIT por 30 s, sem golpe no Guilmon parado longe.
  - stagbs03, Lucemon: chega perto e repete SQ_MISSILE; nenhum dano no Guilmon.
  - stagbs04, stagbs05 (troca para f_devl23 com SQ_CHANGE) e stagbs06: carregam sem erro.
  - stagbs06: aos 30 s o fundo aparece branco num ângulo da câmera (a neblina é preta); causa não achada. Um script manda
    MOVETRANS ao f_devl3 a cada quadro, com deriva pequena na posição.
- **Pendente:**
  - IA de longe: a escolha "SQ_WALK" com valor 0 (MAP07 do Belphemon, lista de longe) durava 0 quadros e o chefe nunca andava.
    Corrigido como HIPÓTESE (valor 0 = segue até a próxima decisão), com teste; falta rever na janela.
  - Os projéteis do SQ_MISSILE do Lucemon (f_luce2?) não acertaram.
  - O fundo branco da stagbs06.
  - A derrota com F9 jogando de verdade.
