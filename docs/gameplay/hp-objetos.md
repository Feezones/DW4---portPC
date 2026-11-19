# HP dos objetos quebráveis (2026-11-02)

Relato do usuário: "estruturas quebram com 1 tapa".

## Original

- **FATO (tabela enemy):** os g_* que estão nela (g_box, g_boxfield, g_bbarrelsz01/02, g_chakraz03, g_woodencasez01..05) têm
  HPMAX 100.
- **FATO (lua-trace dos hit_g_*.lua da stag0101, golpe comum 10000):** o script tira um valor fixo por golpe e manda SQ_SDMG
  enquanto sobra HP; com HP ≤ 0, manda SQ_DDMG (quebra). O dano não depende da arma.
- **FATO:** o golpe carregado (20000) manda SQ_DDMG direto, sem mexer no HP: quebra qualquer um.

| Objeto | Por golpe | Golpes com HP 100 |
|---|---|---|
| g_boxfield (caixa) | −100 | 1 |
| g_rock01z01 (pedra) | −34 | 3 |
| g_rock02z01 (pedra) | −20 | 5 |
| g_bush01z01 (arbusto) | −50 | 2 |
| g_bbarrelsz01 (barril), g_woodencasez01 (caixote) | −50 | 2 |
| g_torchz01, g_wall01z01, g_cannon01, g_numez01, g_bossymbolz01 | −50 | 2 |
| qualquer um, golpe carregado | SQ_DDMG | 1 |

## Port

- Antes: só a caixa de gerador tinha o HP da tabela. Os objetos gerados pelos scripts (pedras, arbustos, paredes, tochas,
  torretas) começavam com 50; o arbusto, que perde 50 por golpe, quebrava no primeiro.
- Agora: todo g_* começa com o HPMAX da tabela, ou 100 fora dela (**HIPÓTESE**: as contas dos scripts só fecham assim). O golpe
  carregado e o tiro da pistola também passam pelo hit_g_*.lua.
- Bot sem janela (DW4_ALVO=objetos, stag0101; Guilmon com espada, Veemon com Crush Arm e com Shot Pistol): pedra g_rock01z01
  em 3 golpes, g_rock02z01 em 5, barril e caixote em 2, com qualquer arma.
