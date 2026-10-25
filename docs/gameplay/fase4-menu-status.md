# Fase 4: menu de status (tela base)

Levantamento estático (main.dol e #179) de 2026-10-25. Nada executado.

## Original

- **FATO** O menu é o objeto nativo `beNDMWStatus` (mensagens GENERATE, REMOVE, UPDATE, GETSHOPDATA, ONPREVIEW, OFPREVIEW,
  ISRUN). A tela base é `beNDMWStatusCtrl00`. As outras telas são CtrlEquip/BaseEquip, CtrlFolder, CtrlSkill, CtrlDisk,
  CtrlFlag, CtrlBit, CtrlSendBit e CtrlD0, com beNDMWListCtrl e beNDMWWindowCtrl. Os scripts Lua nunca abrem o menu de
  status: só preenchem os textos (`txt_status.lua`, por `be:TXTgetCallNo()`).
- **FATO** Atores da tela base, pelas strings ao lado do StatusCtrl00: i_stback SQ_OPEN, i_stbase SQ_LOPEN, i_sttitle,
  i_staccount, i_stdigiface `SQ_<digimon>OPEN`, i_stmenu01 (SQ_3LINEUPOPEN/3LINEDOWNOPEN/4LINEOPEN), i_stcursor01/02,
  i_stmesscr. LAYERCTL BG01..GM03 DISPOFF esconde o cenário.
- **FATO** Textos (índice da beUnicodeObjList do area #9; o nome da variável Lua não importa):
  - callNo 0: ajuda I<11..13> ("Select / Confirm / Cancel");
  - callNo 3: atributos I<14..25>: Strength, Defense, Wisdom, Spirit, Speed, 4 resistências (fogo, gelo, raio, trevas), Data,
    BITS, Next Level;
  - callNo 15: menu de 3 linhas I<26..28>: Equipment, Folder, Techniques (cada um abre o título 30/60/90 e a sua tela).
- **FATO** Os valores da tela base são texto (STWINFONT06 font040..049, 070/071), vindos de GAMERAM GETPLAYER/RECALCPLAYER.
- **DESCONHECIDO** Botão que abre e fecha; se pausa a fase; posições dos GENERATE (floats no código nativo); laço do cursor.

## No port (`src/DW4.Client/Fatia.StatusMenu.cs`)

- Atores exportados com `dw4 igb-hud … --atores=…` para `<fatia>\menu`, desenhados numa SubViewport 640 × 480 como o HUD,
  cada um com a sequência de abertura e `SQ_CLOSE` ao fechar (ObjectSequencer).
- **HIPÓTESES**: Tab ou Start abre; Tab, Esc, K ou B fecha; a fase fica parada com o menu aberto; atores na origem da tela;
  i_stmenu01 abre em SQ_3LINEUPOPEN; resistências mostradas como o valor cru (HONOOTEIKO…).
- **DECISÃO DA FATIA**: textos com a fonte da Godot em posições aproximadas, cursor "▶", Data e BITS como "-" (a fatia
  ainda não conta), submenus só registram a escolha.
- **Pendente (refino)**: os glifos ⊂ ⊃ ∪ ∩ ¶ são ícones de elemento e de Data na fonte do jogo; com a fonte da Godot saem como
  símbolos. O rosto do i_stdigiface aparece deslocado/cortado na aba. Os dois dependem da fonte e da ordem de desenho originais.
