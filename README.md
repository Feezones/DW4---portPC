# Digimon World 4 → Godot 4 (port nativo para PC)

Reconstrução fiel do comportamento de Digimon World 4 na Godot 4, para Windows, sem emulador.

**Fase atual: 0, auditoria e descoberta.** Nenhum código de gameplay existe ainda e nenhum será escrito antes da aprovação do responsável.

Este repositório guarda documentação, ferramentas e o projeto Godot. **Não contém e nunca deve conter** o disco do jogo, executáveis originais ou recursos extraídos. Cada pessoa extrai os recursos da própria cópia.

## Convenção de confiança

Toda descoberta em `docs/` é marcada como:

- **FATO**: verificado por comando reproduzível ou fonte citada.
- **HIPÓTESE**: plausível, com evidência parcial, ainda sem teste.
- **DESCONHECIDO**: sem dados.

## Estrutura

| Caminho | Conteúdo |
|---|---|
| `docs/00-ambiente.md` | Ambiente de nuvem e do PC |
| `docs/inventario/` | Identificação do disco, hashes, lista de arquivos |
| `docs/formatos/` | Um documento por formato investigado |
| `docs/catalogo-recursos.csv` | Catálogo de recursos identificados |
| `docs/gameplay/` | Especificação de mecânicas (observação, medição, hipótese, conclusão) |
| `docs/fidelidade/` | Plano de testes comparativos |
| `docs/adr/` | Registro de decisões arquiteturais |
| `docs/riscos.md` | Riscos técnicos e jurídicos |
| `docs/roadmap.md` | Fases e critérios de aceitação |
| `docs/diario/` | Registro datado de experimentos |
| `tools/` | Leitores de formato e conversores (vazio na Fase 0) |
| `game/` | Projeto Godot (vazio até a Fase 2) |
| `tests/` | Testes de regras e de fidelidade |
