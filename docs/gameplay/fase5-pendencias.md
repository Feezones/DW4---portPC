# Fase 5: o que ainda impede jogar do começo ao fim (2026-10-26)

Critério da Fase 5 (roadmap): o jogo inteiro no Windows, jogável do início ao fim, com desempenho estável. "Não visto" = nunca
conferido na janela. O estado vem do repositório, dos testes (79 ok) e das janelas autorizadas.

## A. Impedem o critério (jogável do início ao fim)

1. **Caminho do enredo nunca jogado inteiro.** Lobby → stagML02 → stag0001 → masmorras dng1..dng4 → chefes stagbs01..06 → fim
   (FLG 86). O SQ_SHOK da stagML02 foi corrigido (66ebbfa). A troca de área já foi usada pelo usuário (escolha e NEXTSTAGE
   funcionam). Falta alguém jogar o caminho inteiro. Mapa das flags: fase5-enredo.md.
2. **Chefes 02..06 não vistos em luta.** Carregam (varredura) e os scripts rodam sem erro. O movimento é o do jogo, mas a escala
   da unidade (30 Hz, unidade do jogo = do mundo) é HIPÓTESE, as peças presas (f_luce "ball") nunca foram vistas e a correção
   da IA (valor 0 segura a decisão) não foi vista.
3. **Morte.** DECISÃO DO PORT (904a338): perde os BITS no campo, HP/MP cheios e volta ao lobby. O fluxo original (tela de queda,
   destino) não foi lido.
4. **Efeitos de estado no jogador** (atordoar, sono, prender, veneno, ataque/defesa baixos). O jogo faz pelos hit_p_*.lua; o port
   calcula o dano por conta própria e não roda esses scripts. Muda o combate dos chefes.
5. **Painel de técnicas.** Barra do port (1..6, K/botão direito/Y). Como o jogo escolhe a técnica no painel não foi lido.
6. **Desempenho.** Não medido: fases grandes, número de NPCs e inimigos, memória numa sessão longa.

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
    - lojas, banco, X-data e LABO: caixas da Godot com a fonte do jogo. A arte (j_shwin) e as caixas STWINFONT estão levantadas
      (fase5-lojas.md), mas a posição das caixas na tela não está nos dados (o usa_pack só tem categoria, linha, largura e
      altura da linha; o código nativo passa só ALIGN/TEXTSET). DESCONHECIDO até ler o beTextCtrl.
14. **Cartas:** arte do pacote (j_shbooster/j_shcardNN), o álbum e o valor 0x7DA/0x7DB/0x7DC pela raridade.
15. **NPCs:** andam, falam e passeiam pela IA do jogo; não foi visto se viram para o jogador e as escalas.
16. **Filmes** (abertura e final, Sofdec): o leitor existe nas ferramentas, não no cliente.
17. **Câmera:** "Dungeon camera" e "Light" vão para a GAMERAM, sem efeito no cliente.

## Já resolvido (referência)

- Menu inicial com 4 vagas por conta, configurações, save automático por vaga, dificuldade pelo hostLoad.
- Evolução (16 formas), lojas de equipamento e discos, banco, X-data (atributos e chips), loja de cartas.
- Chat e placa com nome e nível; NPCs com animação, conversa e passeio; Save Keeper removido.
- Fonte do jogo (fonte-exportar) nas falas, no status e nas caixas; troca de área e teleporte com a arte do jogo.
