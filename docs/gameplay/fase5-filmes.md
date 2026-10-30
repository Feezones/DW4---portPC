# Filmes Sofdec: o que falta para tocar no cliente

Levantamento de 2026-10-30, feito só com leitura estática (sem abrir o jogo nem instalar nada).

## FATO

- O jogo tem **um filme só**: a abertura, em três versões de região. São as entradas de `area.afs` `AJP_`, `AUS_` e `AEU_`
  (~120 MB juntas; o nome original é `dmwxop6M.sfd`; docs/formatos/afs-conteudo.md).
- O final não é filme: os créditos são a stagIS20 feita com o motor (STAFFROL, j_endroll; Fatia.Ending.cs).
- Quem toca é a **stagIS09**. Ela fica entre os logos (stagIS08) e o título (stagIS10).
  - O `first.lua` manda `be:sd("MOVIE", nil, -1, "PLAY", X)`.
  - `X` vem de `SYSgetArea`: 0 → `AJP_opening.sfd`, 1 → `AUS_opening.sfd`, 2 → `AEU_opening.sfd`.
  - Em seguida, `STAGECTL PADINPUT ENABLE 120`.
  - O `nextStage.lua` manda `NEXTSTAGE stagis10`.
- A classe MOVIE do executável tem os comandos PLAY, STOP, ISPLAY e ISEND (cadeias do main.dol).
- O tocador é o CRI "SFD/GC Ver.1.947" (mwPly…), que lê direto do AFS (mwPlyStartAfs).
- O conteúdo (AUS_, #2) é:
  - vídeo MPEG-1 elementar, 640 × 480, 29,97 fps;
  - áudio ADX 48 kHz estéreo;
  - duração de 1:04,0.
- O contêiner é MPEG-PS. O separador já existe: `tools/DW4.Formats/Sofdec/MpegPs.cs` e `dw4 sfd-demux`.
- O ADX já é decodificado (`tools/DW4.Formats/Audio/Adx.cs`, `dw4 adx-wav`).
- A Godot 4.7 só toca Theora (.ogv) no VideoStreamPlayer. MPEG-1 não. Não há pacote NuGet no projeto e a regra é não instalar nada.

## O que falta

Só o **decodificador de vídeo MPEG-1**. Ele precisa de:

- quadros I, P e B;
- VLC, IDCT 8 × 8 e compensação de movimento de meio pixel;
- conversão YCbCr 4:2:0 para RGB.

O resto já está pronto: separação, áudio e o ponto onde o script pede o filme.

## Opções (nenhuma instala nada)

1. **Recomendada: decodificador MPEG-1 em C#, nosso, no DW4.Formats.**
   - Escrito pela norma ISO/IEC 11172-2, tomando como referência de estrutura o pl_mpeg (C, licença MIT, um arquivo, ~2 mil
     linhas com o MP2 que não usamos). Se algo vier dele, a licença é citada no arquivo.
   - Tamanho estimado: ~1.200 linhas de C#.
   - O cliente decodifica em tempo real numa ImageTexture de 640 × 480 e toca o ADX como AudioStreamWav.
   - O áudio é o relógio: a imagem acompanha a posição do som.
   - HIPÓTESE de custo: um quadro de 640 × 480 a 30 fps cabe com folga numa linha de execução separada.
   - Vantagem: lê o .sfd do próprio AFS, sem arquivos novos.
2. **Mesmo decodificador, mas convertendo antes.**
   - `dw4 sfd-quadros` grava os quadros fora do repositório (D:\dw4-work), e o cliente só mostra imagens.
   - Ocupa muito disco: ~1.900 quadros.
   - É mais simples de depurar e serve de teste para a opção 1.
3. **ffmpeg, convertendo para .ogv** (o VideoStreamPlayer da Godot toca na hora).
   - Exige instalar o ffmpeg, o que a regra veta.
   - Só se o dono autorizar.

## HIPÓTESE

- O comando MOVIE ISEND deve ser 1 quando o filme acaba.
- O STAGECTL PADINPUT ENABLE 120 deve deixar pular o filme com um botão depois de 2 s.

## DESCONHECIDO

- Quem chama o `nextStage.lua` (o fim do filme ou o botão). Não se sabe sem ler o código nativo da classe MOVIE.
- Se o filme é ampliado na tela de 640 × 480 ou mostrado com bordas.
