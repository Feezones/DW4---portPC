# Golpe carregado (2026-11-02)

Pedido do usuário: segurar o ataque deve dar um golpe carregado com avanço, como no original.

## Original (igb-seq do Veemon #863 e do Guilmon #857)

- **Carga (SQ_ATCK1, bloco do estilo):** os golpes do combo (VATC?1..3) → WCHECK "Atck01" → KATC?1 (pose de carregar em laço,
  WAITCHECK "Frame" 60) → "Atck02" → KATC?2 (laço sem fim) → "Atck03" → ao soltar, JUMP "SQ_ATCKTM". Segurando desde o golpe
  que abre a carga, o carregado fica pronto depois do combo + 60 quadros de 30 Hz (2 s).
- **Golpe (SQ_ATCKTM), igual nos quatro estilos (dual, slash, arm, gun) das duas formas:**

| Passo | Comando |
|---|---|
| salto | MOVE "Jump" 1 3,9 (canal Y modo 4) |
| avanço | MOVE "Push" 2,3 11 (canal Z modo 4: começa em 2,3 por quadro e freia até 0) |
| golpe | ACTION KATC?3 (katcS03 / katcA03 / katcD03), MUTEKI 18 quadros, 6 caixas HITBOX 20000 de 8 × 8 × 8 em volta e à frente |
| queda | WCHECK "TurnY" → MOVE "Fall" 1,44 6,3 |
| pouso | WCHECK "Landing" → MOVE "Stop" 1 4 → JUMP?3 → SQ_WAIT |

- O x_100gunk01 da pistola é só efeito (sem caixa). O dano é o das caixas 20000, sem multiplicador achado no FIGHTCALC (**HIPÓTESE**).

## Port

- Antes: os MOVE do jogador só valiam no SQ_JUMP/SQ_FALL. No SQ_ATCKTM o salto, o avanço e a queda eram ignorados, e o carregado
  saía parado no lugar. As caixas 20000 também não acertavam (corrigido antes, 6595052).
- Agora: o SQ_ATCKTM aplica o salto e a queda (JumpMotion) e o avanço pelos canais do jogo (MotionChannels, unidades por quadro
  de 30 Hz, como os inimigos). Durante o golpe, o jogador não controla a direção.
- Bot sem janela (DW4_ALVO=carregado, stag0101): Guilmon com espada avançou 61,5; Veemon com Shot Pistol, 64,8. Antes: 0.
  As medidas variam com os inimigos por perto, que interrompem o golpe.
- **HIPÓTESE:** a escala do "Push" em unidades por quadro de 30 Hz (a mesma dos inimigos).
