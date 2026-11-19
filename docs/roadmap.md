# Roadmap

| Fase | Objetivo | Critérios de aceitação |
|---|---|---|
| F0 Auditoria | Ambiente, inventário do disco, formatos, plano | Ambiente do PC registrado com comandos; CSV com offset, tamanho e SHA-256 de cada arquivo; hash do original igual antes e depois; formatos com confiança e evidência |
| F1 Formatos e pipeline mínimo | Contêiner, compressão, 1 textura, 1 modelo estático, 1 áudio | Especificação com offsets e teste automatizado por formato; resultado bate com captura do jogo; conversão reproduzível (mesmo hash de saída) |
| F2 Personagem e cenário | 1 Digimon com esqueleto e animações, 1 mapa com colisão | Durações de animação em frames dentro da tolerância; rotas de colisão validadas |
| F3 Fatia vertical | Movimento, câmera, 1 arma, 1 inimigo, 1 área | Testes de fidelidade aprovados; regras rodam sem renderização |
| F4 Sistemas completos | Combate, habilidades, itens, progressão, missões, UI, áudio; regras do multiplayer original (até 4 jogadores) estudadas como especificação | Cada sistema com especificação, medições e divergências documentadas; regras em `DW4.Gameplay` determinísticas e sem renderização, prontas para rodar num servidor |
| F5 Conteúdo completo | Jogo inteiro no Windows sem emulador | Jogável do início ao fim; desempenho estável |
| F6 Online | Multiplayer online com sistema de party/grupos (alvo do produto, não multiplayer local) | Estudo técnico e jurídico aprovado antes de qualquer código de rede |

## Direção do multiplayer (2026-10-24)

O responsável definiu que o alvo é multiplayer **online com party/grupos**, e não multiplayer local. Consequências desde já:

- As regras ficam em `DW4.Gameplay`, separadas da apresentação, determinísticas (passo fixo, sorteio com semente injetada) e sem estado global, para poderem rodar com servidor autoritativo.
- O multiplayer do original (até 4 jogadores) é estudado para servir de especificação de fidelidade: escala de inimigos, divisão de itens e experiência, câmera compartilhada.
- Nenhum código de rede antes da F6 e do estudo aprovado.
