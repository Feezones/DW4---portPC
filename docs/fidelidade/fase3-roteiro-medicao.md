# Fase 3: roteiro de medição no jogo original

Objetivo: fechar as hipóteses de `docs/gameplay/fase3-levantamento.md` com medições no Dolphin, para a fatia passar de "usa os valores dos dados" a "conferida com o jogo".

## Condições a registrar

Hash do disco (RVZ, SHA-256 já registrado na Fase 0), versão do Dolphin, backend de vídeo, velocidade de emulação em 100%, controle usado e data. Personagem: Guilmon com a espada inicial ou com `w_sword01`, se for possível equipar. Fase: dng1, a primeira masmorra.

## Gravação (feita uma vez pelo jogador)

No Dolphin: Filme → Iniciar gravação de entradas (gera um `.dtm`), começando de um save já dentro da dng1. Sequência de cerca de 2 minutos:

1. Ficar parado 3 s (espera e câmera parada).
2. Andar em linha reta segurando o direcional por 3 s, soltar e esperar 2 s (aceleração, velocidade máxima e frenagem).
3. Repetir em diagonal e com o analógico pela metade (resposta parcial).
4. Andar até uma parede e continuar empurrando 2 s (colisão).
5. Atacar uma vez e esperar (golpe isolado).
6. Atacar apertando de novo logo depois (combo?) e segurando o botão (carga?).
7. Atravessar duas salas com câmeras diferentes (troca de volume: corte ou interpolação).
8. Aproximar-se de um Numemon e deixá-lo agir por 10 s (IA: distâncias de reação).

## Análise (feita por mim, no PC, sem jogar)

Reproduzir o `.dtm` com despejo de quadros e ler a memória em quadros fixos:

| Hipótese | Medição | Critério |
|---|---|---|
| `PDMOVE` Move 0,7 / 6 = aceleração / velocidade máxima | posição do jogador por quadro durante os passos 2 e 3 | curva de velocidade do port dentro de 2% |
| Unidade de tempo dos quadros (30 ou 60 Hz) | contar quadros de vídeo entre início e fim do `vatcS` | duração em quadros igual |
| Câmera = A · B · Comum · alvo | sobrepor captura do jogo e do port no mesmo ponto | diferença de posição da câmera < 1 unidade |
| Troca de volume de câmera | quadros durante o passo 7 | mesmo comportamento (corte ou mistura e duração) |
| FOV | captura com objeto de tamanho conhecido | mesmo enquadramento |
| Janela de combo / carga (WCHECK Atck01, KATCS1 em laço por 60 quadros) | passo 6 | mesma sequência de animações |
| Hitbox nos quadros 9–13 do `vatcS` | quadro do primeiro acerto no Numemon | mesmo quadro |
| IA do Numemon: faixas 36/45/150 e 22/40/150 | distâncias em que ele muda de ação | mesmas faixas |
| Ponto inicial (PLAYERPOINT) | posição do jogador no primeiro quadro da fase | igual |
