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
