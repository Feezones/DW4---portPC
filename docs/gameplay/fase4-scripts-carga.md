# Fase 4: como o motor carrega e executa os scripts Lua de uma fase

Levantamento estático em 2026-10-25 (listagens fora do repositório em `D:\dw4-work\lua\dng1\`; `main.dol` só lido).

## Ordem fixa por fase (FATO)

Máquina de estados do carregador em `0x802F8A64` (r29 = base de strings `0x80425590`, estado em `this+0x3C`):

| Estado | O que faz | Endereço |
|---|---|---|
| 3 | `sprintf("%s/lua", pasta da fase)`, executa `func.lua` e depois `boot.lua` | `0x802F96F8`, `0x802F9714`, `0x802F9724` |
| 4..6 | `LAYERCTL` / `TEXTCTL` / `LOADING`: carrega os `.igb` que o `boot.lua` listou | |
| 7 | executa `start.lua` | `0x802F9B48` |
| 9 | marca uma flag | |
| depois | executa `first.lua`; estado = −1 | `0x802F9BAC` |

Ordem: **func.lua → boot.lua → carga dos IGB da lista CFL → start.lua → first.lua**. Todos na mesma VM (as globais de
`func.lua` são vistas pelos seguintes). HIPÓTESE: `0x80301FA4(lua, nome, pasta)` carrega e executa um `beLuaData` pelo nome.

## Papel de cada script (FATO, dng1 = ndmw #179)

- Os scripts só existem no IGB principal de cada fase; #180..#183 não têm nenhum. #179 tem 55 `beLuaData`.
- `func.lua`: biblioteca comum (17 funções: `funcOneMessage`, `funcOpenMessage`, `funcChoiceMessage`, `funcCloseMessage`,
  `funcStartLayerSet`, `funcFirstLayerSet`, `funcPlayerStatusSet`, `funcPlayerStatusGenerate`, `funcPlayerSet`,
  `funcStageChange`, `funcStageIn`, `funcTechWazaHitCheck`, `funcTameWazaHitCheck`, `funcOpenDigimonInfoWin`,
  `funcSecurityGate`, `funcHealGate`, `_ERRORMESSAGE`) e globais `g_delayTime`, `g_talk`, `g_GateStage`, `g_GatePos`,
  `g_GateOn`. Duas versões: uma em 94 IGB de fase, outra em #800, #804, #815, #833.
- `boot.lua`: sem lógica de jogo; `be:CFLclear()` e monta a lista de `.igb` a carregar: câmera (`/separate/` + câmera normal
  ou `L` por `SYSgetCameraMode`, `ACMR_pack.igb`), geradores por `SYSgetDifficulty` (`generatorN|H|V.igb`), um
  `_common/<SYSgetPlayerName>.igb` por jogador presente (`SYSisJoinPlayer(p)`, p = 0..3), a arma (`SYSgetWeaponName`) e o
  pacote do idioma por `SYSgetCountry` (`USA_` etc.) com as texturas do HUD.
- `start.lua`: camadas (`funcStartLayerSet`), cor da luz por `SYSgetLightMode`, `funcPlayerSet`, contadores, geração dos
  objetos da fase (piso de dano, portas, gravador), geradores `GBOX-%02d`, portas por FLG 102/106, fade e
  `funcPlayerStatusGenerate`.
- `first.lua`: entrada da fase: na primeira vez, demo de câmera `entry`, `SQ_DEMOWALK`/`SQ_DEMOWAIT`, músicas `AM_DUNGE` e
  `DG_CAVE1`, câmera por volumes; depois libera o controle (`SWITCHCTRL ALLON`), `funcFirstLayerSet` e fade.
- Os demais entram por eventos: `be:DMsetLua("x.lua")` (520 usos, agenda o próximo script do diretor de demo), geradores
  (`_callLua "gene01.lua"`), colisão (`_luaBHit`/`_luaWHit` → `hit_*.lua`) e o formato `"%s_%s.lua"` (código em `0x8030B808`).

## No port

`dw4 lua-trace <ndmw.afs> 179` roda essa sequência na VM própria e imprime cada chamada `be:*`: é o roteiro para
implementar a API, método por método.

## Trace da dng1 (2026-10-25, `lua-trace` no PC; saída em `D:\dw4-work\lua\dng1\trace-179*.txt`, fora do repo)

Valores simulados (escolhidos pela listagem do `boot.lua`): `SYSgetCountry` = 1 (**FATO**: número, índice em
JPN_, USA_, ENG_, FRA_, ITA_, GER_, SPN_ começando em 0), `SYSgetPlayerName` = `p_girumon`, `SYSgetDifficulty` = 0 (N),
`SYSgetWeaponName` = `w_sword01` (pedido para 3 posições de arma), `CFLgetFolderName` = `dng101b1`.

- Resultado: os quatro scripts rodam sem erro (≈2.400 instruções). Globais lidas sem definição: `NULL` (usada como nil nos
  argumentos) e `g_delayTime` (lida antes de ser atribuída). **FATO**
- `start.lua`: `COUNTERget(2)` diferente de 101 → `COUNTERset(2, 101)` e `FLGset(100..299, 0)`. **HIPÓTESE forte**: o contador 2
  guarda a área atual (101 = dng1) e, ao entrar numa área nova, as flags 100..299 da área são zeradas.
- `start.lua` gera os objetos da fase com posição em texto (`"V<220,40,-200>/V<0,-90,0>"`): 4 `g_ddamagefloor`, 3 `g_d1door`,
  `n_recorderdng`; inicia os geradores `GBOX-01..05` só se as flags 107..111 estiverem desligadas; portas por flags 102/106.
- `first.lua`: abre o painel de status (`i_stpnl00..05`, o rosto pelo nome do jogador), habilita o controle
  (`PADINPUT ENABLE 30`), `DMrun`, e decide pela `PLAYERgetPos()`: **FATO** (JMPNE 0) valor 0 → entrada com demo de câmera
  `entry`, `SQ_DEMOWALK 15`, `SQ_DEMOWAIT 60`, `DG_CAVE1 120`, `SETBOXCAMERA`; outro valor → sem demo. Em ambos: músicas
  `AM_DUNGE` e `DG_CAVE1`, `SWITCHCTRL ALLON`, `LAYERCTL REMOVE "LOADING"`, `LAYERCTL SWITCH "ALL/DISPON"`, fade
  `i_fadewn01 SQ_BLIN30 5`. **HIPÓTESE**: `PLAYERgetPos` = índice do ponto de entrada (0 = porta inicial).

## API be:* (2026-10-25, leitura estática do main.dol; tabela completa fora do repo em `D:\dw4-work\lua\dng1\be_metodos.txt`)

- **FATO** Registro: `0x802EB788` chama `0x8026C068` com o despachante `0x802EA1FC`, nomes em `0x804DCE50` (107 distintos) e
  116 descritores em `0x804242AC`. Descritor: palavra `w` (id = `w & 0xFF`, nº de parâmetros = `(w >> 8) & 31`, nome =
  `w >> 13`), palavra de retorno e um tipo por parâmetro (5 inteiro, 9 bool, 0xB float, 0xD texto, 7 inteiro de 64 bits).
  O mesmo nome com nº de argumentos diferente tem ids diferentes (sobrecarga).
- **FATO** `be:sd(classe, nome, índice, comando[, argumentos][, atraso])` (ids 29..32) manda a mensagem pelo beMessenger
  (`*0x80535904`) **na hora**; com o número de 64 bits, `0x803061BC` soma n × 16.666.666 ns ao relógio: o comando é entregue
  **n quadros de 60 Hz depois**.
- **FATO** `be:DMsetSD` (ids 40..43, mesma assinatura) só enfileira no beDemoManager (`*0x8053590C`, `0x802F7580`, tipo 0);
  `be:DMchkSD(classe, nome, índice, consulta[, arg], condição)` enfileira tipo 2 (`"!=0"`, `"==1"`) ou 3 (`"==0"`, `"!=1"`)
  (`0x802ED140`); `be:DMrun` entrega a lista ao executor e zera a montagem (`0x802F7BDC`), sem bloquear o script.
  **HIPÓTESE** (executor não lido): o chk segura a fila até a condição valer; o atraso de um set vai para o beMessenger sem
  segurar a fila; `DMswtSD`/`DMcase(n)` condicionam blocos; `DMgetCount` = entradas pendentes; o script de `DMsetLua` roda
  quando a fila esvazia.
- **FATO** Argumentos em texto (`0x802ECA74`): separados por `/`; `letra<a,b,c>` é token tipado (`V` vetor, `I` inteiro).
  `GENERATE "V<x,y,z>/V<rx,ry,rz>"` = posição e rotação (**HIPÓTESE**: graus).
- **FATO** GAMERAM (tabela em `0x804EA930`): FLAG = 1024 bytes, COUNTER = 16 inteiros, NINZU, DIFFICULTY, CAMERAMODE,
  LIGHTMODE, BKCHIP*, GATESTAGE*, PLAYERNAME. `FLGisOn(n)` devolve **número 0/1, nunca nil** (`FLGisOn(1000)` = NINZU ≠ 0);
  `COUNTERget` devolve 0 quando não gravado. `PLAYERgetPos` manda `PLAYERPOINT GETGENERATEPOS` e devolve o ponto de entrada.

Implementação: `src/DW4.Script/Be` (`BeHost`, `BeMessenger`, `DemoManager`, `GameRam`, `StageScripts`); o jogo atende os
comandos por `IBeEngine`. A fatia (`src/DW4.Client/Fatia.Scripts.cs`) já atende `STAGECTL PADINPUT`, `SOUND PLAY/BGMSTOP` e
`GETGENERATEPOS`; o resto aparece no HUD.
