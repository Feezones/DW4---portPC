# Fase 4: itens, drops e dinheiro (levantamento)

Leitura estática do main.dol e das tabelas beDataBase do area #3 em 2026-10-24. Nada executado. FATO = lido no código ou nos dados, com endereço; HIPÓTESE = dedução ainda não confirmada.

## Tabelas (FATO)

| Tabela | Linhas | Conteúdo |
| --- | --- | --- |
| conItem | 12 | Discos de uso: HANBAI (preço), KAITORI (revenda), HP, MP, NAME, TXT, MODEL. HP α/β/γ = 450/1800/5400; MP α/β/γ = 400/1600/4800; キュア (tira veneno), レイズ (revive), ゲート (volta à lobby), リカバー (HP e MP 99999); ブースタパック e キーチェーン são de troca. |
| insItem | 26 | Efeito imediato ao pegar: UP, NAME, NOURYOKU, MODEL. 0 = BIT (dinheiro); 1–3 = Xデータ +5/+20/+100; 4–25 = plugs +1/+2 de tsuyosa, mamori, maxhp, maxmp, kashikosa, seishin, subayasa e resistências. HIPÓTESE: somam no UPCNT permanente. |
| impItem | 12 | Itens de enredo (NAME, TXT): ligam um bit da GETCOMMON.FLAGFOLDER. |
| abChip | 186 | Chips de arma e armadura (POWERPOCKET/MAINPOCKET), guardados como equipamento. |

## Inventário (FATO)

- Um por jogador (GAMERAM/GETPLAYER). DEVICEFOLDER: armas, armaduras e chips, até 36 (cmpwi 0x24 em 0x803AC294). DISKFOLDER: contagem por disco, até 9 (0x803AC2B4..2D4). Pasta cheia: o item fica no chão.
- Equipados: POWERSOCKET (3 armas), MAINSLOT (armor1), SUBSLOT (armor2), pockets conforme ISPOCKET.
- Uso de disco no painel (0x80349B0C): quantidade > 0 e slot sem recarga; chama o efeito do item, arma a recarga (1e9, unidade não confirmada), gasta 1 e toca x_250healhit00 (cura) ou x_250curehit00 (veneno).
- GAMERAM "ADDHP" (Message 9, 0x80371BCC) e "ADDMP" (Message 10, 0x80371D08): somam com limite no máximo (0x8036F220). Usados pelos Lua dos pontos de cura (57 ADDHP, 46 ADDMP).
- Botões do jogador (beNDMWMdlPlayer): 0x400 abre o painel de itens ("GENERATEITEM", depois SQ_ITEM); 0x1000 é a cura de emergência ("GENEEMGHEAL", regra não lida).
- Código no port: `src/DW4.Gameplay/Inventory.cs`.

## Drops

- FATO: a tabela enemy não tem drop. Os drops ficam nos geradores da fase (generatorN/H/V.igb): beGeneraterData (_geneEnemyId = linha da enemy, _fillModeStr "wipeout", _playerDataList) → 4 beGeneraterPlayerData (_geneCount, _geneMaxCount, _itemDataList, _itemLastDataList) → beGeneraterItemDataOne (tabela, linha, _rank, _rate). Comandos: START, STOPREVERS, RESET, PERFECTDROP.
- HIPÓTESE forte: _itemDataList = drop por inimigo morto; _itemLastDataList = drop extra ao matar todos (PERFECTDROP). Cada lista tem uma entrada por dificuldade (prefixos nZ1/hZ1/vZ1).
- Numemon da dng1, Normal (nZ1nume1), tabela:linha (_rank, _rate): armor2:24, 21 e 0 (0, 200); weapon:1, 26, 51 e 76 (1, 100); insItem:0 BIT (0, 800), (1, 600), (2, 300); insItem:1 Xデータ+5 (0, 20).
- DESCONHECIDO: o código do sorteio (_rate como peso ou chance, significado de _rank, RNG usado), a quantia de BIT por _rank e se cria um item por jogador. Sem isso o drop não é ligado no port.
- Item no mundo (beNDMWMdlItem, KIND_ITEM, 0x803AA9A0..0x803AC438): modelo com o nome flutuando; tem um dono (+0xFC) e só entra no inventário desse jogador (0x803AC1FC..208). Pelo tipo (+0xF4): 3/4/9 → DEVICEFOLDER, 5/6/8 → DISKFOLDER, 7 → FLAGFOLDER. Depois some (DROPITEM/SQ_DDMG). HIPÓTESE: pega por proximidade ("NEAR"), sem botão.

## Party online

- FATO: inventários por jogador e item com dono; EXP dividida no P2EEXPUP (quem mata e/50, os outros e/100, depois o fator NINZU).
- HIPÓTESE: drop por jogador (cada um vê e pega o seu). Confirmar no código do gerador.
