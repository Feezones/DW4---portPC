# Scripts Lua 4.0 (`beLuaData` / `beLuaDataObject`)

Fonte: `dw4 lua-scan` (commit 253f024) em 2026-10-24, no PC, sobre a cópia de trabalho. Listagens geradas ficam em `D:\dw4-work\lua\` e **não vão para o repositório** (derivadas de conteúdo protegido).

## Fatos

- Só `ndmw.afs` tem scripts; `area.afs` tem 0. **FATO**
- **5.291 chunks**, todos lidos sem erro pelo leitor próprio (`DW4.Formats/Lua/Lua40Chunk.cs`, escrito a partir de `lundump.c`/`lopcodes.h` da tag v4.0 de github.com/lua/lua, MIT). 7.863 funções, 1.196.311 instruções; 2.902 chunks distintos por SHA-256. **FATO**
- Cabeçalho: `1B 4C 75 61 40 01 04 04 04 20 06 09 08` + número de teste 3,14159265358979323846E8 → Lua 4.0, little-endian, int/size_t/Instruction de 4 bytes, instrução de 32 bits, double de 8 bytes. **FATO**
- Informação de debug removida (sem nomes de locais; linhas = 0). Nomes de **constantes, globais e métodos** estão presentes. **FATO**
- Cada `beLuaData` liga um **nome de arquivo original** (`boot.lua`, `hit_p_agumon.lua`, `bht_x_200fire01.lua`, …) a um `beLuaDataObject`, que aponta para o bloco de memória com o chunk. Vários nomes podem apontar para o mesmo chunk (código compartilhado). **FATO**
- `boot.lua` tem 43 variantes distintas (provavelmente uma por fase). **FATO** (contagem); a relação com fases é HIPÓTESE.

## API do motor usada pelos scripts

Quase todas as chamadas passam pelo objeto global `be` (133.810 `PUSHSELF`), no estilo `be:Metodo(...)`. 108 métodos distintos. Grupos por prefixo (papel = HIPÓTESE pelo nome):

| Prefixo | Exemplos | Papel provável |
|---|---|---|
| `DM` | `DMsetSD`, `DMchkSD`, `DMrun`, `DMcase`, `DMsetLua`, `DMswtSD`, `DMgetCount` | Sequências de diretor/demo (eventos, cutscenes) |
| `MDL` | `MDLsetSD`, `MDLsetSeq`, `MDLgetHp`, `MDLaddHp`, `MDLsetDMGTurn`, `MDLisLive`, `MDLgetBulletKind` | Modelos/atores: HP, sequência de animação, estado |
| `BLT` / `BLP` | `BLTcalcSkillUp`, `BLTcalcEXPUp`, `BLTgetDamageValue`, `BLTisCritical`, `BLTisHitCheck`, `BLTgetPoisonTime`, `BLTgetSleepTime`, `BLTgetBindTime` | **Batalha**: dano, crítico, acerto, status, EXP |
| `SYS` | `SYSgetPlayerName`, `SYSisJoinPlayer`, `SYSgetDifficulty`, `SYSgetPlayerNinzu`, `SYSisGetShinkaDigimon` | Sistema: jogadores, dificuldade, evolução |
| `MAT` | `MATisPlayer`, `MATgetPlayerMatStat`, `MATisAllPlayer` | Estado de jogadores no grupo |
| `FLG` / `COUNTER` | `FLGisOn`, `FLGset`, `COUNTERget`, `COUNTERset` | Flags e contadores de progresso |
| `TXT` / `CFL` | `TXTgetCallNo`, `CFLsetName`, `CFLgetFolderName` | Texto e pastas de recursos |

Globais do próprio script mais usadas: `funcOneMessage`, `funcOpenMessage`, `funcCloseMessage`, `funcChoiceMessage` (diálogos), `g_SItemDB`/`g_TItemDB` (itens), `funcTechWazaHitCheck`/`funcTameWazaHitCheck` (golpes), `funcStageChange`, `funcPlayerStatusGenerate`, e 119 variáveis `mswkPRG0000`–`mswkPRG0143`.

Convenção de nomes de arquivo (HIPÓTESE): `hit_` colisão, `ded_` morte, `bck_` recuo, `bht_x_` projéteis, `_e_` inimigo, `_g_` objeto de cenário, `_p_` jogador, `_n_` NPC.

## Por que isso importa

Boa parte das **regras de jogo** (diálogos, eventos, dano, crítico, status, EXP, itens, progressão) está nesses scripts, que chamam uma API nativa bem delimitada (`be:*`). Para o port isso abre uma opção a decidir mais adiante (ADR):

1. **Executar os scripts originais** com uma VM Lua 4.0 no cliente, reimplementando só a API `be:*` em C#. Máxima fidelidade das regras escritas em script; os scripts seriam lidos do disco do próprio usuário, nunca distribuídos.
2. **Traduzir** os scripts para C#/GDScript a partir das listagens. Mais trabalho e risco de divergência.

As fórmulas internas de `BLTcalc*`/`BLTgetDamageValue` estão no executável (código nativo), não nos scripts; para essas vale a medição comparativa.

## Decisão (2026-10-25)

O responsável escolheu executar os originais: ver `docs/adr/0002-executar-scripts-lua-originais.md` e `src/DW4.Script`.
