# Geradores, torretas, pisca de dano e Security Hole (2026-11-02, stag0101)

Relatos do usuário jogando a stag0101: faltam Goburimons e caixas depois da ponte; as torretas roxas não atiram; objetos
atingidos deviam piscar em vermelho; a plataforma da matx01 "devia ter alguma coisa".

## Geradores

- **FATO (main.dol):** o GENERATER tem 8 comandos (START, STOP, STOPREVERS, RESET, RESTART, PERFECTDROP, ONFREEZE,
  OFFREEZE; tabela em 0x804D1F30, desvio em 0x804DE0D4). A atualização de cada gerador (0x802FCF68) só roda com o byte +0x14
  ligado; o START (0x802FD68C) e o RESTART ligam esse byte, e o RESET desliga.
- **FATO:** no estado 0, o _startModeStr decide (0x802FD1E0): 'd' direct começa já; 'r' radar começa quando o jogador mais
  perto ("PLAYER" "NEAR") fica a uma distância² ≤ _startData do _genePoint (3025 = 55², 1225 = 35²); 't' usa o BOOT_TIMER.
  O `dw4 igb-geradores` agora grava o "point" (_genePoint).
- **FATO (stag0101):** nenhum script cita GENE-00..13 nem os barris e caixotes GBRL/GWCS. Quase todo gerador "direct" de 1
  inimigo sem script fica exatamente numa vaga de um gerador "radar" (GENE-01..05 nas vagas do GENE-00, GENE-11..13 nas do
  GENE-10). Os citados são as ondas de sala (dng1: matg01..07 → START) e os 16 épicos de um mesmo ponto, um por forma (first.lua).
- **HIPÓTESE (implementada):** nasce ligado o gerador sem script que é "radar" ou que gera objeto (g_*); os "direct" de inimigo
  sem script ficam desligados. Ligar todos dava 40 inimigos vivos de uma vez. Com a regra, a stag0101 tem 19 Goburimons nos
  grupos radar (2 ou 1 por grupo, sem voltar depois da onda vencida), 5 barris e 6 caixotes. Onde o byte +0x14 começa ligado
  não foi achado.
- Os limites do original continuam valendo: total (_geneCount), vivos ao mesmo tempo (_geneMaxCount) e fim do wipeout.
- Bot sem janela: o GENE-07 começa em (345, −25) e o GENE-10 no alto, com o barril e o caixote em (415..425, 71, −127).

## Torretas (g_cannon01)

- **FATO (igb-seq):** mapa de IA MAP00 2500/3600/6400 "NEAR": perto (< 50) → SQ_FIRE. O primeiro SQ_FIRE (rótulo OFF) liga a
  torreta (WIT2ON) e passa o SQ_WAIT para o rótulo ON (MOVE "Stop" + HMNG01); os seguintes dão 3 × BLST01 + espera. O
  disparador x_bltcannon01 sai na BLST01 com deslocamento (0, 7,5, 1). Um script manda SQ_POWERON às duas.
- No port só objetos f_/n_ tinham IA. Agora objetos g_* com mapa também têm, e os disparadores de ação valem para objetos.
  **HIPÓTESE:** a HMNG01 ("homing") vira a torreta para o jogador no máximo o giro do mapa (0,0349 por quadro).
- Bot sem janela perto de (175, −175): 24 tiros em 40 s, 8 acertos, HP 150 → 95.

## Pisca vermelho de dano

- **FATO:** as ações de dano e morte começam com FLASHSET "Damage" (SDMG01/DEAD01 dos objetos: torreta, pedras, arbustos,
  barris, caixotes, tochas; LDMG01, LDMGD1… do e_goburi, e_nume, e_ogre).
- **HIPÓTESE (efeito nativo não lido):** cada FLASHSET faz o modelo piscar em vermelho por 0,5 s (liga e desliga a cada
  0,1 s). Cada golpe toca uma ação de dano, então o objeto pisca a cada acerto até ser destruído (`Fatia.Flash.cs`).

## Security Hole (matx01)

- **FATO (textos mswk67041/42):** a plataforma é um Security Hole: com um Gate Disk, liga ao Emergency Gate do lobby. O
  símbolo é o g_security (GENERATE com SQ_POWERON em (−280,6, 57,6, −20)).
- **FATO (lua-trace do matx01.lua):** `MDLisLive(SYSgetPlayerName(p), 0)` decide entre SQ_SWON (liga o símbolo e, na primeira
  vez, mostra o aviso) e SQ_SWOF. O port respondia 0 para o modelo do jogador, então o símbolo apagava e o aviso não aparecia.
  Agora o jogador vivo responde 1. No bot: "entrou em matx01" → SQ_SWON → janela com os dois textos.
