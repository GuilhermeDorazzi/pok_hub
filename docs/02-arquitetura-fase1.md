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
   - Secure area do arm9 copiada sem decifrar (nenhuma chave da BIOS no projeto). O CRC da secure area do header original é preservado enquanto os bytes 0x4000..0x7FFF não mudarem (dumps de cartucho têm os 8 primeiros bytes destruídos, então recalcular nunca bate com o original).
   - `header.bin` guarda os 0x4000 bytes iniciais e `nds-layout.json` guarda a ordem física dos arquivos, então o reempacotamento de uma árvore inalterada é **byte a byte idêntico** ao dump original até o tamanho usado (validado com SoulSilver USA em 2026-10-30; teste opcional `POKEHUB_REAL_ROM=<caminho.nds>`).
   - Novo projeto extrai automaticamente. Botões: Extrair ROM, Empacotar, Empacotar e rodar.
2. "Empacotar e rodar" abre a ROM recém-gerada no melonDS, se instalado.

3. JRE gerenciado (`Runtimes/RuntimeManager`): requisitos `java21`/`java17` nos manifestos são instalados em `runtimes/<id>` a partir da API do Adoptium (Temurin JRE, Windows x64) junto com a ferramenta. O lançador usa esse java.exe; sem ele, cai no `java` do PATH. Registrado em `hub.json` → `installedRuntimes`.

4. Build das decomps (`Decomp/UnixShell`, `Decomp/DecompBuildService`): detecta MSYS2 (pastas usuais) e distros do WSL, roda o `build.command` do manifesto (`make rom`) na pasta do clone e copia `build.output` para `builds/`. MSYS2 tem prioridade porque o clone fica no disco do Windows e o WSL é lento em `/mnt`. Correção ao levantamento: as decomps baixam o compilador sozinhas (`tools/devtools/get_metroskrew.sh` puxa o pacote metroskrew de mid-kid/metroskrew), então o usuário não fornece o mwcc. O que ele precisa é um shell Unix com as dependências do INSTALL.md.

5. Git gerenciado e versionamento do workspace (`Runtimes/RuntimeManager` + `Projects/WorkspaceGit`): ao iniciar, o hub procura git no PATH e em `runtimes/git`; se não há, oferece instalar o MinGit portátil (release do git-for-windows, 64 bits, sem busybox). O `workspace/` vira um repositório git (identidade fixa PokeHub, `* binary`) com commits automáticos: na extração, antes de empacotar e ao fechar uma ferramenta aberta pelo hub. Botões "Salvar versão" e "Versões" na barra superior; `WorkspaceGit.RestoreAsync` já existe para voltar a uma versão, sem UI ainda.

6. Checagem de dependências (`Runtimes/DependencyChecker`): `dotnet-framework-4.8`/`4.5` pelo valor Release em `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full` (528040 = 4.8, 378389 = 4.5), `vcredist-x64` por `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64`. O card mostra cada requisito com estado (em vermelho se falta) e, ao instalar ou abrir, oferece abrir a página oficial de download. Esses instaladores exigem admin, por isso o hub não os executa. Requisitos desconhecidos continuam informativos.

7. Janela "Versões" (`Views/HistoryWindow`): lista os commits do workspace e restaura o selecionado. O estado atual é commitado antes de restaurar e de novo depois, então o histórico nunca perde nada. A versão mais recente não é restaurável por ser o estado atual.

Fase 2 concluída em 2026-10-30.

## Fase 3 (em andamento)

1. **Workspace no formato ds-rom** (2026-10-30). O DSPRE 2.x abandonou o layout ndstool: usa o ds-rom (AetiasHax/ds-rom, MIT, Rust) e oferece converter pastas antigas. Para o DSPRE abrir o workspace sem conversão, o hub passou a extrair com o ds-rom (`Rom/DsRomTool`, runtime `dsrom` instalado da release do GitHub, 5 MB). Layout: `config.yaml`, `header.yaml`, `arm9/` (descomprimido, itcm/dtcm separados), `arm9_overlays/` (descomprimidos), `arm7/`, `banner/`, `files/`, `path_order.txt`. O hub grava ao lado um `pokehub-workspace.json` com SHA-1 do dump e o CRC original da área segura: o `dsrom build` zera esse CRC, e o hub o restaura quando a área segura não mudou. Validado: reconstrução byte a byte idêntica ao dump de SoulSilver (teste opcional com `POKEHUB_REAL_ROM` + `POKEHUB_DSROM`). O extrator/empacotador em C# continua como reserva (formato "ndstool legado") se o ds-rom não puder ser baixado.
2. **Hooks de lançamento** (`Plugins/LaunchHooks`, campo `launch.hook` no manifesto). `dspre-open-workspace`: antes de abrir o DSPRE, grava `openDefaultRom` em `%AppData%\DSPRE\userSettings.json` apontando para o workspace do projeto, preservando as outras chaves. O DSPRE pergunta se reabre o projeto ao iniciar (ele não aceita argumentos de linha de comando; verificado em `Program.cs`).
3. UI: escolha da pasta raiz num painel dentro da janela principal; log ocultável com "Copiar tudo".

Próximos: mais plugins (Nitro Studio 2, DeSmuME), editores próprios (treinadores, encontros, texto), publicar no GitHub.

## Pontos marcados para Gen 5 (`// GEN5:` no código)

- `Rom/GameInfo.cs`: enum `BaseGame` e tabela de game codes (IRB, IRA, IRE, IRD).
- `Projects/ProjectService.cs`: mensagem de ROM não suportada.
- `App/Views/NewProjectWindow.xaml.cs`: mensagem de detecção.
- `App/ViewModels/MainViewModel.cs`: filtro de plugins por geração.
- Manifestos: `generations` de melonds, nitropaint, tinke, apicula.
- Futuro: CTRMap-CE (Java, mesmo mecanismo do PokEditor), codificação de texto Gen 5, caminhos NARC por geração.
