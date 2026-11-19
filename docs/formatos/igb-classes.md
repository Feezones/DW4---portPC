# Classes do Alchemy dentro dos IGB

Fonte: `dw4 igb-types` em 2026-10-24 (commit 2b8fdcd), rodado no PC sobre a cópia de trabalho. Os 2274 IGB foram lidos sem erro; em todos o nº de objetos bate com o cabeçalho e o buffer de objetos termina exatamente. Saídas completas ficam em `D:\dw4-work\inventario\igb_types_*.txt` (fora do repositório).

**Confiança:** a lista de classes é **FATO** (nomes lidos das tabelas de metaobjetos). O papel de cada classe é **HIPÓTESE** pelo nome; nenhum campo foi interpretado ainda.

Prefixos: `ig*` = classes do motor Alchemy; `be*` = classes próprias do jogo (provável "Bandai Engine" ou similar, nome não confirmado).

## Resumo

| Arquivo | IGB | Objetos | Classes distintas | O que parece conter |
|---|---|---|---|---|
| `ndmw.igb` | 1 | 2.297 | 8 | Índice de áudio: 2080 `igAdxAfsFile` + configuração dos AFS (`beNDMWAfsSetup*`) |
| `area.afs` | 1379 | 3.456.158 | 90 | Cenários, interface e telas de carregamento |
| `ndmw.afs` | 894 | 3.516.157 | 250 | Personagens, animação, ações, IA, partículas, sons e scripts |

## area.afs

- **Geometria e cena:** `igGeometry` (152.896), `igVertexArray1_1`, `igGeometryAttr1_5`, `igIndexArray`, `igPrimLengthArray1_1`, `igTransform`, `igNodeList`, `igGroup`, `igSwitch`.
- **Materiais e texturas:** `igImage` (13.437), `igClut` (paletas), `igTextureAttr`, `igMaterialAttr`, `igBlendFunctionAttr`, `igFogStateAttr`, `igLightingStateAttr`.
- **Texto e fontes:** `beFontInfoData` (144.872), `beUnicodeObj` (105.924), `beDataBase`, `beDBManagerInfo`.
- **Load (686 arquivos):** `beLoadingNode`, `igCompressedAnimationSequenceQS` (9.660) — telas de carregamento animadas.
- **Ações/modelos em 13 arquivos:** `beAction2*`, `beModelCtrl*`.
- **Save:** `beSvPlatDataGC`, `beSvDeliver`, `beSvSlotDataList` (12 arquivos).

## ndmw.afs

- **Animação esquelética:** `igAnimation` (7.554), `igAnimationTrack` (215.322), `igEnbayaTransformSource`, `igEnbayaAnimationSource`, `igSkeletonBoneInfo`, `igAnimationCombinerBoneInfoList`, `igQuaternionfList`. Compressão **Enbaya** (o projeto SomeoneIsWorking/alchemy tem notas de decodificação em `docs/enbaya_decode.md`).
- **Atores:** `igActorList`, `igAppearanceList`, `beModelCtrlNode2` (99.638), `beModelCtrlInfoRamData`.
- **Ações e combate:** `beAction2Data`, `beAction2MOTIONSET`, `beAction2JOINTSET`, `beActionStarterData`, `beModelCtrlACTION`, `beModelCtrlJUMP`, **`beModelCtrlHITBOX`** (27.530), `beHitLandDelivInfoRam`, `beAsTargetModelData`.
- **IA:** `beModelCtrlAIData` (10.875).
- **Geradores / spawn:** `beGeneraterData`, `beGeneraterItemDataOne`, `beGeneraterPlayerDataList`.
- **Partículas e efeitos:** `beParticleCtrl2InfoRam`, `ParticleTimer`, `igBillboard`.
- **Som:** `beSoundData`, `beSeData`, `beSeInfoData`.
- **Scripts:** **`beLuaData` (7.300) e `beLuaDataObject` (5.291)** em 98 arquivos.
- **Números/variantes:** `beNumVerData`, `beNumVerInsideData`.
- `_com` (139 arquivos): concentra animações (`igAnimationTrack` 81.486) e ações comuns.
- `BecT`: fonte de texto (`beFontImage`, `igCharMetricsList`).

## O que isso muda

1. **Regras de jogo podem estar em dados, não só no executável:** hitboxes, ações, IA, geradores e scripts Lua aparecem como objetos. Se o `beLuaData` guardar código Lua (fonte ou bytecode), parte da lógica original pode ser lida diretamente. A verificar.
2. **Prioridades de leitura:** (a) `igGeometry`/`igVertexArray`/`igImage` para um primeiro modelo; (b) `beLuaData`; (c) animação Enbaya; (d) `beModelCtrlHITBOX` e `beAction2*` para combate.
3. Para interpretar campos é preciso decodificar os objetos com os descritores de campo dos metaobjetos (tipo, slot, tamanho), próximo passo do leitor.
