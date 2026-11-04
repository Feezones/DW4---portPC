# Armas duplas, Shot Pistol, spawn do lobby e EXP (2026-11-02)

Relatos do usuário jogando com o Veemon.

## Armas duplas (Crush Arm e outras)

- **FATO (igb-tree do w_common03 = Crush Arm, ndmw #914):** igSwitch "PC01" → igAttrSet "right" → geometria, e igAttrSet
  "left" → igTransform "Rot180" → a mesma geometria. A Shot Pistol (w_common04, #915) é igual. 33 das 74 armas têm o nó "left".
- O igb-cena juntava tudo numa malha só, presa na mão direita: as duas peças ficavam sobrepostas. Agora o exportador grava
  `maos.json` (o lado de cada superfície), e o cliente prende as superfícies "left" no osso LArm04 (`Fatia.Weapon.cs`).
- **HIPÓTESE:** os ossos das mãos são RArm04 e LArm04 (o beWeaponAttachData não foi lido). Capturas:
  `D:\dw4-work\previas\veemon_w_common03.png` e `veemon_w_common04.png`.
- A animação de ataque já vem do bloco do estilo da arma ("arm", "gun") no SQ_ATCK1 do personagem.

## Shot Pistol

- **FATO (disparos.json):** o p_vmon tem o disparador x_100gunv01 na ação VATCG1, quadro 10, deslocamento (0, 6,5, 1). O
  x_100gunv01 tem HITBOX 10000 (golpe do jogador), "Push" 2 por 20 quadros e dura uns 54. Entre os x_100*, só ele fere; os
  outros são rastros.
- O port não disparava os x_* do jogador: o tiro não saía e a pistola não dava dano. Agora sai pela ação, voa para a frente e
  acerta inimigos pela mesma conta do golpe físico (PlayerStrikes, código 10000). Some no primeiro acerto (HIPÓTESE).
- **HIPÓTESE:** o quadro do disparador do jogador é de 60 Hz. A VATCG1 dura 0,217 s (6,5 quadros de 30 Hz), e o tiro sai no
  quadro 10.
- Bot sem janela (Veemon + Shot Pistol na stag0101): 12 tiros, Goburimon de 50 para 29 HP.
- **FATO (igb-seq do p_vmon):** o golpe carregado da pistola (SQ_ATCKTM, bloco "gun") não é projétil: é um giro com pulo e
  6 caixas HITBOX 20000 em volta do jogador; o x_100gunk01 da KATCG3 é só o efeito. O port só contava caixas 10000, então
  nenhum golpe carregado acertava; agora conta 10000 e 20000 (a mesma conta do golpe físico; HIPÓTESE: sem multiplicador).

## Spawn do lobby

- **FATO:** a gatedisc.lua (uso do Gate Disk) sai com SETGENERATEPOS I<3>: o Emergency Gate, em (210, 40, 120). O port usava
  essa entrada para quem entra pelo menu. A morte e o final voltam com I<0> (Fatia.Death.cs, Fatia.Ending.cs).
- **FATO (print do usuário no original):** o Digimon aparece em (60,9, 10, −43), dentro da caixa da entrada 0 da stagml11
  (41..79 × −55..−20).
- Agora vaga nova e vaga gravada no lobby começam na entrada 0 (`Session.NewGameEntry`). **HIPÓTESE:** sem SETGENERATEPOS, o
  jogo usa a entrada 0.

## EXP e subida de nível

- **FATO (já no port, main.dol):** NEXTEXP = L²·(L + 140) + 200·L (nível 1: 341, igual ao print do original). A subida enche
  HP e MP, os atributos seguem as curvas do RECALCPLAYER, e a EXP é dividida por NINZU (P2EEXPUP).
- Corrigido: o EXPRITSU do jogador (bônus de EXP de itens) ia como 0 no P2EEXPUP.
- **DESCONHECIDO:** o efeito e o som de subida de nível do original (o port mostra "NÍVEL n").

## Rodada 2 (2026-11-02, noite)

- **Recuo da Shot Pistol.** FATO: o bloco "gun" do SQ_ATCK1 tem MOVE "Back" 0,5 2 na VATCG2 (canal Z modo 5: começa em −0,5 por
  quadro e para em 2 quadros, menos de 1 unidade). O port andava para trás na metade da velocidade de andar até o próximo MOVE.
  Agora todos os MOVE do jogador (Push, Back, Move, Stop) passam pelos canais de movimento do jogo.
- **Velocidade do tiro.** O x_100gunv01 tem "Push" 2 20. **HIPÓTESE:** o tiro do jogador anda no relógio de 60 Hz do sequenciador de
  objetos (o mesmo dos WAITCHECK "Frame" dele), o dobro do que o port usava. Os tiros de inimigo continuam a 30 Hz.
- **Baús e caixas soltam item.** FATO (drops.json da stag0101): GBOX-01, GBRL-01/02 e GWCS-01..04 têm listas de drop como os
  geradores de inimigo. Bot: barril GBRL-01 → BIT, caixote GWCS-01 → Gate Disk.
- **Barril e caixote encostados.** FATO: os pontos dos geradores são globais, numa grade de 10 (GBRL-01 em (415, 71,25, −127,5),
  GWCS-01 em (425, 71,25, −126,25)), e as caixas de corpo do jogo têm 12 e 14 de largura: no próprio dado eles ficam encostados.
  Nada mudou aqui.
- **Canhões de Numemon (g_numez01, 3 na stag0101).** FATO: o mapa de IA sorteia SQ_TIME1..4 → SQ_FIRE → fir_g_numez01.lua →
  GENERATER RESTART "GENE-54"/"GENE-16"/"GENE-19" (um Numemon cada, máximo 1 vivo por gerador). Com a IA dos objetos g_* (desde
  07fadf4), o bot vê os três geradores soltando e_nume.
