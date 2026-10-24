# Inventário do disco

## Arquivo fornecido

- Caminho (PC do responsável): `C:\Jogos\RE-DIGITAL\Digimon World 4\Digimon World 4 (USA).rvz`
- Formato do contêiner: RVZ, formato comprimido do emulador Dolphin para discos de GameCube e Wii. **FATO** (extensão; assinatura ainda não lida).
- Plataforma: provavelmente GameCube. **HIPÓTESE**, a confirmar pelo ID de 6 caracteres no cabeçalho do disco.
- Tamanho, hashes, ID do jogo, região: **DESCONHECIDO** (sem acesso ao arquivo ainda).

Referência externa: a versão PS2 norte-americana aparece como SLUS-20836 no PSXDataCenter. Não se aplica se o disco for de GameCube.

## Procedimento (somente leitura)

1. `Get-Item` e `Get-FileHash` (SHA-256, MD5) do `.rvz`; marcar como somente leitura.
2. `Format-Hex -Count 64` para confirmar a assinatura RVZ.
3. `DolphinTool header -i <arquivo>` e `DolphinTool verify -i <arquivo>`: ID, região, revisão e hashes do disco descomprimido para comparar com Redump.
4. Listagem do sistema de arquivos do disco (FST) com offset, tamanho e hash de cada arquivo, exportada para `arquivos.csv`.
5. Classificação por conteúdo (assinatura e entropia), sem confiar em extensões.
6. `Get-FileHash` final para confirmar que o original não mudou.

Nenhum executável do disco será executado.
