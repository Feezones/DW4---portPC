# Servidor do online do DW4

Primeira etapa do coop (ADR 0003): os jogadores se veem e andam juntos. Combate, inimigos e drops em grupo vêm depois (6d).

## Peças

| Peça | Onde | Papel |
|---|---|---|
| `src/DW4.Net` | protocolo e `World` (sem socket) | pacotes, instâncias de fase, quem vê quem |
| `src/DW4.Server` | `dw4-servidor` (console, sem janela) | UDP com LiteNetLib, confere o ticket na DMO.API, repassa posições, heartbeat de presença |
| `src/DW4.Client` (`Online.cs`, `Fatia.Online.cs`) | o jogo | conecta com o ticket, manda o próprio estado a 20 Hz, desenha os outros com a placa |
| DMO.API (`privateDmoSite`) | `api/dw4/ticket/redeem`, `api/dw4/presence`, `api/dw4/characters` | conta, Tamer e party; "no DW4" para os amigos; as vagas de personagem |

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

- DECISÃO DO RESPONSÁVEL (2026-11-03): todo mundo na mesma fase se vê, na cidade e nas dungeons.
- Inimigos, dano e drops são compartilhados só entre os membros da mesma party na mesma fase (`World.CombatGroup`, etapa 6d).
  Quem não está no grupo vê os outros, mas tem os próprios inimigos.
- Mesma conta entrando de novo derruba a sessão antiga.

## Menu do F (protocolo 2)

- F perto de outro jogador (até 128 unidades, mesma instância): o cliente manda `Interact(alvo, "relation")`; o servidor pergunta
  à DMO.API (`api/dw4/social`, em nome da conta do ticket) e devolve `InteractResult` com a relação e a party.
- Convidar para o grupo (`party-invite`) e adicionar amigo (`friend-request`) seguem as regras do launcher. O alvo recebe
  `SocialPrompt` e responde com `party-accept/decline` ou `friend-accept/decline`.
- Trocar itens aparece desligado até os saves irem para o servidor.
- Cliente e servidor precisam da mesma versão do protocolo; o servidor recusa a versão antiga com aviso para atualizar.

## Combate em grupo (protocolo 3)

Só entre membros da mesma party na mesma fase (decisão do responsável, 2026-11-03). O servidor manda `CoopGroup`
(anfitrião = o primeiro que chegou) e repassa `CoopRelay` só para os outros membros, sem abrir o conteúdo (até 120 pacotes
por segundo por jogador). O conteúdo (`src/DW4.Net/Coop.cs`) é entre os jogos:

- o anfitrião roda geradores e IA e manda inimigo novo, posições (~10 por segundo), sequência, morte, corpo sumido e onda vencida;
- os outros mostram cópias, que tocam as mesmas sequências (o golpe do inimigo acerta cada um na própria tela), e mandam os
  próprios golpes com o dano já calculado;
- NINZU = membros na fase: mais inimigos por onda e a parte do EXP (quem matou leva mais). Cada um sorteia o próprio drop;
- a IA mira o membro mais perto. Se o anfitrião sai, o próximo assume e as cópias viram inimigos.

Limites da v1: chefe com `hit_f_*.lua` roda o script em quem bate, e o anfitrião só recebe o dano. O BIT ainda não é dividido.

## Quadro da party

O estado do jogador leva HP e MP. O servidor manda `PartyRoster` (os membros da party que estão no DW4, em qualquer fase,
com nível, HP, MP e fase) a todos eles quando algo disso muda. O jogo mostra os outros membros no lado esquerdo da tela
(`Fatia.PartyHud.cs`): quem está em outra fase aparece apagado, com o nome dela.

## Vagas de personagem no servidor (protocolo 4)

DECISÃO DO RESPONSÁVEL (2026-11-04): "saves no banco, com digimon, itens, bits, historia, tudo tem q ser online, nada local". O PC e
o celular usam as mesmas 4 vagas da conta.

- **Banco:** `dbo.Dw4Character` no banco web da DMO.API (migração `web/0008_dw4_character.sql`; a API cria a tabela se faltar).
  Uma linha por (conta, vaga 0..3) com o save inteiro (`Data`, o JSON do `CharacterSave`: formas, itens, bits, banco, equipamento,
  discos, flags da história e as permanentes), a gravação anterior (`PreviousData`, para restaurar à mão) e `Revision`. Apagar põe
  `Data` em NULL e guarda o save em `PreviousData`.
- **API:** `GET api/dw4/characters/{conta}`, `PUT api/dw4/characters/{conta}/{vaga}` (`{ name, data }`), `DELETE` idem; só localhost
  com `X-Service-Key`, como os outros `api/dw4/*`.
- **Servidor do DW4:** `SaveList`, `SaveWrite`, `SaveDelete` → `SaveReply` (mesmo `ReqId`). A conta é sempre a do ticket (o jogo não
  escolhe conta). O save vai em gzip (`SaveData`) e é conferido (JSON de objeto, até 512 KB). Os pedidos de uma conta vão à API um
  depois do outro, na ordem em que chegaram (a gravação mais nova nunca é sobrescrita pela mais velha). Até 20 pedidos a cada 10 s.
- **Jogo:** a conexão do menu (aberta pelo `Online.Claim`, sem personagem) fica aberta e é por ela que o menu lista, cria e apaga as
  vagas (`OnlineCharacterStore`). No jogo, troca de fase, F5, evolução, ponto de save e saída gravam no servidor
  (`Session.SaveProgress`); sair para o menu e fechar a janela esperam a confirmação e, se não gravou, perguntam (tentar de novo,
  sair sem gravar, continuar). Gravação que falhou vai de novo quando a conexão volta. Sem servidor, o menu não abre as vagas.
  No editor da Godot sem ticket as vagas continuam em arquivo (`LocalCharacterStore`), só para desenvolver.
- **Migração:** as vagas antigas do aparelho (`user://accounts/<conta>/slotN.json`) sobem sozinhas ao abrir a tela de vagas, na mesma
  vaga se livre, senão na primeira livre (`SlotMigration`); o arquivo vira `slotN.json.enviado` (cópia, o jogo não lê mais). As que
  não cabem ficam no aparelho e o menu avisa.
- **Uma sessão por conta:** a conta que entra em outro aparelho derruba a anterior com `Protocol.KickedReason`; o aparelho derrubado
  volta ao menu sem gravar e não reconecta sozinho (senão os dois ficariam se derrubando e gravando um por cima do outro).

Ordem de publicação: DMO.API (rotas e tabela) → `dw4-servidor` (protocolo 4) → jogo (PC e APK). Cliente do protocolo 3 recebe
"Versão do jogo diferente" até atualizar.

## Rodar

Variáveis de ambiente do `dw4-servidor`:

| Variável | Padrão | |
|---|---|---|
| `DW4_PORTA` | 7031 | porta UDP (abrir no firewall da VPS) |
| `DW4_API` | `http://127.0.0.1:5000/` | DMO.API na mesma máquina (os `api/dw4/*` só aceitam localhost) |
| `DW4_CHAVE_SERVICO` | | a mesma `Dw4:ServiceKey` da DMO.API (32+ letras) |
| `DW4_DEV=1` | | teste local sem a API: ticket `dev:Nome`. Nunca em produção |
| `DW4_DEV_SAVES` | `saves-dev` ao lado do servidor | só com `DW4_DEV=1`: pasta das vagas do teste local |

```
dotnet publish src/DW4.Server -c Release -o publicar/dw4-servidor
```

Teste local sem launcher: servidor com `DW4_DEV=1`; o jogo pela Godot com `DW4_SERVIDOR=127.0.0.1:7031` e `DW4_TICKET=dev:Nome`
(`DW4_TICKET` só vale em build de depuração).

## Pendências

- Launcher: nada a fazer para o endereço (o jogo já sabe o oficial). Os `arguments` do catálogo vão antes do `--` e a Godot não os repassa ao jogo, então o endereço não pode ir por lá.
- DMO.API: `api/dw4/social` (menu do F) precisa estar publicada junto com o servidor do protocolo 2.
- Sem colisão entre jogadores e sem combate em grupo ainda; o chat do jogo ainda é local (o do hub, PR #3, entra depois).
