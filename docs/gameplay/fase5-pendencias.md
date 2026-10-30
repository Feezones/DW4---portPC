# Fase 5: o que ainda impede jogar do começo ao fim (2026-10-30)

Critério da Fase 5 (roadmap): o jogo inteiro no Windows, jogável do início ao fim, com desempenho estável. "Não visto" = nunca
conferido na janela. O estado vem do repositório, dos testes (81 ok) e das janelas autorizadas (incluindo a noite de 2026-10-29/30, com o bot DW4_BOT).

## A. Impedem o critério (jogável do início ao fim)

1. **Caminho do enredo nunca jogado inteiro.** Lobby → stagML02 → stag0001 → masmorras dng1..dng4 → chefes stagbs01..06 → fim
   (FLG 86). O SQ_SHOK da stagML02 foi corrigido (66ebbfa). A troca de área já foi usada pelo usuário (escolha e NEXTSTAGE
   funcionam). Falta alguém jogar o caminho inteiro. Mapa das flags: fase5-enredo.md.
2. **Chefes 02..06 em luta (bot, noite de 2026-10-29/30).**
   - FATO (visto): chefe 2 (f_belvam) derrotado pelo bot duas vezes (nível 40 e 50), com BT_WIN02 e EXP. Chefes 4 (f_devl1) e
     5 (f_devl22) tomam e dão dano (o bot nível 50 morreu no 4 e voltou ao lobby). Chefe 3 (f_luce) agora segue as fases do jogo
     (SQ_ENTR → ATCKON → bombas → ATCKOF → voo/pouso → magia → CHACK) e fere o jogador com mísseis e golpes.
   - Corrigido nesta noite: corpo dos chefes grandes para o golpe do jogador (caixa da malha); LUACALL com o nome de chamada
     ("gr2" "GROUND"); o inimigo nasce pelo SQ_ENTR (AIMAP inicial e rótulos); o inimigo segue o JUMP para outra sequência;
     GENERATER RESTART (gera a f_luce2); ISLIVE dos projéteis; projéteis pelos disparadores de ação (abaixo).
   - Pendente: o chefe 3 só toma dano no chão (g_Ground); ele desce quando a peça f_luce2 (a "chave", SQ_SWON/SQ_SWOF) é
     quebrada (hit_f_luce2.lua → g_dmgf, rl2_f_luce.lua). O bot só ataca o inimigo mais perto e nunca quebrou a peça: falta
     alguém jogar. O chefe 6 (f_devl3) tem o mesmo tipo de fase (g_FIYCHACK, bck_f_devl3.lua) e não tomou dano em 150 s de bot.
   - **Projéteis e golpes à distância dos inimigos (novo):** FATO, o jogo cria os modelos x_* pelos disparadores de ação
     (beActionStarterInfo → modelo alvo → ação, sequência, quadro, deslocamento), exportados em disparos.json pelo
     `dw4 igb-disparos` (também no fase-exportar). O cliente cria os x_* com as sequências deles (MOVE, HITBOX 7xxxx, cadeias)
     e a caixa fere pelo hit_p. HIPÓTESES: o projétil com caixa sai mirando o jogador (os bits b6/b7 não foram lidos); os y_*
     (só efeito) e as partículas não existem; o projétil aparece como uma esfera.
3. **Morte.** FATO (chk_i_timenm0.lua, tempo de missão esgotado): todos em SQ_DEAD → i_fadewn01 SQ_GAMEOVER → some QS_HURRY →
   QS_DONOT → i_messwn com a mensagem → PLAYERPOINT SETGENERATEPOS I<0> → NEXTSTAGE "stagML11". O port faz a queda assim (perde os
   BITS no campo, SQ_GAMEOVER, QS_DONOT, lobby na entrada 0). FATO: o DEADRESTART (GAMERAM, mensagem 16) não é mandado por nenhum
   texto do código nem dos scripts. DESCONHECIDO: o código nativo da queda fora das missões e a mensagem exibida.
4. **Efeitos de estado no jogador.** Feito (63796e8): o golpe no jogador roda o hit_p_<forma>.lua do jogo, com os estados do
   MsgAction (PlayerStatus). HIPÓTESES: tique do veneno por segundo, efeito de cada estado no controle, queda de atributos ainda
   fora do dano.
5. **Painel de técnicas.** Lido (fase4-levantamento-combate.md): 3 listas pelo estado, anterior/próxima com repetição e a
   regra de uso do TechniquePanel. A barra do port faz o mesmo; faltam a terceira lista e o mapa dos botões.
6. **Desempenho.** Medido pelo bot (registro a cada 5 s): 60 FPS (teto do vsync), quadro mais lento 17 ms em stag0001,
   stag0101 (331 geometrias, 66 objetos), stagml12, stagml11 e chefes 2..6. Memória numa sessão longa: não medida.

## A2. Defeitos vistos pelo usuário em 2026-10-26 (resolvidos na noite de 2026-10-29/30)

- **Goburimon retorcido e NPCs em T-pose:** a mesma causa. FATO: o igModelViewMatrixBoneSelect "CharaRoot"/"gobr_main" tem
  logo abaixo um igBlendMatrixSelect com a paleta (pele normal); o exportador prendia tudo a um osso. Corrigido (3f09f71); o
  n_leomon tem 2 pesos por vértice (soma 1). Visto no visualizador (DW4_VER_MODELO) e no jogo (DW4_DIAG_NPC: todas as NPCs
  tocam a animação pedida).
- **Mapa do tutorial (stag0001) preto:** não é defeito: o chão da fase é a textura 0001ground03 (preta com faixas) sob o céu rosa
  da neblina; visto no jogo. O texto da janela do tutorial (i_messwnsp, TALKFONT05) faltava: corrigido. O bot passou o tutorial
  (Goburimons derrotados, SQ_ENJOY).
- **Barra de técnicas por cima do MP do painel:** movida para o canto inferior direito, vista no jogo.

## B. Partes do jogo feitas, mas não vistas

7. **Varredura:** as 98 fases carregam no cliente. Ainda não foi visto se todas têm chão e se os scripts rodam por inteiro.
8. **Missões (quest01..06, treasure01..08):** teste com os scripts reais e cronômetro (chk_i_timenm0.lua) no cliente; recompensa
   pela COUNTER 0. Falta jogar uma na janela.
9. **Armas e estilos:** modelo na mão e blocos de sequência pela arma (74 armas). Osso das armas duplas é HIPÓTESE.
10. **Formas:** as 15 além do Guilmon (pose, escala, animações, arma). Rosto do HUD, aba do status e nome seguem a forma (4f63c96).
11. **Loja de cartas:** sorteio do B. Pack lido no executável (3 cartas, MT % 1000 contra o KAKURITSU) e feito na stagIS11.
12. **Final:** stagbs06 → stagIS20 (créditos) → lobby. Tela de fim com a música; a arte j_endroll não foi exportada.

## C. Só refinamento (o jogo fica diferente, mas jogável)

13. **Janelas:**
    - falas: arte, rosto e fonte do jogo, vistas na tela (stagml11);
    - menu de status: fonte do jogo com os ícones, visto; os cursores passam a andar (f0dada6), sem rever;
    - troca de área (i_zzstchgwb/wn) e teleporte (i_zzstchgcur): arte do jogo, cursor corrigido (f0dada6), sem rever;
    - lojas: arte do jogo (j_shback, j_shbase, j_shwin; variante AWIN comprando, BWIN vendendo, JUNK no ferro-velho) com a fonte
      do jogo (2026-10-30, sem rever na tela). FATO: o código nativo não manda posição de texto (o TEXTCTL SETPOSITION só serve aos
      nomes de item no chão). HIPÓTESE pela geometria: cabeçalho de 28 px, 12 linhas de 16 px (y 127 a −65) e a ajuda de 3 linhas
      embaixo. Banco, X-data e LABO seguem em caixas da Godot com a fonte do jogo (variantes BITPUTIN/XDATAITEM/SINKA levantadas).
14. **Cartas:** álbum feito na loja de cartas (lista das 50); faltam a arte do pacote (j_shbooster/j_shcardNN) e o valor 0x7DA/0x7DB/0x7DC pela raridade.
15. **NPCs:** andam, falam e passeiam pela IA do jogo; não foi visto se viram para o jogador e as escalas.
16. **Filmes:** um só, a abertura da stagIS09 (MOVIE PLAY AJP_/AUS_/AEU_opening.sfd pela região). Separação e ADX prontos; falta o
    decodificador de vídeo MPEG-1 (opção recomendada: nosso, em C#, sem pacote). Ver fase5-filmes.md.
17. **Câmera:** "Dungeon camera" e "Light" vão para a GAMERAM, sem efeito no cliente.

## Já resolvido (referência)

- Menu inicial com 4 vagas por conta, configurações, save automático por vaga, dificuldade pelo hostLoad.
- Evolução (16 formas), lojas de equipamento e discos, banco, X-data (atributos e chips), loja de cartas.
- Chat e placa com nome e nível; NPCs com animação, conversa e passeio; Save Keeper removido.
- Fonte do jogo (fonte-exportar) nas falas, no status e nas caixas; troca de área e teleporte com a arte do jogo.
