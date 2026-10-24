# AFS (`files/area.afs`, `files/ndmw.afs`)

| Campo | area.afs | ndmw.afs |
|---|---|---|
| Tamanho | 511.553.536 | 692.326.400 |
| SHA-256 | 2280CFE9…2BD0D371 | C5C2B015…8CC0BCCC |
| Bytes 0–3 | `41 46 53 00` ("AFS\0") | `41 46 53 00` |
| Bytes 4–7 (LE) | 0x566 = 1382 | 0x3CD = 973 |
| Bytes 8–11 (LE) | 0x3000 | 0x2000 |
| Bytes 12–15 (LE) | 0x025B1000 | 0x00432800 |
| Entropia 64 KiB | 6,85 | 7,11 |

## Hipótese

AFS é um contêiner usado pelo middleware da CRI em vários jogos de PS2, GameCube e Dreamcast, com layout publicamente descrito por ferramentas de modding: assinatura `AFS\0`, contagem de entradas, tabela de pares (offset, tamanho) em little-endian e, opcionalmente, uma tabela de nomes no fim da tabela.

Evidência parcial: a assinatura bate; o 1º offset (0x3000 e 0x2000) é alinhado e cabe depois de uma tabela de 1382 e 973 entradas de 8 bytes; os valores lidos em little-endian fazem sentido mesmo o GameCube sendo big-endian, como esperado para AFS.

**Confiança: HIPÓTESE forte.** Não validado ainda.

## Validação proposta (somente leitura, na cópia de trabalho)

1. Ler a tabela inteira; conferir que todos os offsets são crescentes, alinhados e `offset + tamanho <= tamanho do arquivo`.
2. Conferir se a soma de tamanhos mais preenchimento cobre o arquivo.
3. Procurar a tabela de nomes (entrada logo após a última da tabela, ou no fim do arquivo).
4. Para cada entrada: 16 bytes de cabeçalho e entropia, para separar formatos e detectar compressão.

Entradas com entropia alta (acima de ~7,5) podem estar comprimidas; isso é só um indicador.
