# Roadmap

| Fase | Objetivo | Critérios de aceitação |
|---|---|---|
| F0 Auditoria | Ambiente, inventário do disco, formatos, plano | Ambiente do PC registrado com comandos; CSV com offset, tamanho e SHA-256 de cada arquivo; hash do original igual antes e depois; formatos com confiança e evidência |
| F1 Formatos e pipeline mínimo | Contêiner, compressão, 1 textura, 1 modelo estático, 1 áudio | Especificação com offsets e teste automatizado por formato; resultado bate com captura do jogo; conversão reproduzível (mesmo hash de saída) |
| F2 Personagem e cenário | 1 Digimon com esqueleto e animações, 1 mapa com colisão | Durações de animação em frames dentro da tolerância; rotas de colisão validadas |
| F3 Fatia vertical | Movimento, câmera, 1 arma, 1 inimigo, 1 área | Testes de fidelidade aprovados; regras rodam sem renderização |
| F4 Sistemas completos | Combate, habilidades, itens, progressão, missões, UI, áudio, multiplayer local | Cada sistema com especificação, medições e divergências documentadas |
| F5 Conteúdo completo | Jogo inteiro no Windows sem emulador | Jogável do início ao fim; desempenho estável |
| F6 Avaliação online | Viabilidade técnica e jurídica | Estudo aprovado antes de qualquer código de rede |
