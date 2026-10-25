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

## DESCONHECIDO

- **Quais itens cada loja vende.** As tabelas não têm coluna de loja e a fase não tem dados de loja. A lista chega pronta
  em `this+0x60`, e não achei quem a monta.
- Regras de compra/venda (BIT, pasta cheia, recompra) e as janelas de banco, laboratório e X-data.

## No port

Nada implementado. Inventar o estoque iria contra a regra do projeto. Hoje o SUBWND só aparece no log: o script já devolve o
controle antes, então o jogador não fica preso. Próximo passo: achar quem preenche `this+0x60`, a partir do construtor da
janela do tipo WPU (tabela 0x804E16B0, código perto de 0x80325B74).
