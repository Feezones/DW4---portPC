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

### Atributos do jogador (RECALCPLAYER, FATO)

- Faixas de nível LV = [5, 10, 20, 30, 50, 70, 110, 150, 230, 999] e início L0 = [1, 5, 10, 20, 30, 50, 70, 110, 150, 230].
- MHP/MMP = INI + UPCNT + GROW·(A[i] + B[i]·(L − L0[i]))/100 (até 99999), com A = [0, 40, 140, 640, 1440, 3840, 7440, 17840, 25040, 34640] e B = [10, 20, 50, 80, 120, 180, 260, 180, 120, 80].
- STR/AC/INT/MND/DEX (→ TSUYOSA/MAMORI/KASHIKOSA/SEISHIN/SUBAYASA) = INI + UPCNT + GROW·(C[i] + D[i]·(L − L0[i]))/100 (até 9999), com C = [0, 40, 85, 165, 235, 355, 455, 615, 735, 895] e D = [10, 9, …, 1].
- Equipamento por slot: arma (POWERSOCKET), chips da arma (POWERPOCKET), armadura 1 (MAINSLOT, armor1), chips dela (MAINPOCKET) e armadura 2 (SUBSLOT, armor2). Cada atributo soma colunas específicas, mais UPDATA − DOWNDATA (efeitos temporários). O total fica entre 1 e 9999. As resistências do jogador (HONOO/KOORI/KAMINARI/YAMITEIKO) vêm das colunas RES_* da shinka e das armaduras. ATTACKDELAY = arma.DELAY (+ efeitos), no máximo 70. ATTACKZOKUSEI vem do chip da arma.
- NEXTEXP = L²·(L + 140) + 200·L (EXP acumulado; 0 no nível 999).
- Ainda não lido: o valor inicial e os limites de MEICHURITSU, CHIMEIRITSU, MAHIRITSU e KAIHIRITSU, e o crescimento do SKILL (P2ESKILLUP).

- MEICHURITSU = arma (sem arma, 0); CHIMEIRITSU = efeitos + arma + chips da arma (mínimo 1); MAHIRITSU = arma; KAIHIRITSU = efeitos + CHOHATURITSU dos chips da armadura (mínimo 1).

### Personagem novo (FATO)

- GAMEDATAINIT (0x8036CE1C) zera o estado e põe BASE_LEVEL = 1 e as 9 BASE_SKILL = 1 para as 16 Digimon; EXP, UPCNT, dinheiro (BIT) e inventário começam em 0/vazios. Nenhum IGB guarda esse estado.
- Ao entrar com a Digimon d (0x8036EB0C): copia LEVEL/EXP/SKILL/UPCNT dos BASE_* dela, cria INI_WEAPON1..3 da shinka no DEVICEFOLDER e preenche o POWERSOCKET [0..2] em ordem; a ativa é o [0]. Nenhuma armadura. INI_WEAPON4 não é usado pelo código.
- Guilmon: 268 ∈スタッブダガー (STAB, ativa), 275 e 274. Equipar exige SOUBI & (1 << DIGIMONNO) e SKILL[arma.SKILL] ≥ SKILLPOINT.
- Armaduras: armor1 (MAINSLOT) com BOUGYORITSU, MAHITEIKOU, DOKUTEIKOU, MAMORI, MAXHP, … e LVPOINT (nível mínimo); armor2 (SUBSLOT) com BITRITSU, EXPRITSU, LUCKRITSU, atributos (com penalidades negativas) e SHUSEINOURYOKU (atributo que recebe o bônus de revisão).
- Contato: as sequências do Numemon mantêm o HITBOX 70000 (eneatk 0: TYPE 0, acerto 100, TSUYOSA 100) no corpo, exceto nas de dano e guarda. HIPÓTESE: encostar gera um golpe 70000 pelo mesmo E2PISHITCHECK/E2PDAMAGE (ainda não ligado na fatia).

### Equipamento no RECALCPLAYER (FATO, 0x8036FDE8)

- Ajudantes: arma POWERSOCKET[0] (0x8036F484), chips da arma POWERPOCKET (0x8036F534), armor1 MAINSLOT (0x8036F5DC), chips da armadura MAINPOCKET (0x8036F68C), armor2 SUBSLOT (0x8036F734).
- MHP = curva + UPDATA[6] + armor1.MAXHP + armor2.MAXHP (até 99999). MMP = curva + armor1.MAXMP + armor2.MAXMP.
- TSUYOSA = curva + UP − DOWN + arma + chips da arma + armor2. MAMORI = curva + UP − DOWN + armor1 + armor2. KASHIKOSA, SEISHIN, SUBAYASA = curva + UP − DOWN + arma + chips da arma + armor1 + chips da armadura + armor2. Todos 1..9999.
- HONOO/KOORI/KAMINARI/YAMITEIKO = shinka.RES_* + UPCNT[7..10] + UPDATA[7..10] + armor1 + chips da armadura + armor2 (1..9999).
- ATTACKDELAY = DOWNDATA[6] + arma.DELAY (até 70). MEICHURITSU e MAHIRITSU = arma. CHIMEIRITSU = UP[1] − DOWN[1] + arma + chips da arma (≥ 1). KAIHIRITSU = UP[3] − DOWN[3] + chips da armadura.CHOHATURITSU (≥ 1). BOUGYORITSU, MAHITEIKOU, DOKUTEIKOU = armor1. BITRITSU, EXPRITSU, LUCKRITSU = armor2. ATTACKZOKUSEI = ZOKUSEI do chip da arma. HPDRAIN/MPDRAIN = chips da arma.
- Revisão: com a flag ligada, arma e armor1 somam o valor item+0x14; a armor2 soma só no atributo do SHUSEINOURYOKU. Ainda fora do port (não há item com revisão).
- DIGIMONNO = linha da shinka (FATO: as 16 armas exclusivas têm um bit só, igual a 1 << linha da forma que começa com ela; Guilmon = linha 2, arma especial 252 com SOUBI 4). As armas comuns têm SOUBI 65535.
- Equipar: arma pede SOUBI & (1 << DIGIMONNO) e SKILL ≥ SKILLPOINT; armadura pede só nível ≥ LVPOINT (0x803860A8; o SOUBI das armaduras é da loja). Na digievolução (0x8039CA4C) armaduras e chips são desequipados e a arma é re-escolhida pela mais forte usável (WEAPONEQUIPSTRONG).
- Troca de arma em combate: não achada. O RECALC só usa POWERSOCKET[0]; HIPÓTESE: [1] e [2] são reservas trocadas pelo menu de status. Regra dos pockets (ISPOCKET, quantidade): desconhecida.
- Código no port: `GameTables.Player(..., Equipment, ...)`, `CanEquipWeapon`, `CanEquipArmor`. UPDATA/DOWNDATA ficam para os estados.

### Técnicas (FATO, salvo marcação)

- pcTech (100 linhas): 20 famílias de 5 graus (linha = base + grau − 1; RANK 1..5 = プチ/メガ/ギガ/テラ/∞). SKILL = índice em SKILL[] (6 ataque, 7 cura, 8 suporte), MP = custo, DELAY = recarga em quadros de 60 Hz, HP = potência (negativo = dano, positivo = cura), LABEL = bloco da SQ_WAZA.
- A ligação com a Digimon é a shinka: TECH1..6 (linha-base), TECHn_LEVEL (nível mínimo) e TECHn_SKILL1..5 (skill mínima por grau). A WAZALIST (RECALC 0x80370994) guarda uma técnica por família, no maior grau liberado. Guilmon: fire (nv 1), powerdown (12), healsingle (22), dark (12), powerup (23), bomb (23). Guilmon novo: só a プチファイア (16 MP, potência 160, recarga 75 quadros).
- wazaPanel (10 linhas, COLOR/NAME de estados): não liga técnica a Digimon. HIPÓTESE: cores e ícones dos estados.
- Uso: beNDMWMdlPlayer (0x8034C940) manda "GENERATEWAZA" ao painel (bit 0x800 dos botões); o painel (0x80349474) só aceita sem recarga e com MP ≥ custo, desconta o MP, põe a recarga e devolve o LABEL; o jogador roda SQ_WAZA com esse bloco (fire1: ACTION HFIRE1, sem HITBOX).
- O dano vem de um projétil (KIND_PBULLET; x_200fire01: MOVE "Push" 1,5 25 e HITBOX 40000 por 1000 quadros, 4 × 4 × 4). HIPÓTESE: o motor completa o código com a linha da técnica (40000 + linha), porque um só modelo serve aos 5 graus. Quem gera o projétil não foi achado.
- MP: não há regeneração automática no código lido; sobe com GAMERAM "ADDMP" (itens, pontos de cura) e enche no level-up.
- Código no port: `src/DW4.Gameplay/Techniques.cs` (WAZALIST e painel). Na fatia, sem o painel, as teclas 1..6 usam a técnica direto (DECISÃO DA FATIA); o projétil sai quando a ação termina e é uma esfera (HIPÓTESE e substituto do modelo).

### Skills (P2ESKILLUP, caso 13, FATO)

Limite pelo nível: 30L (até 36), 20L + 360 (até 107), 10L + 1430 (até 227), senão 8L + 1884; no limite não há sorteio. A taxa vem de uma tabela 4 × 21 × 3 do main.dol (0x804EAF18; extraída com `dw4 dol-skilltab`, fora do repositório), indexada pela faixa de nível, pela faixa da skill (1, 2, … 10, 20, 30, 100, 300, 500, 1000, 2000, 3000, 4000, 6000, 10000) e por uma coluna (2 em evento especial, senão 0; a coluna 1 nunca é usada por um andc com 0, provável bug). Técnica: chance = taxa·100; arma: taxa·GROW do tipo de arma (shinka BLUNT/SLASH/STAB/BASH/SHOT/CRUSH_GROW)·500/1000. Sobe +1 se chance > sorteio % 100000.

### Estados (casos 7–12 e 14–19, FATO)

Resumo em `src/DW4.Gameplay/StatusFormula.cs`: atordoamento pela fração de HP tirada e MAHIRITSU contra RES_STUN; veneno, sono, prisão e reduções pela curva da skill da técnica contra RES_POISON/SEISHIN; golpes de inimigo pelo KOUKA/KOUKARANK/KOUKATIME da eneatk e pelo fator 3·KASHIKOSA/SEISHIN + 100 − SEISHIN/40. MAHITEIKOU/DOKUTEIKOU do jogador vêm da armadura 1 com valores negativos (somam e reduzem a chance). Reduções de atributo ficam entre 20% e 80%.

Transcrição em `src/DW4.Gameplay/DamageFormula.cs`, `PlayerGrowth.cs`, `SkillGrowth.cs` e `StatusFormula.cs`. HIPÓTESES ainda abertas: a origem do crítico (BLTisCritical), a origem da redução de defesa (objeto da checagem anterior), o comportamento sem elemento, o PRNG e a divisão por zero.

## Próximo passo

Ler do mesmo jeito os casos `P2EISHITCHECK` (1), `P2EISCRITICAL` (2), `E2PDAMAGE` (3), `P2EEXPUP` e os de estado; descobrir como o jogo monta os parâmetros do jogador (`RECALCPLAYER`: shinka + arma + armaduras + chips); confirmar a hipótese do Int@7.

### Painel de técnicas: escolha (lido em 2026-10-26)

- FATO (atualização do painel, 0x8034A228..0x8034AB70): o painel guarda 3 listas (+0x10, +0x14, +0x18) e mostra a do estado
  +0x8 (0, 1, 2). Três botões trocam o estado (bits internos 0x800, 0x400, 0x200 da palavra de toques +0x48/+0x4C do controle);
  apertar o botão da lista que já está aberta toca CURSOR_CANCEL. O cursor anda para a anterior ou a próxima (bits 0x1/0x10 e
  0x4/0x40 de segurados/tocados), com repetição pelo temporizador "FIRE". O cursor é o ator i_wzslcursel (GENERATE ao abrir,
  SQ_CLOSE ao fechar, som WINDOW_CLOSE). A confirmação segue a regra já lida (0x80349474): sem recarga e MP ≥ custo, desconta,
  põe a recarga e devolve o LABEL (TechniquePanel).
- No mesmo código há DISKFOLDER, EMGHEAL e x_250healhit00/x_250curehit00: uma das listas é a de discos (cura de emergência).
- DESCONHECIDO: o que é a terceira lista e quais botões do controle do GameCube correspondem aos bits internos (o jogo remapeia o
  controle antes). O port mantém a barra (1..6, roda/LB-RB, K/botão direito/Y), que faz o mesmo papel: escolher a anterior/
  próxima e confirmar com a mesma regra.
