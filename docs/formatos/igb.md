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

**Confiança: HIPÓTESE forte.** A marca 0xFADA e a versão 6 batem com o que essas ferramentas descrevem para MUA no PS2 (IGB versão 6). Os arquivos do DW4 são little-endian mesmo no GameCube. Resultado de `dw4 igb-headers` (2026-10-24): **todos os 2274 IGB** (ndmw.igb + 1379 em area + 894 em ndmw) são versão 6, little-endian, e os buffers declarados cabem em todos. Flags: 0xF0000000 em 1979, 0xB0000000 em 293 (sem diretórios externos), 0x30000000 em 2 (sem info nem externos). Metacampos 46–48, metaobjetos 9–226, objetos 0–46.483.

Sobra de 420 a 123.098 bytes além dos buffers do cabeçalho em todos os arquivos. Explicação esperada: seções com tamanho próprio (alinhamento, diretórios externos, nomes de pools, índice, info). `dw4 igb-layout` percorre essas seções e confere se o arquivo fecha exatamente.

**Resultado de `dw4 igb-layout` (2026-10-24): o layout fecha byte a byte em todos os 2274 IGB, sem erro.** Ordem das seções (v6): cabeçalho (48), metacampos, alinhamento (prefixo u32), metaobjetos, diretórios externos (se flag 0x40000000), nomes de pools (se 0x10000000), entradas, índice (prefixo u32), info (4 bytes, se 0x80000000), objetos, memória. A sobra do `igb-headers` era a soma de diretórios externos, nomes de pools, índice e info. **Confiança: FATO** para a divisão em seções; o conteúdo de cada seção ainda não foi interpretado.

Exemplo (`area` #3 dng1, 447.581 B): metacampos 1622, alinhamento 76, metaobjetos 4382, externos 23, pools 336, entradas 4796, índice 13.230, info 4, objetos 214.068, memória 208.996.

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

## Geometria e texturas (conferido em 2026-10-24)

Leitura feita por `dw4 igb-export` em `area.afs` #598 (stag, logotipo) e #3 (dng1, interface de masmorra). Os slots de campo seguem a descrição pública do igb-blender e foram conferidos pelos resultados abaixo.

| Item | Fato observado | Confiança |
|---|---|---|
| Cena | `igSceneInfo` → raiz `igNode`; filhos via `igNodeList`; `igAttrSet` com `igAttrList`; `igTransform` com `Matrix44f` | alta (217 transformações e 217 geometrias visitadas no #3, 0 problemas) |
| `igGeometryAttr1_5` | ObjectRef → `igVertexArray1_1`, `igIndexArray` (opcional), `igPrimLengthArray1_1`; Enum = primitiva (3 lista, 4 faixa) | alta |
| `igVertexArray1_1` | MemoryRef → `igExternalIndexedEntry` de 80 B (20 índices de bloco); UnsignedInt = nº de vértices; Struct = formato (0x00010003 = posição+normal+1 UV; 0x00000001 = só posição) | alta |
| Fluxos de vértices | slot 0 posição float×3, slot 1 normal float×3, slot 11 UV float×2; passos 12/12/8 B batem com o nº de vértices | alta nas amostras |
| `igImage` | 8 bpp indexado (formato 65536), índices em ordem **linear** (não em blocos GX), linhas de **baixo para cima** | alta: logotipo BANDAI e folha de status legíveis depois de inverter |
| `igClut` | formato 7 = RGBA8888, 256 cores, alfa até 255 | alta: cores corretas |
| UV | v = 0 na base da imagem (convenção OpenGL); sem inversão no OBJ | alta: captura na Godot do #598 mostra o logotipo legível e na orientação certa |
| Alfa | a paleta tem alfa (cantos do logotipo); o material OBJ importado é opaco | fato; no port, materiais com alfa precisam de recorte ou mistura |
| Interface (#3) | quads de 6 vértices só com posição e sem textura associada pelo grafo | fato; como são desenhados ainda é desconhecido |

A primeira versão do exportador gravava as duas leituras (linhas e blocos 8×4). Só a linear deu imagem coerente; a variante em blocos foi removida.

Os OBJ e PNG exportados importaram na Godot 4.7.2 mono sem erro nem aviso (`--headless --import`).
