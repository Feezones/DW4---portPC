# Fase do barco (stag0103) — 2026-11-05

Relato do usuário: "fase do barco não progride, barco não se movimenta". No print: Dorumon parado em cima de um barco azul,
câmera `ndmw_0628`, scripts no HUD terminando em `PLAYER MOVETRANS "V<575,15,395>/V<0,-90,0>"`, `PADINPUT ENABLE`, a
janela de fala e "inimigo HP - (vivos 0)".

## Qual fase

- **FATO:** câmera `ndmw_0628` = entrada **#628 do ndmw.afs = stag0103** (gerador N #630, sons #627, usa_pack area #436).
  É o rio da fábrica dos Numemon: o jogador desce o rio de barco até a fábrica, passando pelas jaulas dos Numemon
  (`g_numeprisonz01`) e pelos Digi-Lab presos (`n_digilfm`, flags 89..98).
- Levantamento por leitura estática das listagens Lua (`dw4 lua-scan`), `dw4 igb-seq`, `igb-tree`, `igb-dump` e
  `igb-objetos`. Nada foi executado do jogo e nenhum script ou dado do jogo entrou no Git.

## Como o original funciona

### Começo (start.lua e first.lua)

- **FATO:** o `start.lua` gera o trilho `r_boatz01` na origem e o barco `g_boatz01` no cais em (585, 15, 395) virado −90°,
  mais as jaulas, os pods e os painéis `i_stpnlsp04`/`i_stpnlsp05` (moldura com o desenho do barco e o medidor `boathp`) em
  V<−296, 225, 0>, no alto da tela.
- **FATO:** o `first.lua`:
  - desliga cura, cura de estado, subida de atributo e Gate Disk (`NDMWPANEL NO* I<1>`) e zera as flags 481..489;
  - conta os jogadores e escolhe a tabela da vida do barco (ver abaixo);
  - põe cada jogador em cima do barco (`PLAYER MOVETRANS` em (575..595, 15, 388..400));
  - mostra as falas (TALKFONT01 I<0>, I<1>), toca a demo de câmera `nume1` e, se o Digi-Lab 0 está preso, outra fala;
  - abre os painéis e, no fim: `MODELCTRL r_boatz01 SQ_GO` (20 quadros depois), `MODELCTRL g_boatz01 SQ_START`,
    som `ST_SPE01`, `DMrun` e devolve os controles (`SWITCHCTRL ALLON`).

### Como o barco anda

- **FATO:** `r_boatz01` não tem malha. É só o nó `route00` (igTransform com igTransformSequence: 46 chaves de posição e
  giro em 180 s, do cais (585, 15, 395) até (537, 15, −154), passando por x −546 entre z 200 e −187) com um
  **igTransformRecorder** embaixo.
- **FATO:** sequências do `r_boatz01`: `SQ_GO` = ação WAIT01 (`JOINTSET 0..4500 "OS"`, quadros de 60 Hz = 75 s) e
  `SQ_GO02` = `LUACALL "brk"` + WAIT02 (`JOINTSET 4501..11040`) + `SQ_WAIT`.
- **FATO:** todas as sequências do `g_boatz01` menos a de afundar (`SQ_START`, `SQ_ADAM/BDAM/CDAM`, `SQ_SILENT`) fazem
  `MOVERL "JOINT" "r_boatz01" ""`: o barco copia a matriz do trilho a cada quadro. A animação dele (`JOINTSET 10..98 LP`)
  é só o balanço.
- **HIPÓTESE (forte):** o GETTRCDATA que o MOVERL "JOINT" pede (main.dol 0x8030ACA8) é o dado do igTransformRecorder: o
  barco segue o `route00` animado.
- **FATO:** `brk_r_boatz01.lua` (aos 75 s, no `SQ_GO02`): se `g_IFLG10 == 0` (o `matg00.lua` zera isso quando o jogador
  entra na área da comporta sem demo tocando), derruba a comporta e os olhos (`SQ_DEAD02`) e treme a câmera.

### O jogador no barco

- **FATO:** o `g_boatz01` tem `beHitLandModelInfo` próprio com @10 = 0x1405: uma caixa aberta de 34 × 46 com chão em y 0 e
  paredes de 100, sob um `beCopyTransform` (anda com o barco). Os objetos quebráveis comuns têm 0x402; o blindado
  `g_soukouz02` da stag0203, que também anda preso a um trilho, tem 0x1403.
- **HIPÓTESE:** o bit 0x1000 marca veículo; quem está dentro anda junto. O jogador pode andar e atacar dentro do barco,
  mas não sai dele (as paredes).

### Inimigos e ataques

- **FATO:** os volumes de área (gatilhos `mat*.lua`) disparam conforme o barco passa:
  - `matb03/06/08/09/12`: apagam os Numemon que ficaram para trás e ligam os geradores seguintes (GENE-25.. GENE-122, Numemon
    da linha 12 nas margens);
  - `matb03` também gera as bombas, a comporta (`g_factgatez06`), os olhos, seis torres (`g_facttowerz06`, inimigo com
    hit_*.lua) e as prateleiras;
  - `mate00/mate03`: ligam e desligam os pods de Numemon (`SQ_POWERON/POWEROF`);
  - `matg00`: demo "gate" da comporta.
- **FATO:** o golpe que acerta o jogador roda o `hit_p_<forma>.lua` desta fase (o mesmo arquivo para todas as formas). Além
  do de sempre (SQ_SDMG), ele **tira vida do barco**, não do jogador.

### Vida do barco

- **FATO (`first.lua`):** `g_boathp` começa em 100. Por número de jogadores (`SYSisJoinPlayer`):

  | Jogadores | Dano por golpe (`g_dmgnum`) | Medidor no começo (`g_meta`) | Faixas de dano |
  |---|---|---|---|
  | 1 | 5 | 50 | 75, 50, 25, 0 |
  | 2 | 4 | 50 | 76, 52, 28, 0 |
  | 3 | 3 | 75 | 76, 52, 28, 1 |
  | 4 | 2 | 100 | 74, 50, 24, 0 |

- **FATO (`hit_p_*.lua`):** a cada golpe no jogador: `g_boathp −= g_dmgnum`; `g_meta −= g_dmgnum × 0,5` (1 e 2 jogadores),
  × 0,75 (3) ou × 1 (4); `METERCTRL boathp SETPERCENT F<g_meta>`. Ao chegar exatamente em cada faixa, o barco troca de
  estado (`SQ_ADAM`, `SQ_BDAM`, `SQ_CDAM`: balanço com mais estrago) e a câmera treme. Com um jogador, são **20 golpes**.
- **FATO:** na última faixa: `SQ_DDAM` (o barco para: `MOVE "Stop"`), controles desligados, jogador em `SQ_DDMG`, cura e
  Gate Disk de volta, `i_fadewn01 SQ_GAMEOVER` e `NEXTSTAGE stag0102` no ponto 4: a fase recomeça do lado de fora.
- **HIPÓTESE:** com 1 jogador o medidor começa pela metade (g_meta 50): é o que o script manda.

### Fim

- **FATO (`matf00.lua`):** perto da fábrica (x −560..−530, z −160..−130; o barco passa por ali aos ~104 s) e sem demo
  tocando: flag 489, demo "bomb", as bombas `g_annihilativebomz06` disparam, branco (`i_fadewhout`), troca de área e
  `NEXTSTAGE stag0102` no ponto 4.

## O que estava errado no port

1. **O trilho não existia.** O `fase-exportar` só exportava objetos `g_`, `n_` e `f_`; o `r_boatz01` ficava de fora. O
   `MODELCTRL r_boatz01 GENERATE` não gerava nada, o `MOVERL` do barco não achava a peça e o barco ficava parado no cais.
   Sem o barco andar, nenhum volume `mat*` disparava: nada de inimigos ("vivos 0") e nada de fim.
2. **O MOVERL "JOINT" copiava a raiz da peça.** Mesmo com o trilho, a raiz do `r_boatz01` fica na origem (0, 0, 0); o barco
   iria para lá.
3. **Sem chão de veículo:** a colisão própria do barco não era lida, e nada levava o jogador junto.
4. **Sem painel da vida do barco:** `i_stpnlsp04/05` não eram exportados nem desenhados, e o `METERCTRL boathp` não tinha
   quem respondesse. (A conta da vida já funcionava: é o próprio `hit_p_*.lua` rodando.)

## Correções (branch claude/project-thread-z3hcom)

- Exportador (`DW4.Tools`): `r_*` KIND_OBJECT entra nos objetos da fase; `igb-objetos` grava em cada ator o `land` (nó @8 e
  tipo @10 do `beHitLandModelInfo` com o mesmo nome); o HUD da fase inclui `i_stpnlsp04/05` quando existem.
- Cliente:
  - `HudActor` cria um nó para o `igTransformRecorder`; o `AttachTarget` do MOVERL "JOINT" usa esse nó quando a peça tem
    gravador (senão, a raiz, como antes).
  - Objeto com `land` de veículo (0x1000, `RideLand`) ganha a colisão própria presa ao ator (as duas faces seguram) e, a
    cada quadro, quem está dentro da caixa anda e gira junto (`CarryRiders`, antes do movimento do jogador).
  - `Fatia.StagePanel`: `i_stpnlsp*` GENERATE mostra o painel no alto da tela (preso à borda esquerda) e `METERCTRL <medidor>
    SETPERCENT` mexe no medidor dele (`HudOverlay.SetStageMeter`, mesma regra de tempo do HP).
- Testes (`dw4-testes` com os dados): trilho, gravador, 46 chaves em 180 s, tipo 0x1405 do barco, medidor `boathp`, LUACALL
  "brk"; e o `RideLand` sintético.

## Dados a regerar (fora do Git)

```
dw4 fase-exportar <ndmw.afs> <area.afs> stag0103 <pasta>
```

e copiar `objetos/` e `hud/` para `fases\stag0103` (ou reempacotar a fase). Feito em `D:\dw4-work\fases\stag0103` em
2026-11-05 (cópia anterior em `D:\dw4-work\tmp\stag0103-antes-barco`). O pacote do launcher só leva isso quando a fase for
reempacotada (`dw4 empacotar`).

## A conferir na tela

- O barco sai do cais depois das falas e da demo `nume1`, faz a curva e desce o rio; o jogador vai junto e não cai.
- Aos ~75 s a comporta cai (se a demo "gate" tocou antes).
- Numemon aparecem nas margens; golpe no jogador baixa o medidor do barco; o barco muda de estrago em 75/50/25.
- Perto da fábrica (~104 s): demo das bombas, branco e volta para a stag0102.
- **Não conferido:** se o barco gira para o lado certo (quatérnio conjugado como nos outros nós; o primeiro giro bate com
  os −90° do GENERATE) e se o jogador é empurrado pelas paredes nas curvas.

## Observação do print

- O F3 mostrava o jogador em (575, **1**, 395): o `MOVETRANS` o pôs em y 15, em cima do barco, mas sem a colisão do barco ele
  atravessou e ficou no leito do rio. Com o chão de veículo ele fica em y 15. A janela de fala do print (seta amarela) é a
  primeira fala do `first.lua`, esperando o botão; depois dela vêm a demo `nume1` e a partida do barco.

## Outros relatos do mesmo pedido (2026-11-05)

- **Backup chip lá embaixo depois de cair num buraco.** DECISÃO DO PORT: o chip fica na última plataforma pisada
  (`SafeGround`, DW4.Gameplay). Pousar mais de 40 abaixo de onde saiu do chão conta como queda e não move o ponto, até o
  jogador pisar de novo perto daquela altura; descer escada ou rampa sem sair do chão atualiza normalmente.
- **Preso embaixo de um inimigo morrendo.** DECISÃO DO PORT: com o HP em 0 (ou já derrotado) o corpo do inimigo sai da camada
  de colisão que o jogador enxerga (`UpdateEnemy`), durante toda a animação de queda; volta se ele renascer.

## 0.6.13: o jogador ainda atravessava o barco (2026-11-05)

- Relato: na 0.6.13 (que já tinha os dados novos: `r_boatz01`, `land` 0x1405 do g_boatz01 e o painel, conferidos no
  `fases-stag0103.pcz` instalado) o Dorumon continuava no fundo do rio e o barco parecia parado no cais.
- **Causa (conferida sem janela, `DW4_DIAG_BARCO=1`):** o chão do barco existia e o barco andava pelo trilho, mas o jogador
  ficava em y 1. A malha de colisão do IGB tem o chão em ordem anti-horária vista de cima, e a Godot toma o sentido horário
  como frente. O `SnapToFloor`, que roda depois do `PLAYER MOVETRANS` e na entrada da fase, só aceita faces de frente
  (`HitBackFaces = false`): o raio passava pelo chão do barco e punha o jogador no leito do rio. Dali o barco saía sem ele.
- **Correção:** o chão do veículo é montado com a ordem dos vértices invertida (`MakeRideLand`). A colisão continua com os
  dois lados ativos (as paredes da caixa seguram de dentro).
- **Conferido sem janela** (Godot `--headless`, `DW4_FASE=stag0103 DW4_BOT=1 DW4_DIAG_BARCO=1`, dados de
  `D:\dw4-work\fases`, iguais aos do pacote): o jogador fica em y 15 desde o começo. Quando o barco parte (SQ_START, preso ao
  trilho), ele vai junto, de (575, 15, 395) até perto de (−335, 15, 269) em ~58 s, sempre no chão do barco. Os Numemon
  acertam o jogador e o barco passa para o SQ_ADAM.
- Os dados da stag0103 não mudam: o pacote 0.6.14 só precisa do cliente novo.

## 0.6.14: torres, jaulas e vida do barco (2026-11-05)

Relato: o barco já anda e leva o jogador, mas (1) as "torres" azuis das margens não atiram, (2) as jaulas verdes com um NPC
não quebram e (3) a vida do barco não aparece nem parece baixar.

### Torres = pods de Numemon (g_numepodz01)

- **FATO (igb-seq e disparos):** o pod (KIND_ENEMY, base redonda com canhão) começa no rótulo DEF (parado). O `mate00.lua` e
  o `mate03.lua` mandam `SQ_POWERON` (rótulo RDY) quando o jogador entra na área e `SQ_POWEROF` quando sai. Em RDY, o mapa de
  IA MAP00 ("NEAR", faixas de 40, 130 e 140) escolhe `SQ_SWON` (rótulo ON) ou `SQ_SWOF` (volta a RDY). Em ON, o `SQ_WAIT`
  toca o WAIT02 (3 s) e faz `JUMP "SQ_FIRE"`. A ação FIRE solta dois `x_bltnumemisz01` (deslocamentos ±4,8, 6, 0). O míssil
  anda reto (`MOVE "Move" 1 1`) com a caixa 70108 por 240 quadros e some ao bater (bht → SQ_HIDE). Quando é destruído, o pod
  solta o efeito y_cannon02.
- **Erro no port:** a IA escolhia `SQ_SWON` a cada quadro, e cada escolha recomeçava o `SQ_WAIT` no rótulo ON. O WAIT02 nunca
  chegava ao fim e o `SQ_FIRE` nunca vinha.
- **Correção:** `ObjectSequencer.OnlyJumpsBack` também reconhece o bloco que só põe o rótulo que já está valendo e faz o
  JUMP de volta. A IA não recomeça mais a sequência. Sem janela: os pods passam a soltar os dois mísseis a cada ciclo.

### Jaulas (g_numeprisonz01)

- **FATO (hit_g_numeprisonz01.lua):** o golpe carregado (código 20000) não faz nada. Técnica de 40040 a 40099 quebra a jaula
  na hora (`SQ_DDMG`). Qualquer outro golpe tira 20 (HP 100, ou seja, 5 golpes). Na quebra, o Digi-Lab preso daquela jaula
  (`n_digilfm` com o mesmo índice) faz `SQ_THANK` e `SQ_EXIT`, o contador `i_digilfcount` acende (`SQ_OF2ON`) e a flag
  89 + índice liga. Essas flags valem para a próxima vez: com elas ligadas, o `start.lua` não gera mais aquele Digi-Lab.
- Do barco, o golpe de perto não alcança as jaulas: elas ficam a ~40 do trilho, e o barco tem paredes. Só técnica ou tiro.
- **Erro no port:** a esfera da técnica só procurava inimigos, nunca objetos. Os tiros de arma usavam a caixa fixa da g_box
  (14 × 10 × 14) em vez da caixa do corpo da jaula (20 × 20 × 20).
- **Correção:** a técnica acerta objetos com hit_*.lua (código 40000 + linha da técnica, a mesma HIPÓTESE do golpe em
  inimigos), e tiro e técnica usam as caixas do corpo do objeto (`ObjectTouched`).

### Vida do barco

- **FATO:** o barco só perde vida pelos golpes que acertam o jogador (`hit_p_*.lua` da fase). Não há script de dano no próprio
  barco nem projétil que mire nele.
- Sem janela, os golpes dos Numemon acertam, o `METERCTRL boathp SETPERCENT` desce (50 → 47,5 → 45…) e o barco passa por
  `SQ_ADAM` e `SQ_BDAM`. A conta já funcionava.
- **Erro no port:** o painel. As malhas do `i_stpnlsp04/05` ficam em z 0..60, e as do painel do jogador em z −90..−143. A
  câmera ortográfica do HUD (em z 0, olhando para −z, near 1) deixava o painel do barco atrás dela, e ele nunca aparecia.
- **Correção:** o painel do barco vai 200 para dentro em z. A ordem entre a moldura e o medidor é mantida. Conferido o desenho
  com `dw4 hud-previa`: moldura com o barco e a barra amarela.
- "golpes no Guilmon" no F3 é um contador antigo da fatia (golpes do jogador no Numemon fixo). Não é a vida do barco.
