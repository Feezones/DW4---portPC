# Fase 5: fluxo do enredo pelas flags (levantamento)

Leitura estática dos scripts Lua das 98 fases exportadas em 2026-10-25. São 7300 scripts. Cada FLGset/FLGisOn com número fixo
e cada nome de fase citado foi levantado a partir das listagens (casadas por SHA-256). Nada foi executado. O port roda esses
mesmos scripts (ADR 0002), então o fluxo do enredo sai deles: basta o be:* e os gatilhos responderem como no jogo.

## Começo de jogo (FATO)

- stagIS08 (logos) → stagIS09 (abertura) → stagIS10 (título, telas do motor).
- A gameStart.lua refaz as flags de trabalho a partir das permanentes (ver fase4-levantamento-missoes.md).
- A saída da stagIS10 vai para o lobby stagML11, entrada 3.

## Saída principal do lobby (stagml11 mate00, FATO)

- Os blocos são testados em ordem, cada um só sem missão ativa (FLG 409 desligada). O teste "lobby" do DW4.Tests confere:
  com FLG 2 desligada e FLG 4 ligada, o menu tem 2 destinos.
  - FLG 2: um destino só. stag0001 com a FLG 503 desligada (primeira vez), senão stag0101.
  - FLG 4: stag0101 ou stag0201.
  - FLG 5: mais a stag0301.
  - FLG 6: mais a stag0401.
  - Com missão ativa: o destino da missão (ex.: stag0401, entrada 3).
- Quem liga:
  - FLG 2 e 3: stagml02 (matf00, n_ofanim_00, a base da Ofanimon).
  - FLG 4: stagml02 first.lua.
  - FLG 5 e 6: stagml02 matf02 e matf00.
  - FLG 503: stag0001 first.lua.
  - FLG 86: stagbs06 hit_f_devl3 (último chefe) e stagml11 start.lua.
  - stag0000 time01..06/hard/veryhard ligam blocos inteiros. HIPÓTESE: atalhos de depuração do desenvolvedor.
- Outras saídas do lobby:
  - mate01 → stagML02, pela FLG 424.
  - mate02 → volta à masmorra ou campo do portão (SYSgetGateStageName), pela FLG 3 e 409.
  - matz00/matz01 → stagML12 e stagML13.
- Missões (quest01..06, treasure01..06 em stagml11/12) ligam 409 (missão ativa) e 424. O fim é o end_g_000bcaps.lua das
  masmorras, que testa a 508 e volta ao stagML11.

## Scripts que testam flags e levam a outra fase (FATO)

Primeiras flags testadas (até 12) e fases citadas (até 10) por script. stag0000 fica de fora.

| Fase:script | Flags testadas | Fases citadas |
|---|---|---|
| dng101b1:end_g_000bcaps | 508 | stagML11 |
| dng101b2:end_g_000bcaps | 508 | stagML11 |
| dng102b1:end_g_000bcaps | 508 | stagML11 |
| dng102b2:end_g_000bcaps | 508 | stagML11 |
| dng102b3:end_g_000bcaps | 508 | stagML11 |
| dng103f1:end_g_000bcaps | 508 | stagML11 |
| dng103f2:end_g_000bcaps | 508 | stagML11 |
| dng204b1:end_g_000bcaps | 508 | stagML11 |
| dng204b2:end_g_000bcaps | 508 | stagML11 |
| dng204b2:first | 301 | dng204b2 |
| dng205b1:end_g_000bcaps | 508 | stagML11 |
| dng205b2:end_g_000bcaps | 508 | stagML11 |
| dng205b3:end_g_000bcaps | 508 | stagML11 |
| dng206f1:end_g_000bcaps | 508 | stagML11 |
| dng206f2:end_g_000bcaps | 508 | stagML11 |
| dng307b1:end_g_000bcaps | 508 | stagML11 |
| dng307b2:end_g_000bcaps | 508 | stagML11 |
| dng308b1:end_g_000bcaps | 508 | stagML11 |
| dng308b2:end_g_000bcaps | 508 | stagML11 |
| dng308b3:end_g_000bcaps | 508 | stagML11 |
| dng309b1:end_g_000bcaps | 508 | stagML11 |
| dng309b2:end_g_000bcaps | 508 | stagML11 |
| dng309b3:end_g_000bcaps | 508 | stagML11 |
| dng309b4:end_g_000bcaps | 508 | stagML11 |
| dng310f1:end_g_000bcaps | 508 | stagML11 |
| dng310f2:end_g_000bcaps | 508 | stagML11 |
| dng310f3:end_g_000bcaps | 508 | stagML11 |
| dng411b1:end_g_000bcaps | 508 | stagML11 |
| dng411b2:end_g_000bcaps | 508 | stagML11 |
| dng411b3:end_g_000bcaps | 508 | stagML11 |
| dng412b1:end_g_000bcaps | 508 | stagML11 |
| dng412b2:end_g_000bcaps | 508 | stagML11 |
| dng412b3:end_g_000bcaps | 508 | stagML11 |
| dng412b4:end_g_000bcaps | 508 | stagML11 |
| dngq1b1e:end_g_000bcaps | 508 | stagML11 |
| dngq1b1e:matz01 | 115 | stag0401 |
| dngq1b1e:matz02 | 115 | dngq1b2e |
| dngq1b2e:end_g_000bcaps | 508 | stagML11 |
| dngq1b2e:matz01 | 115 | dngq1b1e |
| dngq2b1e:end_g_000bcaps | 508 | stagML11 |
| dngq2b2e:end_g_000bcaps | 508 | stagML11 |
| dngq2b2e:matz02 | 144 | stagML11 |
| dngq2b2e:matz03 | 144 | stagML11 |
| dngq2b2h:end_g_000bcaps | 508 | stagML11 |
| dngq2b2h:matz02 | 144 | stagML11 |
| dngq2b2h:matz03 | 144 | stagML11 |
| dngq3b1e:hit_p_agumon | 121 | stagML11 |
| dngq3b1e:hit_p_alpha | 121 | stagML11 |
| dngq3b1e:hit_p_angelr | 121 | stagML11 |
| dngq3b1e:hit_p_beelzeb | 121 | stagML11 |
| dngq3b1e:hit_p_bwargrey | 121 | stagML11 |
| dngq3b1e:hit_p_dorumon | 121 | stagML11 |
| dngq3b1e:hit_p_dukecrim | 121 | stagML11 |
| dngq3b1e:hit_p_girumon | 121 | stagML11 |
| dngq3b1e:hit_p_hekabut | 121 | stagML11 |
| dngq3b1e:hit_p_impdrafm | 121 | stagML11 |
| dngq3b1e:hit_p_impdrapm | 121 | stagML11 |
| dngq3b1e:hit_p_metalgaru | 121 | stagML11 |
| dngq3b1e:hit_p_susanoo | 121 | stagML11 |
| dngq3b1e:hit_p_vmon | 121 | stagML11 |
| dngq3b1e:hit_p_wargrey | 121 | stagML11 |
| dngq3b1e:hit_p_weregaru | 121 | stagML11 |
| dngq3b1e:matz01 | 121 | stagML11 |
| dngq3b1e:matz02 | 121 | dngq3b2e |
| dngq3b2e:hit_p_agumon | 121 | stagML11 |
| dngq3b2e:hit_p_alpha | 121 | stagML11 |
| dngq3b2e:hit_p_angelr | 121 | stagML11 |
| dngq3b2e:hit_p_beelzeb | 121 | stagML11 |
| dngq3b2e:hit_p_bwargrey | 121 | stagML11 |
| dngq3b2e:hit_p_dorumon | 121 | stagML11 |
| dngq3b2e:hit_p_dukecrim | 121 | stagML11 |
| dngq3b2e:hit_p_girumon | 121 | stagML11 |
| dngq3b2e:hit_p_hekabut | 121 | stagML11 |
| dngq3b2e:hit_p_impdrafm | 121 | stagML11 |
| dngq3b2e:hit_p_impdrapm | 121 | stagML11 |
| dngq3b2e:hit_p_metalgaru | 121 | stagML11 |
| dngq3b2e:hit_p_susanoo | 121 | stagML11 |
| dngq3b2e:hit_p_vmon | 121 | stagML11 |
| dngq3b2e:hit_p_wargrey | 121 | stagML11 |
| dngq3b2e:hit_p_weregaru | 121 | stagML11 |
| dngq3b2e:matz01 | 121 | dngq3b1e |
| dngq5b1e:end_g_000bcaps | 508 | stagML11 |
| dngq5b1h:end_g_000bcaps | 508 | stagML11 |
| dngq5b1v:end_g_000bcaps | 508 | stagML11 |
| dngq6b01:end_g_000bcaps | 508 | stagML11 |
| dngq7b1e:end_g_000bcaps | 508 | stagML11 |
| dngq7b2e:end_g_000bcaps | 508 | stagML11 |
| dngq8b1e:end_g_000bcaps | 508 | stagML11 |
| dngq8b2e:end_g_000bcaps | 508 | stagML11 |
| dngq8b3e:end_g_000bcaps | 508 | stagML11 |
| dngq8b3h:end_g_000bcaps | 508 | stagML11 |
| dngq8b3v:end_g_000bcaps | 508 | stagML11 |
| stag0001:ded_g_tcounter00 | 382 |  |
| stag0001:first | 503, 382 |  |
| stag0001:gene00 | 382 |  |
| stag0001:gene01 | 382 |  |
| stag0001:gene02 | 382 |  |
| stag0101:end_g_000bcaps | 508 | stagML11 |
| stag0101:matx01 | 507 | stag0101 |
| stag0102:end_g_000bcaps | 508 | stagML11 |
| stag0102:matx01 | 507 | stag0102 |
| stag0103:first | 89, 90, 91, 92, 93, 94, 95, 96, 97, 98 |  |
| stag0103:matf00 | 489 | stag0102 |
| stag0104:end_g_000bcaps | 508 | stagML11 |
| stag0201:end_g_000bcaps | 508 | stagML11 |
| stag0202:end_g_000bcaps | 508 | stagML11 |
| stag0204:end_g_000bcaps | 508 | stagML11 |
| stag0205:end_g_000bcaps | 508 | stagML11 |
| stag0301:end_g_000bcaps | 508 | stagML11 |
| stag0302:end_g_000bcaps | 508 | stagML11 |
| stag0303:end_g_000bcaps | 508 | stagML11 |
| stag0304:end_g_000bcaps | 508 | stagML11 |
| stag0401:end_g_000bcaps | 508 | stagML11 |
| stag0401:mate00 | 905 | stag0403 |
| stag0403:end_g_000bcaps | 508 | stagML11 |
| stag0404:end_g_000bcaps | 508 | stagML11 |
| stagbs01:end_g_000bcaps | 508 | stagML11 |
| stagbs02:end_g_000bcaps | 508 | stagML11 |
| stagbs03:end_g_000bcaps | 508 | stagML11 |
| stagbs04:end_g_000bcaps | 508 | stagML11 |
| stagbs05:end_g_000bcaps | 508 | stagML11 |
| stagbs06:end_g_000bcaps | 508 | stagML11 |
| staggf01:end_g_000bcaps | 508 | stagML11 |
| staggf02:end_g_000bcaps | 508 | stagML11 |
| staggf03:end_g_000bcaps | 508 | stagML11 |
| staggf03:math00 | 20 | stagML02 |
| staggf04:end_g_000bcaps | 508 | stagML11 |
| stagml11:mate00 | 2, 409, 503, 4, 5, 6, 86 | stag0001, stag0101, stag0201, stag0301, stag0401 |
| stagml11:mate01 | 424 | stagML02 |
| stagml11:mate02 | 3, 409 | dng101b2, dng102b2, dng102b3, dng103f2, dng204b2, dng205b2, dng205b3, dng206f2, dng307b2, dng308b2 |
| stagml12:demo_digilab | 3 | stagML12, stagML14 |
| stagml12:hit_g_evenconsz05 | 321, 331, 332, 3, 324 | stagML02 |
| stagml12:mate00 | 336 | stagML13 |
| stagml12:shopexit | 364, 3 |  |
| stagml13:mate00 | 336 | stagML12 |
| stagml13:shopexit | 364, 3 |  |

## Onde essas flags são ligadas (FLGset n 1, FATO)

Até 6 lugares por flag, sem stag0000.

| Flag | Ligada em |
|---|---|
| 2 | stagml02:matf00, stagml02:n_ofanim_00 |
| 3 | stagml02:matf00, stagml02:n_ofanim_00 |
| 4 | stagml02:first |
| 5 | stagml02:matf02 |
| 6 | stagml02:matf00 |
| 20 | staggf03:math00 |
| 86 | stagbs06:hit_f_devl3, stagml11:start |
| 89 | stag0103:hit_g_numeprisonz01 |
| 90 | stag0103:hit_g_numeprisonz01 |
| 91 | stag0103:hit_g_numeprisonz01 |
| 92 | stag0103:hit_g_numeprisonz01 |
| 93 | stag0103:hit_g_numeprisonz01 |
| 94 | stag0103:hit_g_numeprisonz01 |
| 95 | stag0103:hit_g_numeprisonz01 |
| 96 | stag0103:hit_g_numeprisonz01 |
| 97 | stag0103:hit_g_numeprisonz01 |
| 98 | stag0103:hit_g_numeprisonz01 |
| 115 | dng101b2:gene04, dng102b2:gene01, dng103f1:gbox03, dng204b1:gbox02, dng205b1:gbox01, dng206f1:gbox06 … (+15) |
| 121 | dng101b2:gbox02, dng102b2:gene07, dng103f1:hit_g_dbutton1, dng204b1:gbox08, dng205b1:gbox07, dng206f1:hit_g_dbutton1 … (+16) |
| 144 | dng102b3:gene06, dng103f2:gbox06, dng204b2:gene17, dng205b2:gene12, dng206f2:gene13, dng308b3:gene06 … (+28) |
| 301 | dng204b2:first |
| 321 | stagml13:hit_g_junkconsz05 |
| 324 | stagml12:hit_g_evenconsz05 |
| 331 | stagml12:hit_g_evenconsz05 |
| 332 | stagml12:hit_g_evenconsz05 |
| 336 | stagml13:mate00 |
| 364 | stagml12:demo_digilab |
| 409 | stagml11:quest01, stagml11:quest02, stagml11:quest03, stagml11:quest04, stagml11:treasure01, stagml11:treasure02 … (+6) |
| 424 | stagml11:quest01, stagml11:quest02, stagml11:quest03, stagml11:quest04, stagml11:treasure01, stagml11:treasure02 … (+5) |
| 489 | stag0103:hit_g_facttowerz06, stag0103:hit_g_shelfz06, stag0103:matf00 |
| 503 | stag0001:first |
| 507 | stag0101:matx01, stag0102:matx01 |
| 508 | stagml11:first |
| 905 | stag0401:mate00, stag0401:start |

## HIPÓTESES e DESCONHECIDO

- Flags ligadas com valor calculado (GETLOCAL) ou por laço ficam de fora (o gameStart.lua usa laços).
- Os significados das flags vêm do contexto dos scripts; o nome de cada uma não existe no jogo.
- Não foi conferido na janela se o primeiro caminho (lobby → stagML02 → Ofanimon → stag0001) roda inteiro no port.

## Final do jogo (FATO, 2026-10-25)

- O hit_f_devl3.lua (último chefe, stagbs06) liga as FLG 70, 86 e 610.
- A saída matz01 da stagbs06 leva à stagIS20.
- A stagIS20 é a tela de créditos:
  - o first.lua toca "STAFFROL" (ndmw #63, ~2,7 min) e abre j_endback e j_endroll (créditos rolando, actors 2D não
    exportados);
  - espera o j_endroll fechar e manda NEXTSTAGE "stagML11" com SETGENERATEPOS I<0>;
  - o jogo continua no lobby depois do final.
- No port: tela de fim com a música dos créditos (pula com Enter depois de 5 s) e volta ao lobby, entrada 0
  (`Fatia.Ending.cs`).
- A queda do jogador segue a decisão em `Fatia.Death.cs` (destino original desconhecido).
