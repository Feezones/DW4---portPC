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

## Cabeçalho (0x30 bytes, 12 × u32)

Ordem dos campos segundo implementações públicas para X-Men Legends / Marvel: Ultimate Alliance ([igb-blender](https://github.com/KaikoClanworth1/igb-blender), que cita IGBConverter e raven-formats de nikita488, MIT):

| Offset | Campo | ndmw.igb | area #3 dng1 |
|---|---|---|---|
| 0x00 | tamanho do buffer de entradas | 0x100 | 0x12BC |
| 0x04 | nº de entradas | 11 | 169 |
| 0x08 | tamanho do buffer de metaobjetos | 0x492 | 0x111E |
| 0x0C | nº de metaobjetos | 20 | 67 |
| 0x10 | tamanho do buffer de objetos | 0x15628 | 0x34434 |
| 0x14 | nº de objetos | 2297 | 4942 |
| 0x18 | tamanho do buffer de referências de memória | 0x23B4 | 0x33064 |
| 0x1C | nº de referências de memória | 108 | 1669 |
| 0x20 | tamanho do buffer de metacampos | 0x656 | 0x656 |
| 0x24 | nº de metacampos | 48 | 48 |
| 0x28 | marca 0xFADA (define a ordem de bytes) | LE | LE |
| 0x2C | versão (16 bits baixos) + flags (bits 28–31) | 6, flags 0xF0 | 6, flags 0xF0 |

**Confiança: HIPÓTESE forte.** A marca 0xFADA e a versão 6 batem com o que essas ferramentas descrevem para MUA no PS2 (IGB versão 6). Os arquivos do DW4 são little-endian mesmo no GameCube. Validação em curso: `dw4 igb-headers` lê o cabeçalho das 2273 entradas e confere se os buffers declarados cabem no tamanho de cada uma.

## Ferramentas

Avaliadas em 2026-10-24 só pela leitura do código (nada foi executado):

| Projeto | Linguagem | Licença | O que faz | Avaliação |
|---|---|---|---|---|
| [KaikoClanworth1/igb-blender](https://github.com/KaikoClanworth1/igb-blender) | Python (add-on Blender 4.4) | não declarada | Importa IGB de XML1/XML2/MUA (inclusive PS2, IGB v6): malhas, texturas, esqueletos; animação marcada como quebrada | Melhor referência de formato. Sem licença: usar como documentação, não copiar código |
| [SomeoneIsWorking/alchemy](https://github.com/SomeoneIsWorking/alchemy) | C/C++20 + Python | não declarada | Leitor IGB nativo, malhas, imagens, animação | Referência secundária; sem licença |
| IGBConverter / raven-formats (nikita488) | — | MIT | Citados como fonte do formato | A localizar e avaliar; licença clara |
| [ChmtTnky/IGBDataExtractor](https://github.com/ChmtTnky/IGBDataExtractor) | — | — | Depende do SDK proprietário Alchemy 5.0 | Descartado |

Fórum da comunidade: [marvelmods.com](https://marvelmods.com/forum/index.php/topic,10797.30.html).

## Plano

1. Documentar o cabeçalho e a tabela de metacampos de `ndmw.igb` com um leitor próprio, pequeno, só de leitura.
2. Verificar se 5 entradas de `stag` e `dng1` dos AFS têm a mesma estrutura.
3. Avaliar ferramentas existentes de IGB contra esses arquivos.
