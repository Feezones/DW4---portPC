# Fase 5: lobby (stagml11) e os métodos be:* que ele usa

IGB principal #821. Exportar com `dw4 fase-exportar … stagml11 …`, abrir com `DW4_FASE=stagml11`. Leitura estática do
main.dol em 2026-10-25: o despachante `0x802EA1FC` chama um nativo por método (endereços abaixo).

## Os 17 métodos

| Método | Nativo | O que faz | Status |
|---|---|---|---|
| `MsgBox(texto, n)` | 0x802EE480 | corpo vazio (`blr`): caixa de depuração | **FATO** — sem efeito |
| `SYSgetArea()` | 0x802EBC88 | devolve sempre 1 | **FATO** |
| `SYSsetGateStage(nome, n)` | 0x802EBCFC | GAMERAM GATESTAGENO = n, GATESTAGENAME = nome (nulo → "") | **FATO** |
| `SYSgetGateStageName()` / `SYSgetGateStageNo()` | 0x802EBE94 / 0x802EBEF8 | lê os dois campos; o NO vale −1 de início | **FATO**; **HIPÓTESE**: o nome começa nil (o start.lua testa `not nome` e grava "GateOff", −1) |
| `SYSgetPlayerLevel(p)` | 0x802EC380 | LEVEL do registro do jogador p (0x802EB9C4(p+1)) | **FATO** |
| `SYSisGetShinkaDigimon(p, i)` | 0x802EC3E8 | BASE_ISUSE[i] do jogador p | **FATO** |
| `SYSctlShinka(cmd, p, i)` | 0x802EC590 | GETEXP/GETLEVEL: BASE_EXP/BASE_LEVEL[i] se obtida, senão −1; SETSHINKA: 0 se já tinha, senão libera e 1 | **FATO** |
| `SYSctlCardFolder(cmd, i)` | 0x802EC430 | CARDLIST (50 bytes) do jogador 0. KAZU: quantas = 1; SET: 0 se já tinha, senão marca e 1; CLR: o contrário | **FATO** |
| `SYSctlBit(cmd, p, n)` | 0x802EC26C | mensagem GAMERAM CTRLITEM "BIT" | **FATO** a mensagem; **HIPÓTESE** ADD/SET/GET pelo nome (o tratador não foi lido) |
| `SYSctlDeviceFolder(cmd, p, tabela, linha, 0, 0)` | 0x802EBF5C | mensagem GAMERAM CTRLITEM "DEVICE" | **FATO** a mensagem; **HIPÓTESE**: ISMAX = pasta cheia (36), IS = tem, ISEQUIP = equipado, SET = guarda, CLR = tira |
| `SYSctlFlagFolder(cmd, n)` | 0x802EC164 | mensagem GAMERAM CTRLITEM "FLAG" | **FATO** a mensagem; **HIPÓTESE**: IS/SET/CLR no bit n da FLAGFOLDER (os impItem) |
| `MATgetPlayerMatStat(n)` | 0x802ECFB0 → 0x80302DB8 | estado do jogador no volume n: 0 fora, 1 só agora, 2 só antes, 3 antes e agora | **FATO** os valores; **HIPÓTESE**: 1 = entrou, 2 = saiu; n = ordem do volume |
| `MDLgetPosStr(x, y, z)` | 0x802ED8E0 | leva (x, y, z) pela matriz do modelo do contexto → `"V<%f,%f,%f>"` | **FATO**; **HIPÓTESE**: matriz só com posição e giro em Y |
| `MDLgetRotStr(x, y, z)` | 0x802ED9A4 | `"V<x, giro+y, z>"`, giro = atan2(frente.x, frente.z) em graus | **FATO** |
| `MDLisCallName(nome)` | 0x802EDEB0 | compara com o nome da chamada do contexto (+0x34 de 0x80535914) | **FATO** |
| `MDLsetWorldPosTarget(x, y, z)` / `("V<…>")` | 0x802EDEFC / 0x802EDF20 | grava o vetor de saída do contexto (+0x38), se houver | **FATO** |

Campos da GAMERAM (tabela em 0x804EA930, tipo e quantidade): FLAGFOLDER 12 bytes; GATESTAGENAME texto; GATESTAGENO
inteiro (padrão −1); LEVEL e BIT inteiros; CARDLIST 50 bytes; BASE_ISUSE 16 bytes; BASE_LEVEL e BASE_EXP 16 inteiros.

Uso nos scripts do jogo (todas as fases): `SYSctlBit "ADD" p quantia`; `SYSctlDeviceFolder` com ISMAX, ISEQUIP
("armor2" 27..29), IS ("weapon" n), SET/CLR (g_TItemDB/g_SItemDB); `SYSctlFlagFolder` IS/SET/CLR com n de 0 a 11.

## No port

- `src/DW4.Script/Be/BeHost.cs` (`Lobby`): os 17 métodos. O registro do jogador fica em `GameRam.Players[p]` (`BePlayer`):
  `Level` (o cliente liga ao nível), `Forms` (`Evolution`), `Items` (`Inventory`: DEVICEFOLDER, DISKFOLDER, FLAGFOLDER e
  BIT) e `Cards`. O DW4.Script passou a referenciar o DW4.Gameplay.
- `IBeEngine.MatStat` responde o MATgetPlayerMatStat; `IBeModel.Position`/`Yaw` servem o MDLgetPosStr/RotStr;
  `BeHost.CallName`/`WorldPosTarget` são o contexto dos aim_*.lua.
- `gsub` no Lua 4: o boot.lua do lobby troca "p_" por "m_"/"q_" no nome do jogador. Só padrões sem caracteres especiais
  (são os únicos nos scripts do jogo).
- Cliente: o inventário passou a morar na GAMERAM (sobrevive à troca de fase), o LEVEL é o nível do Guilmon e a forma
  inicial é a linha do Guilmon na shinka.
- Teste: `tools/DW4.Tests`, "Jogo: lobby stagml11 (#821)…" roda func/boot/start/first, quest01 e callshop com os chunks
  reais. Sem erro, nenhum método sem resposta, portão "GateOff"/−1, FLG 908 ligada no nível 20, g_cyheal gerado, e as
  chamadas diretas de cada método.

## Visto no log da janela do usuário (antes desta mudança)

A primeira entrada no lobby pelo portal do stagbs01: `boot.lua: attempt to call a nil value` (gsub e os SYS* que faltavam),
`start.lua: attempt to compare nil with number` (SYSgetPlayerLevel), SYSget/setGateStage não implementados. Mesmo assim a
conversa de entrada das Digimon (n_digilff/n_digilfm, textos 36..48) rodou.

## Pendente

- Conferir na janela: NPCs, falas, cura (g_cyheal) e saídas, quando a janela for liberada.
- O tratador do GAMERAM CTRLITEM (efeito exato de BIT/DEVICE/FLAG) não foi lido.
