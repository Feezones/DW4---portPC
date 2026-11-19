# Queda, backup chip e Raise Disk

Pedido do usuário (2026-11-02): ao morrer, deixar o cristal (backup chip) no chão e recuperar com o Raise Disk, como no original,
salvo na vaga. Também conferir se a morte bloqueia o teleporte do lobby.

## FATO (código e dados do jogo)

- Textos mswk67043–67049 e mswk202433–202436: o Digimon derrotado deixa um backup chip onde caiu e volta ao lobby. Todo o BIT fica
  no chip e a EXP cai, menos no nível 1. O Raise Disk ou o MP Tech Raise usado no chip devolve todo o BIT e parte da EXP. Cair de novo
  sem recuperar apaga o chip antigo.
- `end_g_000bcaps.lua` (queda do time): mostra mswk62009, limpa as FLG 409/381/571/424 se COUNTER2 = 906, SQ_GAMEOVER, QS_DONOT,
  mswk61090, SETGENERATEPOS 0, NEXTSTAGE stagML11. Na primeira vez (FLG 508) mostra o tutorial do chip (mswk67043/44).
- BKCHIPBITEXP (0x80377F70, na tabela GAMERAM em 0x804EB224): o BIT vai para a lista do chip e é zerado. A EXP perdida é
  `(NEXTEXP(L−1) − NEXTEXP(L−2)) × 2 / 10`, sem deixar a EXP abaixo de NEXTEXP(L−1). Nada no nível 1 nem no 999. No port:
  `PlayerGrowth.DeathExpLoss`, com teste.
- Modelo: `g_000bcaps` no IGB #179 (bkcore, mekafin, bkchip, textura fsbktip0). Sequências SQ_ENTR1P..4P → SQ_WAIT, SQ_SWON/SWOF,
  SQ_EXIT (DEAD01..04) → SQ_ERAS. Exportado com `dw4 igb-objetos` e `dw4 igb-seq` em `<fases>\_comum\g_000bcaps`. O efeito
  x_000bcaps não tem geometria no #179.
- Teleporte do lobby (mate00): abre os destinos normais com FLG n = 1 e FLG 409 = 0. FLG 2 (primeiro destino, stag0001) vem da
  conversa com o comandante (matf00: FLG1 && !320 → demo, 320 = 1, 1 = 0, 2 = 1; também n_ofanim_00). gameStart.lua limpa a 409
  e as flags de missão, mas nunca 1, 2 nem 320. A morte não mexe em nenhuma delas.

## Como ficou no port

- Queda em campo (fora das stagML): `DropBackupChip` guarda fase, posição, BIT e EXP perdida na vaga (`Inventory.Chip*`,
  `CharacterSave`), zera o BIT e tira a EXP. Um chip antigo é substituído.
- Ao abrir a fase do chip: `SpawnBackupChip` põe o g_000bcaps no lugar da queda, com SQ_ENTR1P.
- Raise Disk (conItem com "Raise" no nome) perto do chip: gasta o disco, devolve o BIT e parte da EXP, chip faz SQ_EXIT e some.
  Longe do chip: aviso e o disco não é gasto.
- Depuração (só Debug): `DW4_MORRER=s` morre uma vez aos s segundos (com +123 BIT); `DW4_RAISE=1` volta do lobby à fase do chip
  e usa um Raise Disk nele; `DW4_RAISE=ver` espera 3 s perto do chip antes de usar (para o DW4_PRINT).
- Conferido pelo bot sem janela (stag0101, nível 5): chip com 123 BIT e 243 EXP → lobby → volta → Raise: +123 BIT, +121 EXP,
  chip apagado. Captura de janela que fecha sozinha mostra o cristal verde no chão.

## HIPÓTESE

- O Raise devolve metade da EXP guardada. O código da devolução não foi achado (0x8034EF50 é o comando RAISE do painel, não a conta).
- O disco vale até 25 unidades do chip.
- O chip nasce na posição exata da queda. As tabelas g_posTbl do end_g_000bcaps.lua, por área, não foram usadas.
- O teleporte "bloqueado depois de morrer" é a falta da conversa com o comandante (FLG 2 = 0 na vaga 1 do usuário: FLG 1 = 1,
  320 = 0), não a morte. Na vaga 2, depois da conversa, FLG 2 = 1 e o teleporte abre.

## DESCONHECIDO

- MP Tech Raise (técnica) no chip: não implementado.
- Se o chip some em outras situações (troca de missão, fim de sessão online).
