# Andaimes sobre o buraco da dng103f2 (matt05/matt06/matt07)

Relato (feelipe, 0.6.29, onda GENERE-15): os blocos diante do matt06 seguram quem pula em cima?

## Original (FATO)

- `start.lua` (ndmw #226) gera 6 `g_dskyscaffold` em V<±20, 100, 80/120/160>, sobre o buraco `matt05`
  (volume y −20..65, z 50..190). O chão do lado do matt06 (z 190..250) e do matt07 (z −10..50) fica em y 100.
- O modelo é uma caixa 20 × 40 × 20 com o topo no y 0 local (topo em y 100 no mundo).
- A colisão da fase só tem as quatro paredes de cada pilar (`pillShape301..306`, y 60..100, 8 triângulos, sem tampa).
- O chão do topo vem do próprio objeto: `beHitLandModelInfo` #3243 **sem nome**, @10 = 0x401, @8 = igSwitch #13607
  (filho da raiz do ator) → beCopyTransform → quadrado 20 × 20 em y 0. O igSwitch tem 10 cópias (x 0..135); o port usa
  a do slot 0, a mesma do modelo visível.
- Cair no buraco (`matt05.lua`): ADDHP −99999 e PLAYER REMOVE; com todos caídos e sem backup chip na fase, o script lê o MP,
  dá PLAYER GENERATE na borda (`g_posTbl11`/`12`, ex.: V<−15, 100, 205>), SQ_ENTR, ADDHP "I<n>/I<0>/I<0>/I<1>" e
  ADDMP (MP − MMP). `matt06.lua`/`matt07.lua` marcam a borda (g_fallPos 11/12) e trazem de volta os caídos do grupo.

## Port

- Antes: o exportador só ligava `beHitLandModelInfo` com o mesmo nome do ator; o do andaime ficou de fora e o jogador
  atravessava o topo e caía no fundo (y 1). O `PLAYER GENERATE` era ignorado: o caído ficava no fundo com HP 0 e ia ao lobby.
- Agora: `IgbHud.Export` aceita o `beHitLandModelInfo` sem nome cujo @8 está dentro da árvore do ator; o cliente faz um chão
  parado (`RideLand.IsFloor`: tipo baixo 1, sem o bit de veículo) preso ao objeto. `PLAYER GENERATE` com o jogador caído põe
  na borda com HP e MP cheios (HIPÓTESE: nasce cheio; o ADDMP negativo do script só fecha assim).
- Dado: `fases/dng103f2/objetos/objetos.json` reexportado (`dw4 igb-objetos ndmw.afs 226 <saída> nome=raiz…`, mesmas raízes
  da exportação antiga). Ganha `land` no g_dskyscaffold (0x401) e, de quebra, no g_dbarrierp (0x402, sem efeito no cliente).
- Conferido sem janela (DW4_BOT=1, DW4_INICIO, DW4_ANDAR, DW4_PULAR=1): parado em cima fica em y 100; pulando do matt06
  cai no bloco de z 120 e para nele; quem cai volta em (−15, 100, 205) com HP cheio.
