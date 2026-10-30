# Arquitetura e estado da fase 1

Data: 2026-10-30. Stack: .NET 9, WPF, CommunityToolkit.Mvvm. Licença GPL-3.0.

## Ecossistema de pastas (criado na primeira execução)

```
<raiz escolhida pelo usuário>/
  hub.json           ferramentas instaladas no escopo do hub, último projeto aberto
  tools/<id>/        uma pasta por ferramenta (DSPRE, melonDS, NitroPaint, PokEditor, apicula, Tinke)
  runtimes/          reservado para JRE gerenciado (fase posterior)
  cache/<id>/<tag>/  downloads das releases, reaproveitados em reinstalação
  logs/              reservado
  projects/<nome>/
    project.json     jogo base, game code, geração, ferramentas de escopo de projeto
    base.nds         dump do usuário, nunca alterado
    workspace/       ROM extraída, fonte única da verdade (extração vem na fase 2)
    builds/          ROMs geradas; ${rom} aponta para a mais recente
    decomp/<id>/     clones das decomps da pret
```

O caminho da raiz fica em `%AppData%\PokeHub\settings.json`.

## Módulos do Core

| Pasta | Responsabilidade |
|---|---|
| `Hub/` | `HubPaths` (layout), `HubConfig` (hub.json), `HubBootstrapper` (primeira execução) |
| `Rom/` | `NdsRomHeader` (lê título e game code), `GameInfo` (game code → jogo, geração, região) |
| `Projects/` | `ProjectConfig`, `ProjectPaths`, `ProjectService` (criar, listar, abrir) |
| `Plugins/` | `PluginManifest` (modelo do JSON), `PluginCatalog`, `GitHubReleaseClient`, `PluginInstaller` |
| `Processes/` | `ProcessRunner` (comandos até o fim, com log), `ToolLauncher` (abre ferramenta com variáveis resolvidas) |

## Manifesto de plugin (plugins/*.json)

- `contexts`: em quais telas aparece (maps, events, scripts, trainers, pokemon, text, graphics, models, audio, emulator, decomp, utility)
- `generations`: filtro pelo projeto aberto. **GEN5:** adicionar 5 nos plugins que servem Gen 5
- `scope`: `hub` (tools/) ou `project` (projects/x/decomp/)
- `source.type`: `gitHubRelease` (assetPattern + executable), `gitClone`, `system` (usuário aponta o exe)
- `launch.runtime`: vazio = exe direto, `java` = `java -jar`
- Variáveis nos args: `${project}` `${workspace}` `${baseRom}` `${rom}` `${builds}` `${decomp}` `${tool}` `${hub}`

## O que a fase 1 entrega

- Primeira execução cria o ecossistema de pastas.
- Novo projeto a partir de um dump, com detecção do jogo pelo header (Gen 4).
- Tela principal por contexto, ferramentas filtradas pela geração do projeto.
- Instalar/atualizar pela última release do GitHub (zip, exe ou jar), com cache.
- Clonar decomps da pret dentro do projeto.
- Abrir ferramentas como processo filho, com log de stdout/stderr e indicador de execução.
- 11 testes no Core (header, catálogo, layout de projeto, configuração).

## Fase 2 (em andamento)

Feito em 2026-10-30:

1. Extração de `base.nds` para `workspace/` e empacotamento em `builds/` em C# puro (`Rom/NdsExtractor`, `Rom/NdsPacker`, `Rom/NdsFileSystem`). Layout igual ao do ndstool: `header.bin`, `arm9.bin` (com nitro footer), `arm7.bin`, `y9.bin`, `y7.bin`, `overlay/overlay_NNNN.bin`, `banner.bin`, `data/`. Extras do hub: `fnt.bin` (FNT original) e `nds-layout.json` (árvore ordenada e ids).
   - Árvore de `data/` inalterada: a FNT original é reaproveitada, ids idênticos aos da ROM.
   - Árvore alterada: FNT reconstruída preservando a ordem conhecida; arquivos novos entram no fim da própria pasta.
   - Secure area do arm9 copiada sem decifrar (nenhuma chave da BIOS no projeto). Fica válida enquanto os primeiros 0x800 bytes do arm9 não mudarem.
   - Novo projeto extrai automaticamente. Botões: Extrair ROM, Empacotar, Empacotar e rodar.
2. "Empacotar e rodar" abre a ROM recém-gerada no melonDS, se instalado.

Pendente:

3. JRE gerenciado em `runtimes/` para PokEditor.
4. Build das decomps: detectar WSL2/MSYS2, pedir caminho do mwcc, rodar `make` com log.
5. git automático no `workspace/` a cada salvamento.
6. Verificação de dependências declaradas em `requires` (.NET Framework 4.8, Java).

## Pontos marcados para Gen 5 (`// GEN5:` no código)

- `Rom/GameInfo.cs`: enum `BaseGame` e tabela de game codes (IRB, IRA, IRE, IRD).
- `Projects/ProjectService.cs`: mensagem de ROM não suportada.
- `App/Views/NewProjectWindow.xaml.cs`: mensagem de detecção.
- `App/ViewModels/MainViewModel.cs`: filtro de plugins por geração.
- Manifestos: `generations` de melonds, nitropaint, tinke, apicula.
- Futuro: CTRMap-CE (Java, mesmo mecanismo do PokEditor), codificação de texto Gen 5, caminhos NARC por geração.
