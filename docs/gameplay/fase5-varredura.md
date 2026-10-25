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
- **Carga no cliente: 24 de 98 fases varridas** (abrir por 20 s e ler o log): dng101b1 … dng310f1. As 24 abrem limpas:
  sem erro de script, nenhum be: sem resposta, nenhuma fila de demo parada, nenhuma janela não portada.
- be: implementados antes da varredura (os mais usados pelos scripts das fases restantes, lidos nos nativos):
  - MATisRidePlayer/MATisRideOffPlayer (entrou/saiu do volume);
  - MDLgetInfoName, MDLisSeqName, MDLgetKyoriSquare, MDLisStatus (CHECKSTATUS), MDLdspExp e PLAYERgetPosStr;
  - SYSctlDiskFolder (HIPÓTESE do efeito, como o DEVICE);
  - a família BLP* (neutra, como BLT*).

## Falta

- Carga das outras 74 fases: dng310f2 em diante (dng310f2/f3, dng411*, dng412*, dngq*, stag0000..0404, stagbs02..06,
  staggf*, stagis*, stagml*). Rodar quando o PC puder abrir janelas:
  `scratchpad\varredura.ps1 -Fases <lista>` (abre e fecha só as próprias instâncias) e depois `tabela.js`.
- Conferir na janela os chefes do enredo (stagbs02..06) e o f_luce rígido.

## Tabela (24 fases varridas)

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
