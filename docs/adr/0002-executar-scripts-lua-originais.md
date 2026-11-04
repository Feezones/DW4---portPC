# ADR 0002: executar os scripts Lua 4.0 originais numa VM própria em C#

Status: **aprovada** pelo responsável em 2026-10-25 ("Ececuta os originais").

## Contexto

Boa parte das regras do jogo (eventos, diálogos, missões, golpes, dano chamado por script, itens) está em 5.291 chunks Lua 4.0
compilados dentro do `ndmw.afs` (ver `docs/formatos/lua.md`). Duas opções: traduzir os scripts para C# ou executar os originais.

## Decisão

- Executar os chunks originais numa VM Lua 4.0 escrita do zero em C# (`src/DW4.Script`), a partir de `lvm.c`, `ldo.c`,
  `ltable.c`, `lstring.c`, `lbaselib.c`, `lstrlib.c` e `lmathlib.c` da tag v4.0 (github.com/lua/lua, licença MIT). Sem pacotes.
- A VM é isolada: não existe io, os, dofile, dostring, loadfile nem rede. O script só enxerga a biblioteca padrão mínima e as
  funções que o hospedeiro registrar (a API `be:*`). Há limite de instruções por chamada contra laço infinito.
- Os chunks são lidos da cópia do próprio usuário em tempo de execução e **nunca** vão para o Git.
- O executável nativo (`main.dol`) continua proibido de executar; a API `be:*` é reimplementada em C# sobre `DW4.Gameplay`.

## Fidelidade

- **FATO (por construção)**: a tabela reproduz o `ltable.c` 4.0 (só parte hash, potência de 2, `hash_s` de 32 bits, posição
  principal, `firstfree` descendo, rehash por 3/4 e 1/4). Por isso `next`, `foreach` e `for k, v in t` percorrem as chaves de
  string e número na mesma ordem do jogo. Teste sintético confere uma sequência calculada à mão.
- **HIPÓTESE**: tabelas e funções usadas como chave usam um número de criação; no jogo o hash era o endereço de memória, que
  não dá para reproduzir. Se algum script itera uma tabela com esse tipo de chave, a ordem pode diferir.
- **HIPÓTESE**: `random` usa o `rand()` do MSL (CodeWarrior), LCG 1103515245/12345, `RAND_MAX` 32767.
- `strfind` só faz busca simples por enquanto (sem os padrões do Lua 4.0); será completado se algum script precisar.
- Ainda não implementados: tag methods (`settagmethod` etc.), `dostring`, `dofile`. O teste com os chunks do jogo lista as
  globais que faltam.

## Alternativa descartada

Traduzir para C#: muito mais trabalho (1,2 milhão de instruções) e risco de divergência em cada script.
