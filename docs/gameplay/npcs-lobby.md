# NPCs do lobby (Central Area, stagML12)

Relato do usuário (2026-11-02): Digi-Lab, Guardião do banco e os outros NPCs do mapa não funcionavam.

## FATO

- Falar com um NPC é o botão de ataque, como no A do original: o primeiro quadro do golpe liga a sonda de conversa
  (HITBOX 60000, 7 à frente do jogador, caixa 3) e o hit_<npc>.lua roda quando ela encosta no NPC (Fatia.Recorder.cs).
- A tecla F ("Interagir com jogador", PR #8) não é lida por nenhum código desta branch e nunca foi a tecla de conversa.
- Causa: no PC o golpe vira para o cursor do mouse (decisão do port de 2026-10-24). A sonda ia para o lado do cursor e só
  acertava o NPC com o cursor em cima dele. O bot sem janela reproduziu: perto do Data Keeper e do Card Keeper, apertou o ataque
  e a sonda saiu para o lado, sem conversa.
- NPCs da stagML12 e janelas (bot com DW4_ALVO=npc DW4_NPC=<nome/índice>, depois da correção):
  - n_digilfm/1, Data Keeper (X-data): conversa e SUBWND XDATA.
  - n_digilfm/2, Appraisal Keeper: conversa e SUBWND CNOS, **janela não portada**.
  - n_digilff/0, Bank Keeper: conversa e SUBWND BANK.
  - n_digilff/1, Digi-Lab: conversa e SUBWND LABO (captura mostra o menu de Digievolução).
  - n_digilff/2, Card Keeper: conversa e NEXTSTAGE stagIS11.
  - n_prmame/0: conversa.
  - n_digilfm/0, Save Keeper: removido antes (decisão do port).

## Correção

Ao apertar o ataque com um NPC que tem hit_<nome>.lua a até 16 (na horizontal, mesma altura), o golpe vira para ele em vez de
para o cursor (`TalkTarget` em Fatia.Recorder.cs). Longe de NPCs, o golpe continua indo para o cursor.

## HIPÓTESE

- Alcance de 16: a sonda alcança ~13,5 com o volume de conversa 10 × 20 × 10 (que também é hipótese); 16 cobre o balcão.

## DESCONHECIDO

- O que a janela CNOS (beNDMWShopCtrl do Appraisal Keeper) faz e como fica.
