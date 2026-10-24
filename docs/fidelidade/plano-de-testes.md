# Plano de testes de fidelidade

Ciclo por sistema: identificar comportamento, definir medição, registrar condições, implementar, comparar, documentar divergências, corrigir, repetir.

| Grandeza | Medição no original | Comparação |
|---|---|---|
| Entradas e tempos | Gravação de entradas no emulador, contagem de frames | Mesma sequência na reconstrução |
| Posições e velocidades | Leitura de memória no depurador em frames fixos | Diferença por frame dentro da tolerância |
| Animações | Vídeo a 60 fps, frames de início e fim | Duração em frames e quadros-chave |
| Câmera | Capturas em pontos de referência | Sobreposição de imagens |
| Dano e atributos | Números exibidos com atributos conhecidos | Teste automatizado em DW4.Gameplay |
| Colisões | Rotas encostando em paredes e bordas | Mesma posição final |

Toda medição registra: hash do disco, emulador e versão, condições, tolerância.
