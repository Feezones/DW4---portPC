# Janelas dos NPCs (lobby e lojas)

Relato do usuário na 0.6.3 (2026-11-03): as janelas do Guardião do Banco, do Guardião dos Dados (X-data) e do Digi-Lab abrem, mas
não dá para mover o cursor, escolher nem sair; no X-data o texto passa da coluna da esquerda para a da direita e aparece um
caractere estranho (¶) em "Guardião dos Dados" na legenda de baixo.

## Causa (FATO)

- Banco, X-data e Digi-Lab usam o mesmo menu com a arte das lojas (`TickArtMenu`, Fatia.ArtMenu.cs). Ele trocava o conjunto de
  teclas "seguradas" pelas do quadro atual ANTES de perguntar o que foi apertado, então nenhuma tecla contava como apertada.
  Só o Esc (tratado fora do menu) fechava. O log do pacote do usuário mostra isso: depois do `SUBWND GENERATE "LABO"` o script
  segue na hora (`PADINPUT ENABLE`), sinal de que a janela foi fechada sem escolha.
- As lojas de armas, armaduras, chips e discos (`TickShop`) faziam na ordem certa e respondiam.
- As janelas de botões (salvar, loja de cartas, Guardião da Chamada) só aceitavam setas, Enter e mouse (padrão da Godot).
- A legenda de baixo ("Guardião dos ¶ Dados") é a caixa de mensagem da Godot; o ¶ é o ícone de X-Data da fonte do jogo e não existe
  na fonte da caixa.
- A primeira tela do X-data usava a arte de duas colunas dos atributos (nome e custo) para frases longas.

## Correções

- `TickArtMenu`: guarda as teclas do quadro anterior antes de atualizar. Teclado (W/S/setas, J/Enter confirma, K/Backspace volta,
  A/D muda valor ou página) e controle voltam a funcionar.
- Mouse nas janelas com arte (banco, X-data, Digi-Lab, Avaliação, lojas): passar por cima escolhe a linha, clique esquerdo
  confirma nela (fora das linhas não escolhe), clique direito volta. A posição vem dos eventos do mouse.
- Janelas de botões: W/S e o direcional trocam o botão, J/confirmar aperta, K/cancelar aperta o último.
- Primeira tela do X-data e as telas de depositar/sacar BITS na janela de lista larga; nomes sem valor encolhem para caber na coluna.
- "¶ " vira "X-" na caixa de mensagem e nos menus com arte ("Guardião dos X-Dados", "X-Data: 30"); o ícone fica só como moeda.
- Guardião da Avaliação (CNOS): antes "janela ainda não portada"; agora abre com os textos do jogo (Appraiser, 300 BITS por
  avaliação), diz que não há item desconhecido e fecha. DESCONHECIDO: como o jogo marca um item como desconhecido.

## Inventário (bot e teclas reais de depuração, `DW4_TECLAS`)

| NPC / janela | Fase | Abre | Navega | Ação | Sai |
|---|---|---|---|---|---|
| Guardião do Banco (BANK) | stagML12/13 | sim | sim (teclado e mouse) | depositou e sacou 500 BITS | K, clique direito, "Sair do banco" |
| Guardião dos Dados (XDATA) | stagML12 | sim (pela conversa) | sim (teclado e mouse) | abriu atributos e chips | clique direito / "Voltar ao Main Lobby"; o script segue |
| Digi-Lab (LABO) | stagML12 | sim | sim | lista de Digievolução (só Guilmon liberado no teste) | "Voltar" |
| Guardião das Cartas | stagML12 → stagIS11 | sim | sim (W/S, J, K) | álbum | "Sem B. Pack."/K volta ao lobby |
| Guardião da Avaliação (CNOS) | stagML12 | sim (novo) | sim | sem item desconhecido para avaliar | "Encerrar a avaliação" |
| Loja de armas (WPU) | stagML13 | sim | sim (teclado e mouse) | comprou e vendeu | K |
| Loja de armaduras (APU) | stagML13 | sim | sim | comprou e vendeu | K |
| Loja de chips (CHIP) | stagML13 | sim | sim | comprou e vendeu | K |
| Loja de discos (DISK) | stagML13 | sim | sim | comprou e vendeu | K |
| Ferro-velho (JUNK) | stagML13 | sim | sim | nada à venda sem itens na pasta | K |
| Guardião da Chamada | stagML11 → stagIS13 | sim | sim (janela de botões) | gravar / trocar de Digimon | "Continuar"/K |
| Quadro de missões, Digi-Elfos, Agumon | stagML11 | sim | escolha da conversa | conversa e escolha | fim da conversa |
| Comandante e Digi-Elfos | stagML02 | sim | escolha da conversa | conversa e escolha | fim da conversa |
| Teleporte (mate00) | stagML11 | sim | lista de destinos (já conferida antes) | troca de fase | K desiste |

Capturas que fecham sozinhas: X-data (lista larga, chips), banco (depositar), loja de armas (compra pelo mouse), Avaliação.

## Depuração (só build Debug)

- `DW4_TECLAS="2:J,3.5:S,9:mover@550;205,10:clique@550;205,12:direito"`: eventos reais de tecla/mouse nesses segundos (a janela
  sem foco também aceita).
- `DW4_BIT=n`: BIT inicial. `DW4_ABRIR=banco|xdata|labo|cartas|avaliar|salvar|loja:WPU`.
- Logs `[menu]`, `[janela]`, `[loja]`, `[banco]` com `DW4_DIAG_NPC=1`.
