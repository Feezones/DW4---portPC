# ADR 0001: Godot 4 com C#/.NET e regras de jogo separadas da engine

Status: **proposta**, aguardando aprovação. Versão exata da Godot e do .NET SDK será fixada depois de auditar o PC.

## Contexto

Port nativo para Windows, fiel ao original, com possível servidor autoritativo no futuro. Precisamos de ferramentas para ler formatos binários e de regras testáveis sem renderização.

## Decisão proposta

- `DW4.Formats`: biblioteca .NET pura com leitores de formato.
- `DW4.Tools`: CLI de inventário e conversão (glTF, PNG, OGG, JSON).
- `DW4.Gameplay`: regras em passo fixo, sem tipos da Godot.
- `DW4.Client`: projeto Godot (apresentação, entrada, câmera, áudio, UI).
- `DW4.Tests`: testes de regras e de fidelidade.

## Alternativas

| Opção | Vantagens | Desvantagens |
|---|---|---|
| GDScript puro | Integração simples com a engine | Regras acopladas à Godot; servidor futuro mais difícil; parsers binários lentos |
| C# (proposta) | Regras reutilizáveis num servidor .NET; parsers rápidos; testes com ferramentas padrão | Build .NET da Godot não exporta para web (irrelevante para Windows); depende de versão compatível do SDK |
| C++ / GDExtension | Desempenho máximo | Custo de build e manutenção alto sem necessidade demonstrada |

## Fora de escopo agora

Rede, contas, persistência, instâncias. Nada do comportamento original muda para facilitar multiplayer.
