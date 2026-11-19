# Fase 3: resultados (2026-10-24)

Objetivo do roadmap: movimento, câmera, 1 arma, 1 inimigo e 1 área. Os detalhes estão em `gameplay/fase3-levantamento.md`.

**Situação:** fatia jogável concluída. Em 2026-10-24 o responsável decidiu seguir para a Fase 4 sem a medição no jogo original. Por isso a fase fecha como "fatia jogável", e **não** como "fiel". Os testes de fidelidade continuam pendentes (`fidelidade/fase3-roteiro-medicao.md`).

| Item | Resultado | Origem |
|---|---|---|
| Área | dng1 (ndmw #179) com textura, colisão e geradores | FATO (dados) |
| Câmera | 18 volumes de `beCameraBoxData` (#180 e #181, F1 alterna) | Volumes: FATO. Cadeia A·B·Comum: HIPÓTESE |
| Paredes e câmera | Paredes visuais de 30 de altura, sem colisão de câmera no jogo | FATO (dados); efeito na tela a comparar |
| Movimento | `PadMovement` com valores provisórios | PROVISÓRIO |
| Arma | w_sword01 no osso RArm04, malha já no espaço do osso. Combo SQ_ATCK1 do estilo slash dirigido pelos dados | Dados: FATO. Semântica dos comandos: HIPÓTESE |
| Inimigo | Numemon com a IA dos mapas `beModelCtrlAIMap` (MAP00/MAP01): anda, troca de modo, ataca com as caixas 70004 | Tabelas: FATO. Sorteio, faixas e giro: HIPÓTESE |
| Dano | SQ_SDMG dos dois lados: animação, recuo e MUTEKI | Dados: FATO. Duração e velocidade: HIPÓTESE |
| Regras sem renderização | `src/DW4.Gameplay` (net8.0, sem Godot): câmera, movimento, executor de sequências e IA | feito |
| Testes | 24 testes (13 sintéticos e 11 com dados do jogo) | ok no PC |
| Decisões do port | Ataque na direção do mouse | `decisoes-port-pc.md` |

## Pendente

- Medição no jogo original: o filme `.dtm` não reproduz a partir do save state no Dolphin 2609a. O caminho combinado é o responsável fazer save states (Shift+F1–F8) na dng1 e extrair a MEM1 com `dolphin-ram` e `ram-busca`.
- Valores a medir: velocidade e aceleração, FOV e transições da câmera, duração do MUTEKI e do recuo, ponto de entrada do jogador (PLAYERPOINT), ritmo da IA.
