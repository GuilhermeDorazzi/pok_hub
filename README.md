# PokeHub

Hub para ROM hacking de Pokémon no Nintendo DS. Instala, configura e lança as ferramentas da comunidade
(DSPRE, melonDS, NitroPaint, PokEditor, apicula, decomps da pret) todas apontando para o mesmo projeto.

- Plataforma: Windows, .NET 9, WPF
- Licença: GPL-3.0 (ver LICENSE)
- Foco atual: Geração IV (Diamond, Pearl, Platinum, HeartGold, SoulSilver). Pontos de extensão para Gen V marcados com `// GEN5:`

## Estrutura

```
src/PokeHub.Core   lógica: pastas do hub, projetos, manifestos de plugins, instalador, lançador de processos, header de ROM
src/PokeHub.App    interface WPF
src/PokeHub.Mcp    servidor MCP: agentes de IA operam o hub (ver docs/03-servidor-mcp.md)
plugins/*.json     manifestos das ferramentas suportadas
docs/              planejamento e levantamento de ferramentas
```

## Build

```
dotnet build PokeHub.sln
dotnet run --project src/PokeHub.App
```
