# `sys/main.dol` (executável GameCube)

Leitura só do cabeçalho e de strings, em 2026-10-24. Nada foi executado nem desmontado.

- Tamanho 5.196.512 bytes; SHA-256 EC61C010…E458E471.
- Formato DOL (big-endian). **FATO** (cabeçalho consistente).
- Entry point 0x80005240. BSS 0x804F2180, 480.402 bytes.

| Seção | Offset | Endereço | Tamanho |
|---|---|---|---|
| T0 | 0x00000100 | 0x80003100 | 9.472 |
| T1 | 0x00002600 | 0x80020160 | 4.140.288 |
| D0 | 0x003F5300 | 0x80005600 | 54.688 |
| D1 | 0x004028A0 | 0x80012BA0 | 54.720 |
| D2 | 0x0040FE60 | 0x80412E60 | 64 |
| D3 | 0x0040FEA0 | 0x80412EA0 | 32 |
| D4 | 0x0040FEC0 | 0x80412EC0 | 328.256 |
| D5 | 0x00460100 | 0x80463100 | 585.856 |
| D6 | 0x004EF180 | 0x8055CF00 | 17.792 |
| D7 | 0x004F3700 | 0x80566220 | 5.088 |

## Bibliotecas identificadas por strings (FATO: strings presentes no binário)

11.766 strings ASCII com mais de 8 caracteres.

- **Intrinsic Alchemy v3.2** ("Intrinsic Alchemy v3.2 -0811 (Static/Release)"): namespace `Gap::` (Core, Gui, Bec), `igIGBFile`, `igBlockMemoryPool`, `igBitmapFont`; plugins do jogo `libBecRuntimePlugin` e `libNdmwRuntimePlugin`.
- **Integração CRI no Alchemy**: `igAdxAudio`, `igAdxAfsFile`, `igAdxAfsFileList`, `igCriMovieCodec`, `igCriMovieData`, `Gap::Bec::beCriAudio`, `beCriSndf`, `beCriAcxData`, `beCriFxInfo`.
- **CRI Middleware** (out/2004): ADXT/GC 9.31b, ADXF/GC 7.18, ADXGC 1.21, CVFS, SJ, SVM, SFD/MPS/MPV/DCT, **Sofdec MWSFD/GC 3.33b**, SNDF 2.09a, entre outros. Mensagens como "Illigal format(not AFS)" confirmam que o AFS é lido pela biblioteca ADXF da CRI.
- **Nintendo Dolphin SDK** (AI, AX, AR, ARQ, CARD, DSP, DVD, GX, OS, PAD, VI, SI, EXI).
- **Metrowerks CodeWarrior** ("Metrowerks Target Resident Kernel for PowerPC").
- **zlib 1.1.3** e **libpng 1.0.6**.
- Nomes de arquivo citados: `BecText.igb`, `_common/sbiallse.igb`, `zdiscerr.igb`, `stagis08.igb`, `igInstanceSceneTemp.igb`.

## Consequências

- Os assets 3D do jogo usam o formato de cena do Alchemy (IGB). Ver `igb.md`.
- Os vídeos são Sofdec (MPEG em contêiner CRI) e o áudio é ADX. Ver `afs-conteudo.md`.
- `Bec` e `Ndmw` parecem ser os módulos próprios do jogo sobre o Alchemy (HIPÓTESE pelo nome; `ndmw.afs` e `ndmw.igb` usam o mesmo nome).
- zlib está linkado: algumas entradas podem estar comprimidas com deflate (HIPÓTESE, não observado ainda).
