# Fase 4: inventário da interface

Levantamento por nomes no main.dol e nos IGB, em 2026-10-24. Só lista o que existe e onde está; o funcionamento de cada tela ainda não foi lido.

## Telas (classes do motor)

- Painéis em jogo: beNDMWPanelWaza e beNDMWPanelObject / ObjectItem / ObjectStat / ObjectWaza (técnicas, itens, status).
- Lojas na lobby: beNDMWShopCtrl00/10/30/40/50/A0/A1/A2, DeviceSell, DiskSell, Sell, Sales, Junk, EditBit (banco), XdataChip, Digilabo, Evolve, ShopDevice, ShopJunk, ShopSelect*, ShopTitle00/01/03.
- Status: beNDMWStatus, StatusCtrl00/D0/Bit/SendBit/Disk/Folder/Flag/Skill/Equip/BaseEquip, StatusMainSlot/SubSlot/PowerSocket, StatusSubMenu, StatusSelect*, StatusTitle00..04.
- Carregar e novo jogo: beNDMWLoadCtrl2, LoadIntf2 (ChrDataSel, DegiState, Diffselect, MakeChr, NameSel, SelSlot…), LoadSlotPlayer, McSlot*; salvar: beNDMWSaveCtrl, SaveIntf.
- Título: beNDMWLogo, Title2, Title2Option. Genéricos: beNDMWWindow, WindowCtrl/Select/Title, ListCtrl, StageCtl.

## Arte

- Modelos de UI (prefixos i_ e j_) são beModelCtrlInfo (KIND_OBJECT) dentro dos IGB de fase: i_stbase, i_wzslbase00 e i_dmgnum em ndmw #178/#179/#186/#187 (dng1); j_shwin em #820/#824/#825/#828 (lobby). Título (j_startlogo) provavelmente no area.afs (não procurado).
- HUD: números i_dmgnum, i_pcdmgnum, i_healnum, i_expnum, i_bitnum; aviso de item i_getitempnl01..03; mensagens i_messwn/i_messcr/i_messky. Medidores de HP/MP e nível: subnós hpmt/mpmt (METERCTRL SETPERCENT) e hpnm/mpnm/lvnm (NUMBERCTRL SETVALUE) do modelo do jogador.
- Fontes: atlas em IGB "BecT" com beFontImage (ex.: ndmw #79). Nomes STWINFONT02..43, TALKFONT01/04, CHRSELFONT01..16, ITNAMEFONT, font000..319. Textos nos Lua (txt_shop.lua, txt_status.lua, mswk*).
- Pendente: ligação fonte ↔ caracteres (Shift-JIS → glifo) e as telas do area.afs.

## HUD em jogo (FATO, leitura de 2026-10-25 no #179)

- Medidores no IGB principal da fase, não no modelo do jogador: beMeterCtrlInfo "hpmt"/"mpmt" → beMeterCtrlOneData hpmt1..4 e mpmt1..4 (um por jogador), com _maxValueF 100, _nonZeroTimeL 1/60 s, _nonMaxTimeL ≈ 1,983 s, _isNotchMode (HP 1, MP 0).
- Desenho por animação de transform: a barra de HP é uma rolagem de UV (igTextureMatrixStateAttr no nó "hpmetr") e uma escala em "hpmetrwk02"; o MP é um medidor circular de 10 gomos (mpmetr01..10) num arco de raio ≈ 33. HIPÓTESE: SETPERCENT posiciona a animação no tempo da porcentagem (fórmula não lida).
- Números (hpnm/mpnm/lvnm e os de dano): beNumberCtrlInfoRam = [geometria, _offsetNext 0,09375, _maxValue 99999, _mode, 5 dígitos]. Cada dígito é um quad (6 vértices) e o número troca a UV: U += dígito × 0,09375. Textura numdmg.png 256 × 32 com 10 dígitos de 24 px; a cor vem da textura (laranja no dano em inimigo, i_dmgnum; rosa no dano no jogador, i_pcdmgnum). São igBillboard.
- Sequência dos números: SQ_DMGNM01..05 → OPEN01..05 (trechos 10–30, 40–60, 70–90, 100–120, 130–150 da animação "dmgnmtrans"), FADESET IN 12, WAIT 20, FADESET OUT 20, WAIT 20. Quem escolhe entre as 5 trajetórias: DESCONHECIDO.
- Texturas do HUD e painéis (num01..03, stmojipnl01, moji, bit, base*) estão no pacote de idioma da fase (usa_pack, ex.: area #9).
- Falta no port: exportador de atores sem esqueleto (beModelCtrlInfo → nós com transform, billboard, matriz de textura e igTransformSequence) para montar o HUD com a arte original.
