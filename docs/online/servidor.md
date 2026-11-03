# Servidor do online do DW4

Primeira etapa do coop (ADR 0003): os jogadores se veem e andam juntos. Combate, inimigos e drops em grupo vêm depois (6d).

## Peças

| Peça | Onde | Papel |
|---|---|---|
| `src/DW4.Net` | protocolo e `World` (sem socket) | pacotes, instâncias de fase, quem vê quem |
| `src/DW4.Server` | `dw4-servidor` (console, sem janela) | UDP com LiteNetLib, confere o ticket na DMO.API, repassa posições, heartbeat de presença |
| `src/DW4.Client` (`Online.cs`, `Fatia.Online.cs`) | o jogo | conecta com o ticket, manda o próprio estado a 20 Hz, desenha os outros com a placa |
| DMO.API (`privateDmoSite`) | `api/dw4/ticket/redeem`, `api/dw4/presence` | conta, Tamer e party; "no DW4" para os amigos |

## Como entra

1. O launcher pede o ticket (`api/auth/game-ticket`) e abre `DW4.exe -- --conta <usuário> --ticket <ticket>`. O jogo conecta no servidor oficial (`Online.DefaultServer`, `209.126.83.192:7031`, a VPS da DMO.API); `--servidor host:porta` ou `DW4_SERVIDOR` trocam o endereço.
2. O jogo conecta (UDP, chave `DW4-online-1`) e manda `Hello` com o ticket, o nome do personagem, a forma e o nível.
   O jogo gasta o ticket logo ao abrir (`Online.Claim`, no menu), porque ele só vale 2 min para o primeiro uso e a abertura,
   o menu e a criação de personagem passam disso. Depois, ao entrar na fase, reconecta com o personagem (o mesmo ticket vale
   do mesmo IP por 12 h); se cair, tenta de novo na próxima troca de fase.
3. O servidor resgata o ticket na DMO.API (mesma regra do DMO: 1º uso em 2 min; reconexão do mesmo IP por 12 h).
   Válido: `Welcome`; inválido: a desconexão leva o motivo, e o jogo segue local avisando no chat.
4. A cada 30 s o servidor manda o heartbeat de presença; a resposta traz a party, que decide a instância.

## Instâncias (DECISÃO DO PORT)

- Fases da cidade (`stagml*`): abertas, todo mundo se vê.
- As outras (dungeons, chefes, tutorial): uma instância por party; sem party, uma por conta.
- Mesma conta entrando de novo derruba a sessão antiga.

## Rodar

Variáveis de ambiente do `dw4-servidor`:

| Variável | Padrão | |
|---|---|---|
| `DW4_PORTA` | 7031 | porta UDP (abrir no firewall da VPS) |
| `DW4_API` | `http://127.0.0.1:5000/` | DMO.API na mesma máquina (os `api/dw4/*` só aceitam localhost) |
| `DW4_CHAVE_SERVICO` | | a mesma `Dw4:ServiceKey` da DMO.API (32+ letras) |
| `DW4_DEV=1` | | teste local sem a API: ticket `dev:Nome`. Nunca em produção |

```
dotnet publish src/DW4.Server -c Release -o publicar/dw4-servidor
```

Teste local sem launcher: servidor com `DW4_DEV=1`; o jogo pela Godot com `DW4_SERVIDOR=127.0.0.1:7031` e `DW4_TICKET=dev:Nome`
(`DW4_TICKET` só vale em build de depuração).

## Pendências

- Launcher: nada a fazer para o endereço (o jogo já sabe o oficial). Os `arguments` do catálogo vão antes do `--` e a Godot não os repassa ao jogo, então o endereço não pode ir por lá.
- DMO.API: publicar `api/dw4/ticket/redeem` (PR #4 do privateDmoSite) e ter `Dw4:ServiceKey` configurada.
- Sem colisão entre jogadores e sem combate em grupo ainda; o chat do jogo ainda é local (o do hub, PR #3, entra depois).
