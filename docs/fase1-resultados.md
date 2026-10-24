# Fase 1: resultados (2026-10-24)

Critérios do roadmap para F1: especificação com teste automatizado por formato, resultado igual a captura do jogo e conversão reproduzível.

| Item | Resultado | Situação |
|---|---|---|
| Contêiner AFS | Leitor C# (`dw4 afs-*`), 0 diferenças contra o inventário em PowerShell (2355 entradas) | feito |
| IGB | Cabeçalho, seções, tipos e diretório validados nos 2274 arquivos; grafo de cena, geometria e texturas indexadas exportados (`igb-export`) | feito |
| Textura | 8 bpp linear com paleta RGBA8888, linhas de baixo para cima; logotipo BANDAI e folha de status legíveis | feito |
| Modelo estático | area #598 e #3 em OBJ; importação na Godot 4.7.2 sem erro; UV conferida em captura renderizada | feito |
| Áudio | ADX → WAV com vgmstream r2117 (dois sons e o áudio da abertura) | feito |
| Vídeo | Sofdec AUS_ separado com `sfd-demux`: MPEG-1 640×480 a 29,97 fps + ADX 48 kHz estéreo 1:04,0 | feito; reprodução do .m1v não testada |
| Scripts | 5291 chunks Lua 4.0 lidos sem erro; API `be:*` com 108 métodos | feito |
| Reprodutibilidade | Duas execuções de `igb-export` (27 arquivos), `sfd-demux` e vgmstream deram SHA-256 idênticos | feito |
| Teste automatizado por formato | Ainda não há projeto de testes (`DW4.Tests`) | pendente |
| Comparação com captura do jogo | Nenhuma captura do jogo rodando no Dolphin ainda | pendente |
| Compressão | Nenhum dado comprimido encontrado nos contêineres; zlib existe no executável (uso desconhecido) | desconhecido |

Ferramentas no PC: vgmstream r2117 e Godot 4.7.2 mono em `D:\dw4-tools`, com hashes conferidos. Tudo o que foi convertido está em `D:\dw4-work\convertido` e fica fora do Git.
