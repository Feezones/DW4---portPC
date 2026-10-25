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
