# Lançador de barril da ladeira (stag0102, g_barrelshotz01)

Relato (0.6.19): na ladeira da stag0102 os "baús" abrem (`MODELCTRL g_barrelshotz01 0/1 SQ_FIREDEMO`, `-1 SQ_POWERON` no F3,
jogador em (57,6, 112,8, 25,5), gatilho `matf00`), mas nenhum barril sai.

## Como é no original

- **FATO (matf00.lua, `dw4 lua-trace ndmw.afs 621 matf00.lua MATisPlayer=1`):** ao entrar no gatilho, `ONCTRL`, demo de câmera
  "Slope", `SQ_FIREDEMO` alternando os lançadores 0 e 1 (quadros 2, 12, 54, 74, 106, 126, 158, 179, 210, 231, 262) e depois
  `SQ_POWERON`. Fora do gatilho, `SQ_POWEROF`. Os lançadores nascem em (32,5, 118, 17,5) e (32,5, 118, 33), giro 90.
- **FATO (seq do g_barrelshotz01):** `SQ_FIREDEMO` = ação BLST01. Ligado, o mapa MAP00 "NEAR" (300/400/500) sorteia
  `SQ_FIRE`/`SQ_FIRE02`/`SQ_ON`/`SQ_ONGO`; com o rótulo ON elas tocam BLST01 e, no ONGO, BLST02. HP: `hit_g_barrelshotz01.lua`
  tira 50 por golpe comum.
- **FATO (disparos.json, beActionStarterInfo do #621):** a BLST01 gera o **objeto** `g_bbarrelmz01` e a BLST02 o `g_bbarrelgz01`
  (mais o efeito `x_bbarrelsz01`), no quadro 26, em (0, 8, 6) no espaço do lançador, na `SQ_ATCK01`.
- **FATO (seq do g_bbarrelmz01/gz01):** `SQ_ATCK01` = WAIT01 (juntas 40..100 em laço, `WAITCHECK "Frame" 250`), `HITBOX 70122`
  (0, −2, 0) (8, 6, 6) por 1000 quadros, `MOVE "Push" 0.9 100`, `MOVE "Jump" 2 2`; no fim da ação `SQ_DDMG` (caixa 70122
  15 × 15 × 15 por 6 quadros, DEAD01 some em 12/20) → `SQ_ERAS`. `SQ_FALL` é o mesmo com `MOVE "Fall" 1 2`.
  Pela conta do executor do MOVE (MotionChannels), o "Push" freia só 0,9/5000 por quadro²: rola a ~0,9 por quadro (≈ 27/s) e
  anda ~111 até quebrar.
- **FATO (eneatk 122):** TYPE 2, TSUYOSA 5 (5% do HP máximo), MAHIRITSU 5. **FATO (`hit_g_bbarrelmz01.lua`):** golpe comum
  tira 100 e põe `SQ_DDMG`; técnica (40040..40099) não faz nada.
- **HIPÓTESE:** o barril segue o chão (o código de chão dos objetos não foi lido): desce colado na ladeira e, sem chão logo
  abaixo, cai com a conta do `MOVE "Fall" 1 2` da `SQ_FALL`.

## O que faltava no port

1. O `fase-exportar` só exportava como objeto os atores KIND_OBJECT/KIND_ENEMY; os barris (gerados por disparador) ficavam de
   fora, sem modelo nem sequência.
2. O cliente só lia disparadores `x_*` (projéteis); o `g_*` do disparador era ignorado.

## O que mudou

- `fase-exportar`: os `g_*` que aparecem como nome de beActionStarterInfo e são atores da fase entram em `objetos/`
  (modelo no objetos.json e `objetos/<nome>/*_seq.json`). Só a stag0102 tem disparador `g_*`.
- Cliente (`Fatia.Barrels.cs`): o disparador `g_*` com modelo nos objetos nasce como objeto (`Spawn` + corpo para golpe, sem
  segurar o jogador) com o giro do lançador. O barril rola pelo MOVE seguindo o chão, fere o jogador pela caixa 70122
  (`CheckFloorHit`), quebra no golpe (hit_*.lua) ou no fim da ação e some no `SQ_ERAS`.
- **DECISÃO DO PORT:** barril que bate numa parede quebra (`SQ_DDMG`) em vez de ficar parado nela.
- Depuração: `DW4_DIAG_BARRIL=1` (nascimento, troca de sequência, posição e HP do jogador a cada 15 quadros).

## Conferido sem janela (2026-11-06)

- Godot `--headless`, `DW4_FASE=stag0102 DW4_INICIO=70,110,33`, dados de `D:\dw4-work\fases` e também um DW4.exe Release
  exportado com os pacotes .pcz do jogador e o `fases-stag0102.pcz` novo: os barris nascem em (38, 126, 18/33), descem de
  y 126 a 85 até x 150 e quebram lá (`SQ_DDMG`); o jogador na faixa leva 6–7 por barril (150 → 86 em 30 s); com golpe,
  `hit_g_bbarrelmz01.lua` quebra o barril na hora.
- O pacote da fase precisa ser regerado (`dw4 fase-exportar … stag0102` e `dw4 empacotar`) para ter `objetos/g_bbarrelmz01` e
  `objetos/g_bbarrelgz01`.
