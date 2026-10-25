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

## Exportador `igb-hud` e o HUD da fatia (2026-10-25)

`dw4 igb-hud ndmw.afs 179 area.afs 9 <pasta>` grava `ndmw_0179_hud.json` e as PNG dos atores i_dmgnum, i_pcdmgnum, i_stpnl01..05 e i_stbase (código em `tools/DW4.Formats/Igb/IgbHud.cs`, teste "Jogo: HUD da dng1" no DW4.Tests).

- Raiz de cada ator: o igAttrSet com o nome do ator (FATO: i_dmgnum #25434 sob a camada "WN00" #22468; i_stpnl03 #31271 sob "WN01" #6852). O JSON guarda os nós por índice de slot, porque vários nós são compartilhados: os 4 jogadores usam os mesmos hpmetr, mpmetr01..10 e a geometria "mpparts".
- Os painéis i_stpnl01..05 têm igProjectionMatrixAttr = projeção ortográfica de 640 × 480 centrada (2/640, 2/480; z de −10 a −10010). O Lua gera i_stpnl01..04 juntos em V<−260,−160,0>, V<−110,−160,0>, V<40,−160,0> e V<190,−160,0>, um por jogador; o primeiro é o do laço com índice 0 (FATO em ded_g_tcounter00.lua).
- Os números hpnm/mpnm/lvnm ficam no i_stpnl04, não no i_stpnl03 (FATO).
- igTransformSequence1_5: @2 translação, @3 rotação (quatérnio x y z w), @4 escala, @11 tempos em ns (int64), @15 máscara (bit 0 translação, bit 1 rotação, bit 3 escala: FATO pelas 7 combinações que aparecem no #179), @18 duração. @6, @7, @8, @10 e @16 saem crus (DESCONHECIDOS).
- JOINTSET "" a b das ações põe o tempo, em quadros de 60 Hz, de todas as sequências do ator: o dmgnmtrans dura 2,517 s = 151 quadros, o que bate com OPEN01..05 entre 10 e 150 (FATO). i_stpnl01 roda 0..360 em loop; i_stpnl02 para no quadro do Digimon (LABEL "girumon" → WAIT03 → 32: o rosto do Guilmon na matriz de textura do charaicon.png); i_stpnl03/04 ficam no quadro 0.
- Matriz de textura: os igTransform abaixo de um igAttrSet com igTextureMatrixStateAttr mexem na UV, não na posição. O hpmetr desloca a UV de (0,1875; 0,5) a (0; 0) entre 1 s e 3 s (FATO).
- Medidores, HIPÓTESE forte: as chaves de todos os nós do hpmt/mpmt cobrem 1 s → 3 s. Os gomos k do MP acendem (escala 0 → 1) em ≈ 1 + 0,2·(k − 1) s, o hpmetrwk01 (faixa de perigo) some a 1,5 s (25%) e o hpmetrwk02 (o X de zerado) só aparece até 1 s. A fatia usa tempo = 1 + 2 × valor/máximo, limitado a 1 + _nonZeroTime e 1 + _nonMaxTime. Com HP 0 o painel mostrou o anel vermelho com o X, como no jogo. A fórmula do SETPERCENT no DOL ainda não foi lida.
- Números: dígito k (0 = unidade, o quad mais à direita: FATO pelas posições), U += dígito × _offsetNext; _kind 0/2 = borda esquerda da célula, 1/3 = borda direita. Zeros à esquerda: a fatia esconde (HIPÓTESE para _mode 1).
- Na fatia (`src/DW4.Client/Hud.cs`): painel do jogador 1 (i_stpnl01..04) numa SubViewport ortográfica de 640 × 480 escalada para a altura da janela (DECISÃO DA FATIA); números de dano i_dmgnum/i_pcdmgnum no mundo, com billboard, trajetória do dmgnmtrans e fade de 12/20 quadros. Qual OPEN01..05 usar é sorteado (DECISÃO DA FATIA; o jogo não foi lido). O número nasce na posição do modelo atingido (HIPÓTESE).
- A UV da imagem: o IGB guarda as linhas de baixo para cima; com escala 0 na matriz de textura a UV crua (0, 0) deve cair no texel da última linha da PNG (preto translúcido no stpnlwn01), não na primeira.
- Ainda não ligado: i_stpnl05, i_stbase (menu de status), os outros jogadores e os números de EXP e BIT (expnm0, bitnum0 são exportados).
