# Fase 5: lojas do lobby e da Central Area (levantamento)

## O que os scripts fazem (FATO, stagml11/12/13)

- As NPCs abrem as lojas pela escolha da conversa (bML010NN.lua → DMsetLua "<loja>shop.lua").
- Lojas que são **outra fase**:
  - callshop.lua (lobby) → `NEXTSTAGE "stagIS13"`.
  - saveshop.lua (Central Area) → `NEXTSTAGE "stagIS12"`.
  - cardshop.lua (Central Area) → `NEXTSTAGE "stagIS11"`.
- A stagIS13 não vende nada. É a tela de **gravar / criar / chamar Digimon**, com os atores j_chrsel*, j_namewin*,
  j_saveareawin*, j_slot* e os textos "Do you want to save?", "Create Digimon.", "Call Digimon.". Toca RM_NSHOP e o first.lua
  só manda LOADCTRL2. Quem conduz é o motor.
- Lojas que são **janela do motor**:
  - Os scripts xdatashop, digilabshop e bankshop (stagml12) e diskshop, weaponshop, armorshop, chipshop e junkshop
    (stagml13) só fecham a conversa, devolvem o controle e mandam `SUBWND GENERATE "<TIPO>/I<jogador>"`.
  - Os tipos são XDATA, LABO, BANK, DISK, WPU (armas), APU (armaduras), CHIP e JUNK.
- shopexit.lua volta a música do lobby (AM_LOBBY, ou AM_OFNI2 / QS_HURRY pelas flags).

## No executável (FATO, leitura estática)

- Tabela dos tipos de SUBWND em 0x804E16B0: STATUS, XDATA, LABO, BANK, CNOS, DISK, WPU, APU, CHIP, JUNK, CARD (índices 0..10).
- Preços nas tabelas do banco da área:
  - `HANBAI` é o preço de venda da loja e `KAITORI` o de recompra.
  - Os dois estão em weapon, armor1/2, abChip e conItem.
  - O código lê HANBAI pelo GETDATABASE (0x80397B0C para conItem; 0x803A3A94 na lista genérica) e KAITORI em
    0x803A0EA0..0x803A26DC.
- A lista (0x803A3938 em diante) pagina de 12 em 12 (`this+0x54 * 12`) uma lista de itens pronta em `this+0x60`.
  Escreve NAME (STWINFONT14) e HANBAI (STWINFONT17) e mostra "----" num caso especial.

## Rastro do estoque (2026-10-25, segunda leitura)

- Classes de UI das lojas (só existem no executável; nenhum IGB as tem): beNDMWShopDevice, beNDMWShopJunk,
  beNDMWShopCtrlSales/Sell/DeviceSell/DiskSell/XdataChip/EditBit/Evolve/Digilabo, beNDMWShopSelect*, beNDMWShopTitle*.
  Registro de metadados do beNDMWShopDevice em 0x8032BD14.
- **FATO:** a janela da loja pede a lista com a mensagem `SUBWND GETSHOPDATA` (0x803A2984, e 0x80397EC4 na de disco) e
  guarda o objeto da resposta em `this+0x60` (0x803A2A00), que é a lista que a tela pagina de 12 em 12.
- **FATO:** os comandos do SUBWND (enum "Message" do beNDMWWindowCtrl, 0x804E20B4) são GENERATE, REMOVE, UPDATE,
  GETSHOPDATA, ONPREVIEW, OFPREVIEW e ISRUN, com os campos _windowPro, _whoGenerate e _subWndNo.
- Próximo passo: achar o tratador de mensagens do beNDMWWindowCtrl (caso 3, GETSHOPDATA). É ele que monta a lista pelo tipo
  de janela (_subWndNo: DISK, WPU, APU, CHIP, JUNK…) e diz de onde vêm os itens (tabela fixa, enredo/flags ou dificuldade).
  A classe é criada em código, por isso as referências de texto não chegam lá.

## Estoque e compra/venda (FATO, terceira leitura, 2026-10-25)

- As tabelas de estoque ficam no **banco da própria fase**: no #829 (stagml13), shopWeapon (175 × 36), shopArmor (30 × 36),
  shopChip (30 × 36), shopJunk (9 × 72, pares "NNkind"/"NN") e shopDisk (1 × 36). O banco da área (usa_pack) só tem as 12
  tabelas de sempre. O `fase-exportar` agora exporta também o banco da fase.
- O SUBWND responde GETSHOPDATA em 0x80351794: lê o jogador 0 (GETPLAYER: LEVEL, DIGIMONNO) e conta os jogadores com ISUSE.
  Depois escolhe pelo tipo de janela: 6 WPU → shopWeapon, 7 APU → shopArmor, 8 CHIP → shopChip, 9 JUNK → shopJunk.
  - **shopWeapon:** linha = base + faixa do LEVEL.
    - A base vem do DIGIMONNO pela tabela de saltos 0x804E8024: 0→0, 1→25, 2→50, 3→75, 4→25, 5→0, 6→100, 7→50, 8→125, 9→75,
      10→100, 11→125, 12→25, 13→125, 14→50, 15→0. Com mais de um jogador, a base é 150.
    - A faixa vai de 0 a 24: ≤ 9, 16, 21, 26, 31, 36, 41, 46, 51, 60, 67, 72, 77, 82, 87, 92, 97, 102, 107, 112, 117, 127, 137,
      147, e acima disso 24.
    - 150 + 24 + 1 = 175 linhas, o tamanho exato da tabela.
  - **shopArmor e shopChip:** linha = faixa do LEVEL, de 0 a 29 (as mesmas faixas e mais 157, 167, 177, 187, 197); 30 linhas.
  - **shopJunk:** linha pela COMMON.COUNTER[1]: ≥ 1000 → 0, 6000, 26000, 86000, 206000, 456000, 956000, 1956000, ≥ 3960000 → 8.
    Abaixo de 1000, a loja fica vazia. São 9 linhas.
  - A lista tem 36 posições, uma por coluna "00".."35" (3 páginas de 12): um item ou vazio ("----") por coluna, sem tirar
    repetidos (0x80351BE8..0x80351CCC).
  - Tipo de item: 0 weapon (0x803B0CB4), 1 armor1 (0x803B0DE8), 2 armor2 (0x803B0F20), 3 abChip (0x803B1014), 4 conItem
    (0x803B10A0).
- Compra (0x803A3604..0x803A3678): BIT −= HANBAI e o item vai para o DEVICEFOLDER.
- Venda (0x803A0AF8..0x803A0B58): BIT += KAITORI, com teto de 9 999 999, e o item sai da pasta. Só dá para vender item não
  equipado e com KAITORI ≥ 1 (0x803A1338).

## No port

- `src/DW4.Gameplay/ShopStock.cs`: `ShopStock` (linha e itens) e `ShopTrade` (compra e venda). Teste com o #829 real.
- Cliente:
  - `Fatia.Shop.cs`: o SUBWND GENERATE "WPU|APU|CHIP|JUNK/I<p>" abre a loja (12 por página) em modo Comprar ou Vender.
  - A escolha da conversa (`SELECTCTRL i_messcr`, funcChoiceMessage) agora tem menu (`Fatia.ListMenu.cs`).
  - NPCs: uma sequência que acaba sem JUMP volta ao SQ_WAIT. Sem isso, a conversa de loja parava no DMchkSD ISSEQNAME
    "SQ_WAIT".
- **Conferido na janela (stagml13):** falar com o vendedor de armas → "View Weapon Shop Menu." → loja WPU com shopWeapon
  linha 50 (Guilmon, forma 2, nível 1), 24 itens em 3 páginas.
- **HIPÓTESES:**
  - Não compra sem BIT suficiente nem com a pasta cheia (a condição não foi lida).
  - Sequência de NPC sem JUMP volta ao SQ_WAIT.
- **DECISÃO DA FATIA:** caixa da Godot no lugar da janela j_shwin (não exportada). V/Y alterna comprar/vender.

## DESCONHECIDO / pendente

- O auxiliar de item recebe o LEVEL (0x803B0AB4/0x803B0B9C, faixas 1/3/7/15/31) e calcula algo por item: grau ou bônus. Por
  isso as colunas repetidas talvez não sejam o mesmo item na tela do jogo. Falta ler.
- As lojas de disco (DISK: não há "shopDisk" no executável; a janela lê o conItem), X-data, laboratório, banco e cartas.

## stagIS13: gravar e sair (2026-10-25)

- **FATO:**
  - O first.lua manda LOADCTRL2 NULL GENERATE. A tela é nativa: beNDMWLoadCtrl2 e beNDMWLoadIntf2 (SelSlot, MakeChr, NameSel,
    Diffselect, MesWin, ChrDataSel, DegiState).
  - Os textos da fase (mswkMem0160..0268) dão o fluxo:
    - "Create Digimon." e "Call Digimon.";
    - "Select Difficulty", com Normal e Very Hard;
    - "Do you want to save?" (Save. / Do not save.);
    - "Select the save box." e as caixas j_saveareawin01..04;
    - "Return to the lobby?" (Return to the lobby. / End the game.).
    - O resto é do cartão de memória.
  - A gatedisc.lua sai com SETGENERATEPOS I<3> e NEXTSTAGE "stagML11".
  - As formas escolhíveis são DIGIMONNO 0..3 (Agumon, Veemon, Guilmon, Dorumon na shinka).
- **No port:**
  - Criar e chamar Digimon ficam no menu inicial: as vagas da conta (ver docs/decisoes-port-pc.md).
  - Na stagIS13, o cliente abre uma janela com os textos do jogo:
    - Save grava na vaga com o personagem no lobby, entrada 3;
    - Return to the lobby vai à stagML11, entrada 3;
    - End the game volta ao menu inicial sem gravar de novo.
- **DESCONHECIDO:**
  - O limite de letras do nome.
  - O texto e o valor do "Hard" (só "Normal" e "Very Hard" estão nos textos exportados).
  - Quantas caixas o save do original tem (as janelas sugerem 4).

## Loja de discos (2026-10-25)

- **FATO:**
  - As lojas são classes criadas em código: beNDMWShopCtrlDiskSell, DeviceSell, EditBit, XdataChip, Digilabo, Evolve, Sales,
    Sell; beNDMWShopDevice, Junk, Select, Title.
  - DiskSell tem os campos _diskIdList, _cursor, _maxSell, _kazu e _tanka.
  - O GETSHOPDATA (0x80351794) só monta lista para 6..9; DISK (5) cai no caso padrão.
  - O banco da stagml13 tem shopDisk (1 linha): conItem 2, 1, 0, 5, 4, 3, 6, 7, 8, 9.
  - Não há o texto "shopDisk" no executável.
- **HIPÓTESE forte:** a janela DISK lista a shopDisk.
  - Comprar põe o disco na DISKFOLDER, até 9 por tipo (FATO do limite).
  - Vender (DiskSell) tira um disco guardado por KAITORI.
  - A escolha de quantidade (_kazu) ainda não foi feita: o port compra e vende um por toque.
- Teste: na "Jogo: estoque das lojas da Central Area (#829) …".
- **DESCONHECIDO:** BANK (EditBit: _editBit, _editMax), XDATA (XdataChip) e CARD. O GETSHOPDATA soma BIT e XDATA (teto
  9999) dos jogadores com ISUSE antes do switch, o que deve servir ao banco e à X-data; falta ler.

## Banco (2026-10-25)

- **FATO:**
  - Os campos do jogador na GAMERAM (tabela 0x804EAC08) são BANKDEVICE (lista) e BANKBIT (inteiro).
  - A gravação copia o BANKDEVICE para 96 posições, com −1 nas vazias (0x80364EC4..0x80364F78).
  - Textos (stagml12): "D.S.G. Bank, where we store your collected Items and BITS"; "If you're destroyed out in the field,
    you'll lose all the BITS…"; menus BITS Folder / BITS Bank / Item Bank e Deposit / Withdraw.
- **No port:**
  - `Inventory`: BankBit, BankDevices (até 96), DepositBit, WithdrawBit, DepositDevice, WithdrawDevice. O banco entra no save.
  - Cliente: `SUBWND GENERATE "BANK/…"` abre uma janela com as abas BITS (valor, depositar, sacar, depositar tudo) e itens
    (pasta ↔ banco).
- **HIPÓTESES:** teto do BANKBIT igual ao do BIT; só armas, armaduras e chips entram no banco de itens; item equipado não entra.
- **Pendente:** perder os BITS carregados ao cair no campo (regra dita nos textos; o momento exato não foi lido).

## X-data nos atributos do Digimon (2026-10-26)

- **FATO** (0x803947B8..0x803949F0):
  - "Raise Digimon parameter." deixa escolher TSUYOSA, MAMORI, KASHIKOSA, SEISHIN, SUBAYASA, MHP ou MMP.
  - O custo é ⌈(valor atual − 45) / 5⌉, no máximo 400 (abaixo de 0, vira 1).
  - XDATA −= custo e UPCNT[2, 3, 4, 5, 6, 0, 1] += 1. O UPCNT soma no RECALCPLAYER. Isso confirma o mapa do UPCNT que antes
    era HIPÓTESE no GameTables.
- No port: `XDataStatus` (Gameplay) e a primeira tela da janela XDATA com as três escolhas do jogo (atributos, chips, voltar).
- **HIPÓTESES:** o valor atual é o do RECALC (com equipamento); a condição de "já está no máximo" não foi lida.

## Janelas das lojas (código nativo, levantamento)

- Arte: j_shback, j_shbase e j_shwin no #825 (stagml12), exportados por `dw4 fase-exportar` na pasta `loja`. FATO: o j_shwin tem as
  variantes SQ_AWINOPEN, BWINOPEN, CWINOPEN, DWINOPEN, EWINOPEN, JUNKOPEN, BITPUTINOPEN/BITDRAWOUTOPEN, SINKAON/OFFOPEN e
  XDATAITEMOPEN/XDATASTATUSOPEN (JOINTSET por faixa de quadros; CLOSE = FADESET OUT 1).
- Classes (strings do main.dol): beNDMWShopCtrl00/10/30/40/50, A0/A1/A2, Sales, Sell, DeviceSell, DiskSell, EditBit, Digilabo,
  Evolve, XdataChip, além de ShopSelect*, ShopTitle*, ShopDevice e ShopJunk.
- Tabela virtual → classe: no main.dol a tabela virtual de cada classe vem logo antes do registro do nome. HIPÓTESE (a ordem bate
  com o conteúdo do código): Sales 0x804ECB2C (atualização 0x803A2920: GETSHOPDATA → j_shwin SQ_AWINOPEN, cursores
  i_stcursorleft/right em x −146 e 236, y 25), DiskSell 0x804ECBBC (0x803A15B0: DISKFOLDER, SQ_EWINOPEN/SQ_BWINOPEN),
  DeviceSell 0x804ECC48 (0x803A01F0: SQ_AWINOPEN), Sell 0x804ECCD8, EditBit 0x804ECD5C, Digilabo 0x804ECDE0 e Ctrl50
  0x804ED10C (0x803967A8: conItem, HANBAI, DISKFOLDER, SQ_CWINOPEN/SQ_DWINOPEN). A numeração ShopCtrl00/10/30/40/50 parece ser
  o tipo de SUBWND × 10 (50 = DISK, com o código da loja de discos).
- Caixas de texto no usa_pack da Central Area (area.afs #681, `dw4 fonte-caixas`), todas na fonte MINIMUM com 16 px por linha:
  STWINFONT14 (12 linhas de 168 px: os nomes), STWINFONT15 (12 × 14 px: o ícone), STWINFONT17 (12 × 98 px: o preço),
  STWINFONT13 (título, 224 px), STWINFONT29 font000..002 (351 px) e font100 (577 px, a ajuda do rodapé).
- DESCONHECIDO: onde o código põe cada caixa na tela (as posições vêm do código nativo, não do usa_pack) e qual variante cada
  estado abre além das listadas acima. Até isso ser lido, a loja segue numa caixa da Godot com a fonte do jogo.
- Posição das caixas (lido depois): o beFontInfoData só traz categoria (@5), linha (@6), largura (@7, 1,68 = 168 px) e altura da
  linha (@8, 0,16 = 16 px); as transformações do usa_pack deslocam cada linha por frações de pixel; o código da Sales chama
  0x8036B278, que manda só TEXTCTL ALIGN e TEXTSET vazio. O j_shwin não tem nós de âncora para o texto. DESCONHECIDO: quem põe
  as caixas na tela (provavelmente o beTextCtrl nativo). Por isso a loja não foi montada com a arte.
