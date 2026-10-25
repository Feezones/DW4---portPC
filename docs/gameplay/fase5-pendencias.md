# Fase 5: o que ainda impede jogar do começo ao fim (2026-10-25)

Critério da Fase 5 (roadmap): o jogo inteiro no Windows, jogável do início ao fim, com desempenho estável. A lista vai do que
mais bloqueia ao que menos bloqueia. O estado vem do que está no repositório e dos testes. "Não visto" = nunca conferido na
janela.

## 1. Bloqueiam o caminho principal

1. **Caminho do enredo sem conferir na janela.** O primeiro trecho é lobby → base da Ofanimon (stagML02) → stag0001. Ele
   travou no SQ_SHOK da stagML02, já corrigido (66ebbfa), mas ainda não foi jogado inteiro. Depois vêm as masmorras da dng1 a
   dng4, os chefes stagbs01..06 e o fim (FLG 86 no último chefe). Mapa das flags: fase5-enredo.md.
2. **36 fases nunca abertas no cliente.** São as que vêm depois de stag0302 na varredura (fase5-varredura.md). Todas
   exportam, mas não se sabe se carregam, se têm chão e se os scripts rodam. Abrir uma janela por fase precisa de autorização.
3. **Morte e recomeço do jogo.** Feito como DECISÃO DO PORT (904a338): perde os BITS no campo, HP/MP cheios, volta ao lobby. O destino original continua desconhecido.
   - FATO: o DEADRESTART da GAMERAM (0x80371B3C) enche HP e MP.
   - Os textos dizem que os BITS carregados se perdem ao cair no campo; onde isso acontece no código não foi lido.
   - Falta o fluxo do jogo: tela de queda, volta ao lobby ou ao ponto, perda dos BITS.
4. **Chefes 02..06 não vistos.** Os scripts rodam sem erro e o movimento (MOVE/MOVEEX/MOVERL) é o do jogo. A escala da
   unidade (30 Hz, unidade do jogo = do mundo) é HIPÓTESE, e as peças presas (f_luce "ball") nunca foram vistas.
5. **Final do jogo.** Levantado: stagbs06 → stagIS20 (créditos) → lobby, entrada 0. No port, tela de fim com a música dos créditos; a arte dos créditos (j_endroll) não foi exportada.

## 2. Bloqueiam partes do jogo

6. **Armas e estilos.** A fatia usa sempre a w_sword01 (estilo slash) na mão (DECISÃO DA FATIA). Equipar outra arma muda
   só os números: não muda o modelo na mão nem o bloco de sequências (blunt, stab, bash, shot, crush). As 16 formas têm os
   blocos; falta ligar.
7. **Painel de técnicas.** As teclas 1..6 usam a técnica n da WAZALIST (DECISÃO DA FATIA); o painel do jogo (wazaPanel) não
   foi portado.
8. **Missões (quest01..06 e treasure01..08).** Rodam pelos scripts, mas nunca foram jogadas; a recompensa libera as formas.
9. **Loja de cartas (stagIS11) e as cartas.** As regras não foram lidas. Hoje o NPC mostra "ainda não portada".
10. **Efeitos de estado no jogador** (atordoar, sono, prender, veneno, ataque/defesa baixos). O jogo faz pelos hit_p_*.lua; o
    port calcula o dano por conta própria e não roda esses scripts.

## 3. Aparência e conforto (não bloqueiam, mas o jogo fica diferente)

11. **Janelas do jogo.** A fala (i_messwn), a troca de área, o menu de status, as lojas, o banco e o X-data são caixas da
    Godot com os textos do jogo. As janelas j_* e i_* e a fonte do jogo não foram exportadas.
12. **Formas novas não vistas.** Pose, escala, animações e o osso da arma das 15 formas além do Guilmon.
13. **NPCs.** Andam, falam e passeiam pela IA do jogo (2d48253), mas não foram vistas. Falta conferir se viram para o jogador
    ao falar e as escalas.
14. **Filmes** (abertura AJP/AUS/AEU_opening.sfd e o final): o leitor Sofdec existe nas ferramentas, mas não no cliente.
15. **Câmera.** "Dungeon camera" Perto/Longe e "Light" Suave/Forte vão para a GAMERAM, mas o efeito delas no cliente não foi
    ligado.

## 4. Desempenho e estabilidade

16. Não medido: carga das fases grandes, número de NPCs e inimigos, uso de memória numa sessão longa.

## Já resolvido nesta etapa (para referência)

- Menu inicial com as 4 vagas por conta, configurações, save por vaga e começo de jogo pelo gameStart.lua.
- Dificuldade pelo hostLoad.
- Evolução (16 formas).
- Lojas de equipamento, discos, banco e X-data.
- Chat e placa com nome e nível.
- NPCs: animação, conversa e passeio.
- Save Keeper removido, com o save automático.
