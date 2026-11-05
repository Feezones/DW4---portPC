# Baúzinhos dos itens no chão

Pedido (2026-11-04): o item que cai de inimigo, caixa, barril ou caixote aparece como o baúzinho do jogo, no lugar do cubo dourado.

## FATO

- Tabela tabela → modelo no main.dol em 0x804DE0A8 (pares de ponteiros): impItem → g_000key, weapon → g_000weapon,
  armor1 → g_000armor, armor2 → g_000armor, abChip → g_000access.
- conItem e insItem têm a coluna str:MODEL: conItem 0..9 (discos) g_000item, 10 (Booster Pack) g_000boost1, 11 (Key Chain)
  g_000penx; insItem 0 (BIT) g_000bit, 1..3 (X-Data) g_000xdata1, 4..25 (plugs ↑) g_000xdata2.
- A lista 0x804EDB70 do main.dol tem os dez modelos (g_000bit, xdata1, xdata2, access, armor, boost1, item, key, penx, weapon).
- IGB #179 (e as outras fases): têm raiz (igAttrSet) só g_000item, g_000armor (16 malhas), g_000bit, g_000xdata1, g_000xdata2,
  g_000key e g_000boost1. g_000weapon, g_000access e g_000penx têm beModelCtrlInfo (KIND_ITEM) e sequências, mas nenhuma raiz com
  o nome em nenhum IGB de fase.
- Sequências (igb-seq): SQ_WAIT começa no rótulo DEF → SQ_ENTR (TRAN01) → SQ_ENT2 (TRAN02, LABELCTRL ON) → SQ_WAIT/ON (WAIT01,
  espera de 960 quadros) → SQ_BODING (FLASH1, pisca 12 vezes) → SQ_EXIT (EXIT01, some em 10 quadros) → SQ_ERAS. SQ_DDMG (DEAD01,
  FLASHSET Damage) → SQ_ERAS. O g_000key não tem SQ_BODING nem SQ_EXIT. Faixas de quadro: weapon, armor e access idênticas
  (0..330); item, bit, key, boost1, penx e xdata* cada um com as suas.

## HIPÓTESE

- weapon e access usam o desenho do g_000armor (sequências idênticas); penx usa o g_000item.
- Ao pegar, o baú faz SQ_EXIT (o código da coleta não foi lido).
- O item some sozinho no fim do SQ_BODING (≈ 20 s); o CTRL "isLive" do SQ_WAIT não foi lido.

## No port

- `Drops.ChestModel` (tabela → modelo, com teste), `Fatia.DropChest.cs` (baú, reserva de desenho e cubo se faltar o modelo, com
  aviso em _problems), `DropAt` e `UpdateGround` em `Fatia.cs`. O nome do item continua flutuando (DECISÃO DO PORT).
- Modelos em `<fases>\_comum\g_000drop` (fora do git):
  `dw4 igb-objetos ndmw.afs 179 <fases>\_comum\g_000drop g_000item g_000armor g_000bit g_000xdata1 g_000xdata2 g_000key g_000boost1`
  e `dw4 igb-seq ndmw.afs 179 <fases>\_comum\g_000drop --ator=<nome>` para cada um. O `dw4 empacotar` põe a pasta no
  `fases-_comum.pcz` (PC e celular baixam o mesmo pacote).
- Depuração (só Debug): `DW4_DROP=tabela:linha DW4_DROP_AQUI=1` solta o item aos 3 s a 30 e a 300 unidades do jogador; o log
  mostra caiu/pegou/sumiu e a sequência do baú a cada 5 s. Conferido sem janela (`--headless`) em 2026-11-04 com insItem:0,
  insItem:5, weapon:7, abChip:1, conItem:11 e impItem:2: todos pegos; o BIT longe pisca aos 20 s e some; a chave fica.
