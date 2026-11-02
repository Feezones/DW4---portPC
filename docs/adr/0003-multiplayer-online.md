# ADR 0003: multiplayer online (chat, grupos, amigos, troca)

Status: **aprovada** pelo responsável em 2026-11-02 ("Aprovo sim"), incluindo o pacote LiteNetLib para o servidor do DW4.

## Pedido (feelipe, 2026-11-02)

Chat global, de grupo e privado; lista de amigos integrada ao launcher que já existe para o RE:DIGITAL ONLINE (DMO);
interagir com outro jogador com **F** (convidar para o grupo de até 4, adicionar amigo, trocar itens); XP e BIT divididos no
grupo; dificuldade da fase sobe com o número de jogadores.

## O que já existe (FATO, lido em 2026-11-02)

**Launcher RE:DIGITAL** (`D:\Servidor\RE-DIGITAL-Launcher`, WPF .NET 4.8, `src/ReDigital.Core/Social`):

- Conta pela **DMO.API**: `api/auth/login`, `refresh` (token de acesso de 15 min), `revoke` e `api/auth/game-ticket`
  (ticket de uso único de 2 min, hoje usado para abrir o client do DMO sem tela de login).
- Amigos: `api/social/me`, `friends`, `requests` (+ `accept`, `decline`, `cancel`), `friends/remove`, `blocks`. É a mesma
  lista de amigos do RE:DIGITAL ONLINE, ligada ao **Tamer** da conta (conta sem Tamer recebe o código `SemTamer`).
- Hub SignalR `hubs/social`: `InviteToParty`, `AcceptPartyInvite`, `DeclinePartyInvite`, `LeaveParty`; eventos
  `PresenceChanged` (com `inDw4`, `inDmo`, `inLauncher`), `PartyUpdated`, `PartyInviteReceived`, `PartyLeft` etc.
  Party com `maxSize` 4. O catálogo já marca o jogo `dw4` com `"party": true`.
- O jogo `dw4` do catálogo ainda abre o Dolphin com o disco; o port vai substituir essa entrada.
- Código da DMO.API: repositório `Feezones/privateDmoSite` (`backend/src/DMO.API`), em produção na VPS do responsável.
  Ela **já prevê o servidor do DW4** (`Dw4Controller`, na mesma VPS, chave `X-Service-Key` e só localhost):
  `POST dw4/introspect` (valida o token do launcher e devolve conta, Tamer e party), `POST dw4/presence` (heartbeat a cada
  ≤ 30 s, vence em 90 s; liga o "no DW4" dos amigos) e `GET dw4/party/{accountId}`.
- Regras da party na API (`PartyService`, decisões do responsável de 2026-10-18): até 4; **só o líder convida**; **só amigos
  mútuos sem bloqueio**; o convidado precisa estar no launcher ou no DW4; convite vale 60 s; líder que sai ou fica 30 s
  offline passa a liderança ao membro mais antigo; a party acaba quando sobra 1. `PartyGroup` segue essas regras.
- O hub social ainda **não tem chat**. O ticket de jogo (`RDT1…`, uso único, 2 min) hoje só é consumido pelo servidor do DMO.

**Original** (`docs/gameplay/fase4-levantamento-grupo.md`): até 4 jogadores; progresso da missão é do grupo, nível, EXP, BIT e
inventário de cada um; NINZU escala a quantidade de inimigos (PlayerData), não o HP; EXP pelo P2EEXPUP; BIT para quem pega.
O original não tem chat, convites, amigos nem troca: tudo isso é decisão do port.

## Decisão proposta

Três peças, cada uma com um papel:

```
 Launcher RE:DIGITAL ──(login, ticket)──► DMO.API ◄──(hub social: amigos, party, chat, presença)── Cliente DW4 (Godot)
                                            │ valida o token                                        │
                                            ▼                                                       │ UDP (jogo)
                                    Servidor do DW4 (novo, .NET 10, sem janela) ◄────────────────────┘
                                    salas por party · DW4.Gameplay + VM Lua · saves e inventários
```

1. **DMO.API (existente) = social.** Amigos, pedidos, bloqueios, party e presença continuam lá, para o mesmo grupo e a mesma
   lista valerem no launcher, no DMO e no DW4. Acrescentar no hub social:
   - `SendChat(canal, para, texto)` e o evento `ChatReceived` (geral, grupo e privada), com anti-spam no servidor
     (`ChatFlood`: 5 mensagens a cada 5 s) e respeitando os bloqueios que já existem;
   - consumo do ticket de jogo pelo servidor do DW4 (hoje só o do DMO consome); a presença e a introspecção já existem.
   O chat geral e o privado funcionam também no launcher, de graça.
2. **Servidor do DW4 (novo, `src/DW4.Server`, na mesma VPS da API).** Processo sem janela, autoritativo, usa os
   endpoints `dw4/*` que a API já tem, roda as mesmas regras do cliente
   (`DW4.Gameplay` e a VM Lua) e lê os dados do jogo da cópia que estiver na máquina do servidor (nada extraído vai para o
   Git nem é distribuído). Uma **sala por party** na fase: inimigos, dano, drops, EXP, BIT e troca são decididos lá.
   Os **saves passam a ficar no servidor** (as 4 vagas por conta): sem isso a troca de itens vira porta para trapaça.
   Transporte: UDP com **LiteNetLib** (MIT, C# puro, sem nativo; precisa de aprovação para entrar como pacote).
   Movimento previsto no cliente e conferido pelo servidor (velocidade máxima); o resto chega por estado a ~20 Hz.
3. **Cliente DW4.** Aberto pelo launcher com um ticket de jogo (por variável de ambiente, não na linha de comando, para não
   aparecer na lista de processos). Troca o ticket por token na API, conecta no hub social e no servidor do DW4.

### Regras (já no código, sem rede: `src/DW4.Gameplay/Online`)

| Regra | Origem | Onde |
|---|---|---|
| Grupo de até 4; só o líder convida e só amigos; convite vence em 60 s; acaba quando sobra 1; expulsar, passar liderança; convites pendentes ocupam vaga | 4 = FATO; resto = port, igual à DMO.API | `PartyGroup` |
| NINZU = membros do grupo na mesma fase (1..4): mais inimigos no total, mesmo máximo vivo; HP/força iguais | FATO (já ligado em `GameRam.Ninzu`) | `PartyRewards.Ninzu` |
| EXP: cada membro vivo na fase recebe a parte do P2EEXPUP (sozinho 100%; em 4, quem mata 80% e os outros 40%) | FATO | `PartyRewards.Exp` |
| BIT: quantia como no original (nível e BITRITSU de quem pegou), dividida igual entre os membros vivos na fase; o resto fica com quem pegou | port (no original vai inteiro para quem pega) | `PartyRewards.Bit` |
| Chat: canais geral, grupo e privada; `/g`, `/p`, `/w nome`, `/r`; prefixos `[Grupo]`, `[De X]`, `[Para X]` | port | `ChatChannel`, `ChatCommand`, `ChatFlood` |
| F perto de outro jogador (até 128 unidades): convidar (só o líder, só amigos; não se cheio ou em outro grupo), adicionar amigo (não se já é ou já pediu), trocar | port | `PlayerInteraction`, ação `interagir` |
| Troca: até 8 itens + BIT de cada lado; travar, depois confirmar; mudar algo destrava os dois; tudo ou nada, conferindo posse e espaço na hora; itens de enredo (impItem) fora | port | `TradeSession` |

## Andamento

- 2026-11-02: etapa 6c inicial no ar no código: `src/DW4.Net` (protocolo e salas), `src/DW4.Server` e o cliente vendo os outros
  jogadores. Como rodar e o que falta em `docs/online/servidor.md`. A entrada pelo launcher (6a) foi feita junto do pacote.

## Etapas depois da aprovação

1. **6a. Entrada pelo launcher.** Entrada `dw4` do catálogo abrindo o port; ticket → token; presença "no DW4". (precisa da DMO.API)
2. **6b. Social no jogo.** Amigos (lista, pedidos), party pelo hub, chat nos três canais, janela de convite. (precisa da DMO.API)
3. **6c. Servidor do DW4.** Salas por party, saves no servidor, jogadores se vendo e andando juntos.
4. **6d. Combate em grupo.** Inimigos e drops no servidor, EXP e BIT divididos, morte e chips de backup do grupo.
5. **6e. Menu do F e troca** ligados ao servidor.

## Questões em aberto

- ~~Nome online~~ **decidido (feelipe, 2026-11-02)**: placa sobre a cabeça com o nome do personagem do DW4; amigos,
  convites, party e `/w` pelo nome do **Tamer** da conta (a mesma lista do launcher). O servidor sabe qual conta está por trás
  de cada personagem, então o menu do F usa o Tamer do alvo sem mostrar.
- Hospedagem do servidor do DW4 (há pastas `deploy_aws` e `deploy_contabo`; a mesma máquina da API serve para começar).
- Jurídico: o servidor lê os dados de uma cópia própria do jogo; nada extraído é distribuído; o repositório é público
  (recomendado privar antes de publicar o servidor).
