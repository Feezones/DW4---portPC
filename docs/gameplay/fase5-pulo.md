# Fase 5: pulo e queda do jogador

## Levantamento

- **FATO** (Guilmon #857, SQ_JUMP em cada bloco de estilo): CTRL "onWalk"; PDMOVE "Stop" 1 4; PDMOVE "Move" 0,7 6;
  MOVE "Jump" 1,44 6,3; ACTION JUMPS1; WCHECK "TurnY"; MOVE "Fall" 1,44 6,3; ACTION JUMPS2; WCHECK "Landing";
  CTRL "toWalk"; MOVE "Stop" 1 4; ACTION JUMPS3; WCHECK "Action"; JUMP "SQ_WAIT".
- **FATO** SQ_FALL é o mesmo sem a parte do "Jump" (começa no MOVE "Fall").
- **FATO** (main.dol, leitura estática do controle do jogador perto de 0x8034CB00): o pedido de SQ_JUMP sai do bit 0x2000
  da máscara de botões apertados que o jogo monta (r27 = palavra +0x48 da estrutura do controle). O bit 0x8000 da mesma
  estrutura leva ao SQ_GUARD.
- **DESCONHECIDO** qual botão físico do controle de GameCube vira o bit 0x2000. A tabela de conversão não foi achada, e
  os textos das áreas 3 a 60 não citam o botão de pulo.
- **HIPÓTESE** "Jump" g v0 e "Fall" g vmáx em unidades por quadro a 30 Hz (a taxa do SequenceRunner): pico de
  v0² / 2g ≈ 13,8 unidades, cerca de 0,29 s no ar. Passa o degrau de 10 do pedestal do stagbs01, o que combina com a arena.
- **HIPÓTESE** "TurnY" = a velocidade vertical virou (começou a descer); "Landing" = tocou o chão.
- **HIPÓTESE** PDMOVE "Move" 0,7 = controle no ar a 70% da velocidade de andar.

## Na fatia

- `src/DW4.Gameplay/JumpMotion.cs`: aplica os MOVE "Jump"/"Fall" e integra a velocidade vertical.
- `SequenceRunner.Condition` responde aos WCHECK que dependem do mundo ("TurnY", "Landing"). `SequenceRunner.MoveStarted`
  avisa na hora em que o MOVE roda, porque o pulo precisa da velocidade antes do "TurnY" seguinte.
- `src/DW4.Client/Fatia.Jump.cs`: Espaço ou o botão de baixo do controle começa o SQ_JUMP no bloco do estilo da arma. Sem
  chão por 0,2 s fora de um pulo, começa o SQ_FALL (**HIPÓTESE da fatia**, para não cair em cada degrau).
- Não pula com o menu aberto, com o controle travado pelo script (PADINPUT/OFCTRL), caído (HP 0) nem durante um golpe ou
  técnica. Um botão que estava apertado quando o controle voltou, ou que fechou o menu, não vira pulo.
- Teste: `tools/DW4.Tests`, "Jogo: SQ_JUMP do Guilmon (slash)…" com a sequência real: JUMPS1 → JUMPS2 → JUMPS3, termina no
  SQ_WAIT, altura entre 10 e 14,5.

## Conferido na janela (stagbs01, 2026-10-25)

Espaço: o Guilmon sobe até y ≈ 31–33 saindo do chão em y 20, toca JUMPS2 no ar, pousa de volta em y 20 e volta a ficar
parado.

## Correção: conta do MOVE "Jump"/"Fall" pelo executor (2026-10-25)

- **FATO:** a leitura do executor do MOVE (ver fase5-chefe-bs01.md, MotionChannels) dá outra conta para os números.
  - "Jump" 1,44 6,3 começa em +1,44 por quadro e freia 1,44 / (0,5·6,3²) ≈ 0,073 por quadro² até 0.
  - "Fall" acelera para baixo com a mesma conta até −1,44.
- O pico fica quase igual: 14,3 contra 13,8. A subida passa a levar ~20 quadros, contra ~4 antes.
- O JumpMotion foi trocado para essa conta. Ainda não foi conferido na janela.
