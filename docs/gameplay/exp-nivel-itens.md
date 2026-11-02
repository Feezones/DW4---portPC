# EXP, subida de nível e itens no meio da fase (2026-11-02)

## EXP ao matar

- **FATO (main.dol, tabela de números em 0x804DD2D0):** i_dmgnum (dano no inimigo), i_expnum (EXP ganha, número "expnm0",
  SQ_EXPNM01..04), i_healnum, i_pcdmgnum (dano no jogador), i_bitnum (BIT). Seq do i_expnum: DELAY01 (40 quadros escondido) →
  OPEN01..04 (JOINTSET 10..30 / 40..60 / 70..90 / 100..120, FADESET IN 12, espera 20, OUT 20) → SQ_ERAS.
- Port: o "EXP n" sobe sobre o jogador com o ator e o número do jogo (DamageNumbers.ShowExp). **HIPÓTESE:** o OPEN pelo número de
  dígitos.

## Subida de nível

- **FATO (P2EEXPUP 0x80373DE0):** MODELCTRL "i_getitempnl01" "SQ_LVUP". **FATO (árvore do i_getitempnl01):** é um igBillboard em
  unidades de mundo (anéis G_lvring, "lv", "up", "maintrans" a 10 de altura) sobre o personagem; SQ_LVUP = DELAY01 (50 quadros) →
  LVUP (FADESET IN 12, JOINTSET 290..360, FADESET OUT 40). As peças animam a escala como (x, y, 0); no billboard da Godot o z vira
  1 (base singular não desenha).
- **FATO:** depois, o P2EEXPUP manda SETEFFECT no jogador; o nome sai de um buffer montado em tempo de execução (não lido).
  **HIPÓTESE:** o brilho x_250uphit01 (SQ_ATCK01) no jogador.

## Itens no meio da fase

- **FATO (textos mswk201984/204308 da stagML02):** o botão √ abre a Item Window, o direcional escolhe o disco e o ≪ usa. **FATO:** o
  jogador tem a SQ_ITEM; o x_250curehit01 sai na ação ITEMC1 (SQ_ATCK01); o disco cura pelo HP/MP da conItem (0x80349B0C).
- Port: janela de itens (I / B no controle) e atalhos 1–5 (decisoes-port-pc.md). Mouse: passar por cima escolhe o disco, 1–5
  põe no atalho, clique usa. Correção: a janela crescia a cada seta (UseGameFont multiplicava a fonte por 1,3 a cada redesenho). Capturas: D:\dw4-work\previas\hud_itens_16x9.png,
  hud_itens_4x3.png e hud_nivel_16x9.png.
