# Guilda do DW4 (protocolo 8, 2026-11-07)

DECISÃO DO PORT, pedida pelo responsável (pacotes 1 e 2; o pacote 3 fica para o futuro). O jogo original não tem guilda.
A guilda é própria do DW4: vale para a conta do launcher, funciona sem Tamer do DMO e é criada e cuidada dentro do jogo.

## Onde fica cada parte

| Parte | Onde |
| --- | --- |
| Regras e dados | DMO.API (`privatedmosite`): `Dw4GuildService`, tabelas `dbo.Dw4Guild*` (migração web 0010), `POST api/dw4/guild` (chave de serviço) |
| Site | `GET api/public/dw4/guilds` (ranking), `GET api/public/dw4/guilds/{id}`, `GET api/guilds/dw4/mine` e `POST api/guilds/dw4/logo` (logo do Mestre, mesma aprovação dos logos do DMO, publicado em `/guild-marks/-<id>.png`) |
| Servidor do jogo | `src/DW4.Server/GameServer.Guild.cs` (comandos, convites de 2 min, baú, XP); `DevApi.Guild.cs` para testes sem a API |
| Protocolo | `GuildCmd`, `GuildView`, `GuildBankDone`, `GuildLog`, `PlayerTag`; `Spawn.Guild` leva a tag |
| Jogo | `src/DW4.Client/Fatia.Guild.cs` (janela), tag na placa (`Nameplate.SetGuild`), chat `/gu` |

## Regras

- Cargos: Mestre, Sub-mestre, Membro, Novato. Mestre e Sub-mestre convidam, mudam o aviso e tiram BIT. Membro para cima tira itens. Novato só guarda.
- Só se mexe em quem tem cargo abaixo do seu; só o Mestre nomeia Sub-mestre e passa o Mestre. O Mestre só sai sozinho (a guilda acaba junto).
- Nome com 3 a 16 letras ou números; tag com 2 a 4 letras ou números (maiúsculas). Aviso com até 120 letras.
- Nível 1 a 10. XP para o nível L: 500 × (L−1) × L. Membros: 20 + 5 por nível acima do 1. Bônus: +1% de EXP e BIT por nível acima do 1.
- XP: EXP do inimigo / 20 (mínimo 1); chefe dá 100; em dobro com colega de guilda no mesmo grupo; até 3000 por conta por hora.
- Baú: 60 tipos de item, até 999 de cada, até 99.999.999 BIT. Equipados e itens de enredo não vão (mesmas regras da troca).
  O servidor muda o save da conta e devolve o `TradeSeq`, como na troca, para um save velho do jogo não desfazer.

## Controles

- PC: O abre a janela (muda em Configurações > Controles); também na pausa (Esc).
- Controle: pausa (Start) > Guilda.
- Celular: engrenagem (pausa) > Guilda; botões grandes no toque.
- Convidar de perto: Interagir (G / Y / JOGADOR) > Convidar para a guilda.
- Para conferir a janela sem servidor (build de depuração): `DW4_ABRIR=guilda`, `guilda-bau`, `guilda-membro` ou `guilda-criar`.
