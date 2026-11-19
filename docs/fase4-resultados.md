# Fase 4: resultados (2026-10-25)

Objetivo do roadmap: combate, habilidades, itens, progressão, missões, UI e áudio, mais as regras do multiplayer original
como especificação. Detalhes em `gameplay/fase4-*.md`.

**Situação:** sistemas da dng1 prontos na fatia e conferidos no PC. Como na Fase 3, a fase fecha como "sistemas jogáveis",
**não** como "fiel": não houve medição comparativa no jogo original. Os valores marcados como HIPÓTESE continuam a medir.

| Sistema | Resultado | Origem |
|---|---|---|
| Combate | Fórmulas de dano, acerto, crítico e reação dos dois lados (P2E/E2P) | Código do main.dol: FATO com endereço |
| Técnicas e evolução | Técnicas com MP; digievolução e skills por forma | FATO (código e tabelas) |
| Itens e drops | Tabelas conItem/insItem/impItem/abChip, drops, equipamento | FATO (tabelas); sorteio com semente |
| Áudio | Músicas ADX com loop e efeitos | FATO (dados) |
| HUD | Painel de status, números de dano, medidores, do IGB | FATO (dados) |
| Scripts | VM Lua 4.0 própria rodando os scripts originais da fase (ADR 0002) | FATO (dados); executor do DM: HIPÓTESE forte |
| Objetos e ondas | Portas, caixas, pisos de dano, geradores e gatilhos de área da dng1 | FATO (dados); semântica dos comandos: HIPÓTESE |
| Gravador e minimapa | Conversa pelo botão de ataque, mensagem original, minimapa no canto | FATO (dados e main.dol) |
| Entrada | Pontos de entrada da fase e demo de câmera "entry" | FATO (dados) |
| Troca de área | Janela sim/não e NEXTSTAGE pelos scripts; outras fases na Fase 5 | FATO (scripts) |
| Menu de status | Tela base reescrita (código nativo no original); submenus pendentes | Atores e textos: FATO; abrir/pausar: HIPÓTESE |
| Grupo | Regras do original (por jogador × compartilhado) documentadas para a Fase 6 | FATO (main.dol) |
| Testes | 50 testes (sintéticos e com dados do jogo) | ok no PC |

## Pendente

- Medição no jogo original (velocidades, ritmo dos Numemon, que hoje atacam rápido demais, MUTEKI, fades).
- Submenus Equipment, Folder e Techniques (nativos); fonte e janelas originais das mensagens.
- SQ_DEMOWALK do jogador na entrada; carregar outras fases (Fase 5).
