# Fase 4: digievolução (levantamento)

Leitura estática do main.dol e dos scripts Lua em 2026-10-24. Nada executado. FATO = lido no código ou nos dados; HIPÓTESE = dedução ainda não confirmada.

## Duas etapas (FATO)

1. **Liberar a forma:** o Lua chama be:SYSctlShinka (nativo 0x802EC590) com "SETSHINKA"(jogador, d), que liga BASE_ISUSE[d] e devolve 1 se ela ainda estava bloqueada. "GETEXP" devolve BASE_EXP[d] (−1 se bloqueada) e "GETLEVEL" devolve BASE_LEVEL[d]. A shinka não tem coluna de requisito nem de forma de destino: quem decide é o script.
2. **Trocar de forma:** menu da lobby (beNDMWShopCtrlEvolve, "SQ_SINKAONOPEN"/"SQ_SINKAOFFOPEN"), com as formas liberadas e o nível de cada. A escolha (0x8039CA4C) faz CURRENT2BASE (salva LEVEL, EXP, SKILL[9], UPCNT[11] em BASE_*[atual]), BASE2CURRENT (DIGIMONNO pela MODEL_NAME; carrega os BASE_* da nova forma), RECALCPLAYER, WEAPONEQUIPSTRONG, desequipa MAINSLOT, SUBSLOT, POWERPOCKET e MAINPOCKET e faz RECALC de novo.

- Cada forma tem nível, EXP, skills e UPCNT próprios; uma forma recém-liberada começa no nível 1 com skills 1. BIT e as pastas de itens são da conta e não mudam.
- Permanente até a próxima troca; há volta para as iniciais. Nenhum custo ou duração no fluxo lido (o menu pode ter condição: DESCONHECIDO).

## Quem libera (scripts)

- treasure02/03/04/06/07/08.lua (eventos da lobby ao voltar das masmorras, NPC n_digilfm): somam COUNTERs dos jogadores presentes, escolhem a inicial (0..3) de maior BASE_EXP e liberam uma forma, com um ramo por inicial. HIPÓTESE: cada fim de área libera a evolução da inicial mais usada.
- sinka00..15.lua (falas da Digi-lab, cada um libera a linha NN), sinka.lua (laço) e b0001005..9.lua (menu encadeado; HIPÓTESE: depuração).
- DESCONHECIDO: a tabela completa inicial → forma por área e as condições de % de COUNTER.

## As 16 formas (FATO; área que libera = HIPÓTESE pelos scripts)

| Linha | Forma | IGB (ndmw.afs) | Liberada em |
| --- | --- | --- | --- |
| 0 | アグモン p_agumon | #850 | inicial |
| 1 | ブイモン p_vmon | #863 | inicial |
| 2 | ギルモン p_girumon | #857 | inicial |
| 3 | ドルモン p_dorumon | #855 | inicial |
| 4 | ワーガルルモン p_weregaru | #865 | treasure02/03/07 |
| 5 | ヘラクルカブテリモン p_hekabut | #858 | treasure02/03/07 |
| 6 | ウォーグレイモン p_wargrey | #864 | treasure02/03/07 |
| 7 | クラビスエンジェモン p_angelr | #852 | treasure02/03/07 |
| 8 | ベルゼブモンBM p_beelzeb | #853 | treasure04/07 |
| 9 | アルファモン p_alpha | #851 | treasure04/07, 06 |
| 10 | BLウォーグレイモン p_bwargrey | #854 | treasure08 |
| 11 | IPドラモンFM p_impdrafm | #859 | treasure03/04 |
| 12 | IPドラモンPM p_impdrapm | #860 | treasure08 |
| 13 | メタルガルルモン p_metalgaru | #861 | treasure04/07, 06 |
| 14 | デュークモンCM p_dukecrim | #856 | treasure06 |
| 15 | スサノオモン p_susanoo | #862 | treasure03/04 |

Cada forma tem o próprio IGB (esqueleto, animações e sequências SQ_*), escolhido pela MODEL_NAME. A arma exclusiva de cada uma tem SOUBI = 1 << linha (ex.: 259 para a 14).

## Port

`src/DW4.Gameplay/Evolution.cs`: estado por forma, liberar, trocar e a escolha da arma mais forte (HIPÓTESE: por TSUYOSA). A fatia ainda não troca de modelo (falta exportar o IGB da forma).
