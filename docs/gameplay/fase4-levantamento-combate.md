# Fase 4: levantamento do combate (2026-10-24)

Leitura estática: nada extraído do jogo foi executado. Saídas de trabalho ficam em `D:\dw4-work\fase4\` e não vão para o Git.

## Onde estão as regras

| O quê | Onde | Confiança |
|---|---|---|
| Números (vida, força, acerto, crítico, resistências, experiência, preços) | 12 tabelas `beDataBase` dentro de cada IGB de fase do area.afs; iguais em #3 e #441 | FATO |
| Fórmulas (dano, acerto, crítico, estados, experiência) | Código nativo do main.dol, atrás dos comandos FightCom (`P2EDAMAGE`, `E2PDAMAGE`, `P2EISHITCHECK`, `P2EISCRITICAL`, `P2EEXPUP`…) e das funções Lua `BLT*`/`BLP*` | FATO (onde); fórmula DESCONHECIDA |
| Encadeamento das reações | Scripts Lua `hit_e_*.lua` | FATO |

## Tabelas (`dw4 igb-db area.afs 3 <pasta>`)

Layout em `tools/DW4.Formats/Igb/IgbDatabase.cs`. No area #3 (beDBManagerInfo → beDataBase):

| Tabela | Objetos | Linhas | Conteúdo |
|---|---|---|---|
| weapon | #1959 → #2094 | 277 (74 modelos × variantes α..ε) | DELAY, MEICHURITSU (acerto), CHIMEIRITSU (crítico), MAHIRITSU (paralisia), SKILL, TSUYOSA, KASHIKOSA, SEISHIN, SUBAYASA, HANBAI/KAITORI, SKILLPOINT, SOUBI, NAME, MODEL, SHUSEINOURYOKU, TXT |
| enemy | #1379 → #1372 | 649 (49 modelos) | CHIMEIRITSU, MAHIRITSU, RARITY, TSUYOSA, MAMORI, KASHIKOSA, SEISHIN, SUBAYASA, RES_FIRE/ICE/THUNDER/DARK/STUN/POISON, EXP, HPMAX, MODEL |
| eneatk | #1393 → #1406 | 134 | TYPE, acerto, crítico, KOUKARANK, GEDOKU, SEQUENCE, TSUYOSA, ZOKUSEI (elemento), KOUKA (estado) |
| shinka | #1913 → #1918 | 16 | atributos iniciais e crescimento dos Digimon jogáveis, armas iniciais, técnicas |
| pcTech, armor1, armor2, abChip, conItem, insItem, impItem, wazaPanel | ver `igb-db` | 100, 32, 45, 186, 12, 26, –, 10 | técnicas, armaduras, chips, consumíveis, itens |

Exemplos (FATO): w_sword01 = ロングソードα..ε, TSUYOSA 60/81/109/148/199, DELAY 30, acerto 70, crítico 4. Guilmon (p_girumon): STR 55, AC 120, INT 70, MND 65, DEX 60, MHP 150 (+90 por nível), MMP 80 (+120).

## Força do inimigo por dificuldade

- FATO: `boot.lua` carrega `generatorN.igb`, `generatorH.igb` ou `generatorV.igb` conforme `SYSgetDifficulty()` (0, 1, 2). Na dng1: #184 (N), #183 (H) e #185 (V).
- FATO: Int@7 dos `GENE-*` vale 13 (N), 17 (H) e 21 (V), e todos caem no bloco de linhas do e_nume (12–23) na tabela enemy; `GBOX-*` = 370 = g_box.
- FATO: e_nume 12–15 tem HP 52–74; 16–19, 612–689; 20–23, 2436–2704.
- **HIPÓTESE forte:** Int@7 é a linha da tabela enemy. A dificuldade só escolhe o arquivo de geradores, e a progressão dentro da masmorra usa a linha seguinte do mesmo bloco.
- FATO: cada gerador tem 4 `beGeneraterPlayerData` (pares como (5,5) (7,5) (9,5) (12,5)), e cada um tem listas de itens por dificuldade (n/h/v) apontando para linha de tabela (weapon, armor, conItem…).
- **HIPÓTESE:** os 4 PlayerData correspondem a 1–4 jogadores (quantidade de inimigos) e as listas de itens são os drops. Isso é a base de fidelidade para a party online.

## Fluxo de um golpe recebido (hit_e_nume.lua, FATO)

1. Bala "PCBA" → SQ_PUSHED. `BLTisHitCheck` = 0 → SQ_GUARD.
2. Senão: `BLTisCritical`, `BLTgetDamageValue`; estados (sono, prisão, ataque e força reduzidos) com `BLTget*Time`.
3. `BLTcalcSkillUp`, `MDLaddHp(-dano)`, `MDLdspDamage`.
4. `BLTgetDamageRank` 0–3 escolhe SQ_SDMG, SQ_MDMG, SQ_LDMG ou SQ_DDMG, no bloco do elemento do golpe (`BLTgetAttributeStr`: common, fire, ice, thunder, dark).
5. `MDLdspExp`, `BLTcalcEXPUp`, atordoamento e veneno.

## Código nativo (main.dol, desmontagem estática)

- FATO: o enum Alchemy "FightCom" tem 22 nomes em 0x804E3F38 (E2PISHITCHECK = 0, P2EISHITCHECK = 1, P2EISCRITICAL = 2, E2PDAMAGE = 3, E2PDAMAGERANK = 4, P2EDAMAGE = 5…), registrado pela função 0x803441B4.
- FATO: a função em 0x802EE7E4 (lado nativo de `BLTgetDamageValue`, pelo retorno) monta o comando "P2EDAMAGE" com 5 argumentos (3 inteiros do estado de combate em 0x80535900, o byte do parâmetro e uma flag de checagem prévia) e o envia ao handler "FIGHTCALC"; guarda 3 resultados em +0x154/+0x158/+0x15C e devolve o primeiro. A fórmula está no handler de FIGHTCALC (ainda não localizado).
- FATO: a faixa de dano (`BLTgetDamageRank`, 0x802EE91C) é: alvo com HP ≤ 0 → 3 (SQ_DDMG); senão dano / HP máximo > 0,5 → 2 (SQ_LDMG); > 0,25 → 1 (SQ_MDMG); senão 0 (SQ_SDMG). Há condições adicionais a partir de 0x802EE9BC ainda não lidas.

### Fórmula do P2EDAMAGE (FATO, handler FIGHTCALC, caso 5 em 0x80372EC4)

O envio de comandos (0x80305A28) chama o método virtual do destinatário. O handler converte o nome pelo enum "Message" (switch em 0x803717E0; caso 18 = FIGHTCALC → 0x803729D4) e depois pelo enum FightCom (switch em 0x80372A24, tabela 0x804EB18C). Os parâmetros são lidos pelo **nome da coluna** (TSUYOSA, MAMORI, SKILL…), os mesmos das tabelas. Toda a conta é inteira, com divisão truncada:

- Código do golpe / 10000 = 4 → técnica (linha da pcTech = código % 1000); senão, físico.
- **Físico:** dano = (6·TSUYOSA + 4·SKILL[WEAPONSKILLNO]) / (MAMORI do alvo − redução), limitado a 0..99999. Com elemento (ATTACKZOKUSEI ≥ 0): se RES do alvo < sorteio % 1000, soma dano·(1000 − RES)/1000.
- **Técnica:** potência = −HP da técnica (0 → sem dano). Dano = (20·potência + 10·KASHIKOSA + 5·SKILL[técnica]) / SEISHIN do alvo, limitado; depois dano·(1000 − RES[elemento])/1000 (fire, ice, thunder, dark, poison).
- **Crítico:** com SKILL = sk, soma dano·b/100, com b = sk/20 + 30 (sk ≤ 1000), (sk − 1000)/50 + 80 (sk ≤ 2000) e (sk − 2000)/100 + 100 (acima).
- **Variação:** k = sorteio & 3; m = sorteio % (25·dano); dano = (dano·{950, 975, 1000, 1025}[k] + m)/1000, limitado. k = 3 liga a flag de golpe forte.
- **Drenos (só físico):** HPDRAIN e MPDRAIN devolvem dano·valor/100 ao atacante.
- Dano 0 depois do primeiro limite: pula elemento, crítico e variação, sem nenhum sorteio (0x80373010, 0x803732C4). Há um limite 0..99999 também em 0x80373298, antes do crítico. Sorteios são uint32 com módulo sem sinal.
- Resultado: [dano, elemento, flag], guardado em 0x80535900 + 0x154/0x158/0x15C.

### Demais casos do FightCom (FATO)

- **E2PISHITCHECK (0):** TYPE ≠ 0 acerta sempre. Senão: bloqueio se BOUGYORITSU do jogador > sorteio % 100; acerta se MEICHURITSU do golpe · SUBAYASA do inimigo / (SUBAYASA do jogador · (KAIHIRITSU + 100)/100) > sorteio % 100.
- **P2EISHITCHECK (1):** técnica acerta sempre. Senão acerta se MEICHURITSU · (20·SKILL + 6000 + 30·SUBAYASA) / (SUBAYASA do alvo · (100 − redução)/100) > sorteio % 10000.
- **P2EISCRITICAL (2):** físico: CHIMEIRITSU > sorteio % 200. Técnica sem elemento: CHIMEIRITSU da técnica > sorteio % 100; com elemento: (c + 100)·(10000/(res·12/10 + 50))/100 > sorteio % 1000.
- **E2PDAMAGE (3):** TYPE 0: (TSUYOSA do inimigo − redução)·TSUYOSA do golpe / MAMORI, bônus de elemento por faixas da resistência do jogador (HONOOTEIKO, KOORITEIKO, KAMINARITEIKO, YAMITEIKO) e crítico (100 − c1)·(100 − c2) contra sorteio % 10000 com +(c2/2 + 90)%. TYPE 1 (mágico): KASHIKOSA · TSUYOSA do golpe / SEISHIN; o elemento substitui o dano; o crítico **soma** (c/2 + dano + 90)/100 em vez de multiplicar (provável bug do jogo, reproduzido). TYPE 2: % do HP máximo do jogador. TYPE ≥ 3: 0. Fim igual ao do jogador.
- **P2EEXPUP (6):** EXP do inimigo (× (EXPRITSU + 100)/100), multiplicado por NINZU (1: 100/2, 2: 140/3, 3: 160/4, 4: 200/5); quem matou recebe /50 e os outros /100. Com 4 jogadores: 80% e 40%. Sobe de nível enquanto NEXTEXP ≤ EXP (até 999) e enche HP e MP. Base de fidelidade da party online.
- **Sorteio:** igMersenneTwisterRandomNumber = MT19937 com a semeadura de 1998 (sgenrand) e semente 5150 no construtor (`MersenneTwister.cs`). HIPÓTESE: o combate usa esse objeto sem ressemear.

Transcrição em `src/DW4.Gameplay/DamageFormula.cs`. HIPÓTESES ainda abertas: a origem do crítico (BLTisCritical), a origem da redução de defesa (objeto da checagem anterior), o comportamento sem elemento, o PRNG e a divisão por zero.

## Próximo passo

Ler do mesmo jeito os casos `P2EISHITCHECK` (1), `P2EISCRITICAL` (2), `E2PDAMAGE` (3), `P2EEXPUP` e os de estado; descobrir como o jogo monta os parâmetros do jogador (`RECALCPLAYER`: shinka + arma + armaduras + chips); confirmar a hipótese do Int@7.
