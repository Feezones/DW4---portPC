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
| `MATgetPlayerMatStat(p)` | 0x802ECFB0 → 0x80302DB8 | estado do **jogador p** no volume do gatilho atual (bit p de dois mapas): 0, 1, 2 ou 3 | **FATO** os valores e o argumento (funcHealGate passa o número do jogador); **HIPÓTESE**: 2 = entrou agora (é quem a cura atende), 1 = saiu |
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
- MATgetPlayerMatStat sai do `BeMatEvent` do gatilho em execução; `IBeModel.Position`/`Yaw` servem o MDLgetPosStr/RotStr;
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

## Conferido na janela (2026-10-25, depois da liberação)

- Entrada 0: sem erro nem "não implementado" no log. A conversa de entrada (n_digilff 2 e n_digilfm 0, textos em inglês
  "You must be new to the Digital Security Guard…") roda até o fim, a NPC sai andando (SQ_WALK02) e o controle volta.
- Músicas: AM_LOBBY (#13) e ST_LOBBY (#58) tocam pelo first/start.lua.
- NPCs: o nó de cada n_* no IGB é um igActor (personagem), que o exportador de objetos não desenhava. Agora o
  `fase-exportar` grava `npcs/<nome>` (n_agumon, n_digilff, n_digilfm, n_leomon) e o cliente põe o personagem no objeto,
  com a animação da ACTION da sequência.
- n_digilff/n_digilfm usam `igSkin → igModelViewMatrixBoneSelect` (pele rígida presa a um osso pelo nome, "CharaRoot"),
  que o conversor não lia. Agora lê. **HIPÓTESE**: vértices no espaço desse osso. Na janela, a NPC aparece em pé, com
  textura, e anda.
- Saída matz00: entrando no volume abre a janela "Central Area — Sim/Não"; com Sim, `NEXTSTAGE "StagML12"` carrega a
  Central Area (exportada agora, junto com a stagml02 e a stagml13). FATO pelo script: o matz00 só age com a fila de demo
  vazia (DMgetCount == 0), então não dispara durante a conversa de entrada.
- Cura (mate03.lua → funcHealGate): conferida pelo teste com o script real (HP 100/150 → `GAMERAM ADDHP "I<0>/I<150>/I<1>"`,
  ADDMP, ONCTRL). O cliente agora atende SYSctlPlayer GETMHP/GETMP/GETMMP e GAMERAM ADDHP/ADDMP. Na janela não deu para
  conferir: no lobby não há como perder HP.

## Teleporte, portão, mistura aditiva e efeitos (2026-10-25, só código; janela com o usuário)

- **Teleporte (mate00.lua, g_cytelep1).** FATO pelo script: oferece os campos conforme o enredo (FLG 2 → stag0001 ou
  stag0101 pela FLG 503; FLG 4 → stag0101, 0201; FLG 5 → + 0301; FLG 6/86 → + 0401; FLG 409 → só stag0401 entrada 3, o
  destino da missão). Uma função local recebe trios fase/entrada/texto (`arg.n`), escreve "STCHGFONT01/font0k" e abre
  `SELECTCTRL i_zzstchgcur GENERATE "I<n>"`; espera ISEND/ISSELECTNOW; `DMswtSD GETSELECT`: DMcase 0..3 = NEXTSTAGE da
  opção, DMcase 4 = desistir (volta o fade e o controle). No cliente: `Fatia.ListMenu.cs` (lista com cima/baixo,
  confirmar = ataque, desistir = K/Backspace/B → 4, **HIPÓTESE**). DECISÃO DA FATIA: caixa da Godot (o i_zzstchgcur não foi
  exportado).
- **Portão de volta (mate02.lua, g_cyexit).** Com o portão gravado (SYSsetGateStage no gatedisc.lua da dungeon), mostra o nome
  da dungeon (STCHGFONT00 I<60..>) na janela sim/não e leva à fase e à entrada gravadas. Sem portão ("GateOff") ou com a
  FLG 3/409, não faz nada.
- Teste (com os scripts reais): teleporte com a FLG 4 → 2 opções, a 2ª leva ao stag0201; desistir não troca de fase e devolve
  o controle; portão gravado em dng101b2/2 → NEXTSTAGE dng101b2 com SETGENERATEPOS I<2>.
- **Cones brancos.** O cenário não lia o `igBlendFunctionAttr` (os campos são Enum). No #821 há um só aditivo (#1251: 4/1 =
  SRC_ALPHA/ONE, com igBlendStateAttr ligado) e um normal (#9242: 4/5). Agora o conversor leva o destino da mistura pela
  árvore, o glTF marca o material com "_add" (BLEND) e o cliente usa mistura aditiva, sem luz e sem gravar profundidade.
  No lobby, 8 materiais (luzes, ícones, faixas do chão); 9, 10 e 7 nas stagml02/12/13. **HIPÓTESE** (a conferir na janela):
  os cones brancos são essas peças aditivas desenhadas opacas.
- **mlBEEP01.** Não é música: é efeito do banco da fase (stagml12 #824, beSoundData cue 0xA9250900 → A925_09.wav, já
  exportado). O SOUND PLAY do cliente agora toca efeitos do banco quando o nome não é música.

## Conferido na janela (2026-10-25, depois do teste do usuário)

- Cones: continuam branco-azulados. **Não eram** as peças aditivas: são 8 peças de 105 vértices (15 × 38 × 15) com o atlas
  ML01GR00_n, e os UV caem na faixa de degradê branco→azul do atlas (x ≈ 128..143, y 65..127). **HIPÓTESE**: são cristais
  assim no original, não erro do conversor. A mistura aditiva das outras peças (faixas, brilhos) ficou certa na janela.
- Teleporte: com DW4_FLG=4, entrando no mate00 abre a lista "Death Valley / Dry Land" (textos do jogo); confirmar a
  primeira leva ao stag0101 (`NEXTSTAGE "stag0101"`). Sem as flags do enredo (jogo novo) o teleporte não oferece nada,
  como manda o script.
- mlBEEP01: toca no fim da conversa de entrada do lobby (banco #820, A925_09.wav), sem aviso no log.
- Portão (mate02): só pelo teste; na janela precisaria de um portão gravado numa dungeon.

## Pendente

- O tratador do GAMERAM CTRLITEM (efeito exato de BIT/DEVICE/FLAG) não foi lido.
