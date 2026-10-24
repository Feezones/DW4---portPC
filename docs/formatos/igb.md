# IGB (Intrinsic Alchemy)

## `files/ndmw.igb`

- 107.527 bytes; SHA-256 1A126324…898AC451.
- Primeiros 64 bytes:
```
00 01 00 00 0B 00 00 00 92 04 00 00 14 00 00 00
28 56 01 00 F9 08 00 00 B4 23 00 00 6C 00 00 00
56 06 00 00 30 00 00 00 DA FA 00 00 06 00 00 F0
15 00 00 00 01 00 00 00 00 00 00 00 0F 00 00 00
```
- 2.527 strings; as primeiras 40 são nomes de tipos de metacampo do Alchemy (`igObjectRefMetaField`, `igIntMetaField`, … `igVec4ucMetaField`).

**Confiança: FATO** que é um arquivo IGB do Intrinsic Alchemy v3.2: o executável identifica o motor e a classe `igIGBFile`, e o arquivo começa pela tabela de metacampos do Alchemy. Campos do cabeçalho em little-endian.

## Entradas dos AFS

HIPÓTESE: a maioria das entradas de cenário (`dng*`, `dngq`, `stag`) também são IGB. Evidência: começam com o mesmo padrão de pares de u32 pequenos (por exemplo `BC 12 00 00 A9 00 00 00 1E 11 00 00 43 00 00 00`), compatível com um cabeçalho de seções (tamanho, contagem) como o de `ndmw.igb`; o executável cita arquivos `stag*.igb`. Validar procurando a tabela de metacampos dentro de algumas entradas.

## Ferramentas

O formato IGB é conhecido pela comunidade de modding de X-Men Legends e Marvel: Ultimate Alliance (fórum [marvelmods.com](https://marvelmods.com/forum/index.php/topic,10797.30.html)), que usa o mesmo motor. Ainda não avaliei ferramentas específicas: procedência, licença e compatibilidade com a versão 3.2 (os jogos da Marvel usam versões diferentes do Alchemy) precisam ser checadas antes de baixar qualquer coisa.

## Plano

1. Documentar o cabeçalho e a tabela de metacampos de `ndmw.igb` com um leitor próprio, pequeno, só de leitura.
2. Verificar se 5 entradas de `stag` e `dng1` dos AFS têm a mesma estrutura.
3. Avaliar ferramentas existentes de IGB contra esses arquivos.
