# Decisões do port para PC (fora da fidelidade)

Mudanças pedidas para o PC que **não existem no original**. Ficam separadas das descobertas de fidelidade e nunca entram nas comparações com o jogo.

| Data | Decisão | Pedido por | Onde |
|---|---|---|---|
| 2026-10-25 | Com a janela do jogo sem foco, a entrada de controle e teclado é ignorada (um controle Bluetooth pareado movia o Guilmon sozinho). | Claude, depois do teste da fase 5 | `src/DW4.Client/Fatia.cs` (`_PhysicsProcess`) |
| 2026-10-25 | Menu de status com mouse: passar o cursor sobre um item seleciona, clique esquerdo escolhe, clique direito fecha. Teclado e controle continuam como antes. | feelipe, depois de abrir o menu | `src/DW4.Client/Fatia.StatusMenu.cs` (`MenuItemAt`) |
| 2026-10-24 | O ataque sai na direção do cursor do mouse, mesmo andando para outro lado. No controle, continua na direção em que o personagem olha, como no original. | feelipe, depois de jogar a fatia | `src/DW4.Client/Fatia.cs` (`MouseGround`) |

## Constantes de regras e tabelas de dados (2026-10-24)

Constantes curtas que fazem parte de uma fórmula (6 e 4 do dano, faixas de nível do crescimento, limites de skill) ficam no código de `DW4.Gameplay`, porque descrevem a regra. Tabelas de dados do jogo (as 12 `beDataBase`, as taxas do P2ESKILLUP) não vão para o repositório: são extraídas da cópia de trabalho por `dw4 igb-db` e `dw4 dol-skilltab` e lidas em tempo de execução.
