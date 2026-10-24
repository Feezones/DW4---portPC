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
