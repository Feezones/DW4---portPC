# Menu de status (TAB) refeito: Equipment, Folder e Techniques

Pedido do usuário em 2026-11-02 ("retrabalho no menu ao apertar TAB, todos os seus dados e funcionalidades, função de equipar
itens, inventário, técnicas, armas"). Layout próprio no estilo do original, como as telas de vagas e criação. Código:
`src/DW4.Client/StatusScreen.cs` (tela), `src/DW4.Client/Fatia.StatusMenu.cs` (ligação com o jogo) e
`src/DW4.Gameplay/EquipRules.cs` (regras, com teste no DW4.Tests). Levantamento da tela base: `fase4-menu-status.md`.

## O que vem do jogo (FATO)

- **Textos de todas as telas** no `txt_status.lua` (be:TXTgetCallNo), índices da beUnicodeObjList do area #9 (textos.json):
  - 11..13 ajuda (Select / Confirm / Cancel); 14..25 atributos, ¶ Data, BITS, Next Level; 26..28 Equipment / Folder / Techniques.
  - Equipment: 29..31 Weapon Slot / Armor Slot / Sub Slot; 32 "Select the slot to change Item."; 33..35 descrição de cada
    lugar (∈ Weapon Units, ∋ Armor Board, ∨ Armor Board); 36 Mods; 37..43 (Select an Item / old / new, Equip Weapon Unit,
    Edit Slot, Remove Equipment, Select option); 89..92 Equip Armor Board, Exchange Weapon Unit, Equip / Remove Ability Chip;
    93 "This Item cannot be equipped."; 99..102 Select the socket / slot / mod / Item to equip.
  - Folder: 44..47 Device / Key / Disk / BITS Folder e 58..61 as descrições; 48 "Select the folder to check Item."; 49..51
    Sort / Give / Break; 52..57 Give to?, How many?, How much?, Break Item? Yes / No; 62..63 e 103..112 as recusas (sem
    Digimon para dar, item equipado não quebra nem vai, item que não quebra nem vai, pasta do outro cheia).
  - Techniques: 64 Digivolve Tech, 67 Skill Tech, 65 MP Tech e 68..70 as descrições; 66 "Select the Tech to check.";
    71..79 as 9 skills (Blunt, Slash, Stab, Bash, Shot, Crush, Blast, Heal, Force) e 80..88 a descrição de cada uma
    (ordem = SKILL[0..8] do jogador: weapon.SKILL 0 = ブラント, pcTech.SKILL 6 = ブラスト).
- **Tabelas**: weapon.ISHAKAI (破壊, pode quebrar), ISJYOUDO (譲渡, pode dar), ISPOCKET; abChip.WU (usa na arma) e BLUEAB
  (usa na ∋); armor1/armor2.LVPOINT e weapon.SOUBI/SKILL/SKILLPOINT (regras de equipar já lidas na Fase 4); o TXT das armas e
  da ∋ termina em "ポケットのかず:" (o número é posto pelo código).
- **Ícones** dos textos são caracteres da fonte do jogo: ∈ arma, ∋ armadura principal, ∨ sub, ∧ chip, ⊂⊃∪∩ elementos, ¶ Data.
- Rostos: charaicon.png do i_stdigiface (4 × 4, na ordem da shinka). Soquetes: POWERSOCKET[0..2], o [0] é a arma ativa
  (RECALC); personagem novo com INI_WEAPON1..3 nos três (GAMEDATAINIT).

## Decisões do port e hipóteses

- **DECISÃO**: layout com controles da Godot e a fonte do jogo, nas cores do menu antigo (azul-escuro, borda lilás, linhas
  sublinhadas, faixas rosa): coluna de status fixa à esquerda, abas em cima, lista à esquerda, escolhas à direita, quadro de
  descrição embaixo, barra de ajuda com os botões do aparelho. Escala pela altura (base 720) e, abaixo de 1200 de largura, pela
  largura (4:3 cabe).
- **DECISÃO**: Q/E (LB/RB) trocam a aba; mouse aponta e clica, botão direito volta; Tab (status) e Esc fecham.
- **HIPÓTESE / DECISÃO**: 2 pockets em toda arma com ISPOCKET e em toda ∋, nenhum na ∨ (a regra do "ポケットのかず" não foi lida).
- **DECISÃO**: o soquete 1 (arma ativa) nunca fica vazio; "Exchange" troca a reserva com a ativa; os chips da arma ficam
  enquanto couberem na arma nova; tirar a ∋ tira os chips dela. Reserva aceita arma que a forma não usa agora (só a ativa é
  conferida: HIPÓTESE).
- **HIPÓTESE**: Sort ordena por tipo (∈ ∋ ∨ ∧) e pela linha da tabela. Break de disco tira um por vez (o "How many?" não foi
  feito). Itens de evento não quebram nem vão.
- **HIPÓTESE**: "Digivolve Tech" = formas liberadas e o nível de cada (o conteúdo real da tela não foi lido).
- **DECISÃO (usuário, 2026-11-02)**: o TAB é só o menu do próprio jogador. O Give do jogo (dar item ou BITS a outro Digimon do
  grupo) ficou de fora; compra, venda, banco e conversas continuam só nos NPCs. A BITS Folder só mostra os valores.
- Os textos são buscados pela chave mswkPRG, não pelo índice: cada fase tem o pacote de textos em outra ordem (no lobby o
  índice 26 é uma fala de NPC).
- Comparação antes de equipar (atributos antes > depois) é do port.

## Conferência

- Teste "Port: menu de status (TAB)…" no DW4.Tests (soquetes, armaduras, pockets, chips, marcas E, Break, Give, Sort, save).
- Capturas sem janela no PC do usuário: `DW4_TELA=status[:equiparma|equipchip|pasta|item|quebrar|discos|tecnicas|skill|evolucao|aplicado]`
  (build Debug) abre o menu com um jogador de teste e grava `menu_status*.png` em DW4_TELA_PASTA.
