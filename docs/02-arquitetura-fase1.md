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
4. **Editor de treinadores embutido** (`Core/Gen4/*`, `App/Views/TrainerEditorWindow`, manifesto `trainer-editor.json` com `source.type = builtin`). Primeiro editor próprio do hub:
   - `Narc`: leitura e escrita de NARC (BTNF preservado, arquivos alinhados a 4 bytes com 0xFF).
   - `Gen4TextArchive` + `CharMap`: bancos de texto de Gen 4 (cifra da tabela de offsets e das strings, compressão de 9 bits dos nomes de treinador) com o `charmap.json` do DSPRE embutido. Mensagens não alteradas são regravadas com as palavras originais, porque o encoder da Nintendo deixa bits de preenchimento diferentes.
   - `Gen4Game`: caminhos dos NARCs e números dos bancos de texto por jogo e idioma (DP, Pt, HGSS; fonte: RomInfo do DSPRE).
   - `Trainer`/`PartyMember`: registro de 20 bytes do trdata e entradas do trpoke (6/8/14/16 bytes em DP, +2 de ball seal em Pt/HGSS), com os bytes originais preservados quando o treinador não muda.
   - `TrainerRepository`: carrega treinadores e as listas de nomes de classes, espécies, itens e golpes do próprio jogo; grava trdata, trpoke e o banco de nomes.
   - A janela lista os treinadores com filtro, edita nome, classe, IA, tipo de batalha, itens e o time (espécie, forma, nível, dificuldade/IVs, item, golpes, ball seal, flags de gênero e habilidade). Salvar grava no workspace e cria uma versão no git.
   - Validado com SoulSilver: carregar e regravar sem mudanças produz NARCs idênticos (teste opcional `POKEHUB_WORKSPACE=<workspace>`).
   - Fora do escopo por enquanto: adicionar treinadores novos (exige expandir tabelas no arm9), nomes de classe, sprites.

5. **MSYS2 instalado pelo hub** (runtime `msys2`, manifesto `msys2.json` com `source.type = runtime`). Baixa o pacote auto-extraível `msys2-base-x86_64-latest.sfx.exe` (40 MB) da release do msys2-installer, extrai em `runtimes/msys2/msys64` com `-y -o` (sem instalador, sem administrador) e prepara a toolchain como o CI do MSYS2 faz: login shell inicial, `pacman --noconfirm -Syuu` duas vezes e `pacman -S --needed` com os pacotes do INSTALL.md das decomps (lista em `setup.pacman` do manifesto). O MSYS2 do hub tem prioridade na detecção. O card aparece na aba Decomp; "Instalar" faz tudo num clique e, já instalado, "Preparar toolchain de novo" só refaz a preparação.
   - Lições do build real do pokeplatinum pelo hub (2026-10-30): (a) o `/etc/profile` só roda os scripts de primeira execução (chaveiro do pacman etc.) com `MAYBE_FIRST_START=true`, que o hub passa na primeira chamada; (b) o profile redefine PATH e PKG_CONFIG_PATH, então eles são exportados dentro do comando; (c) o INSTALL.md esquece o `pkg-config` (pacote `pkgconf`) na lista do MSYS2; (d) as ferramentas nativas da decomp usam APIs POSIX e só compilam com o gcc do msys (ambiente `MSYS`, não `UCRT64`); (e) a libpng do mingw64 sugerida pelo INSTALL.md derruba o `nitrogfx` com segmentation fault, porque ele passa `FILE*` do runtime msys para uma libpng de outro runtime. Como o repositório msys não tem libpng, o hub compila uma libpng estática (tarball oficial, `--disable-shared`) em `/usr/local` e aponta `PKG_CONFIG_PATH` e `PKG_CONFIG_PATH_FOR_BUILD` para lá. (f) a libpng estática precisa de `-lz` em `Libs` do `.pc` (não só em `Libs.private`), senão o link do nitrogfx falha. **Validado em 2026-10-30: `make rom` do pokeplatinum completo pelo MSYS2 do hub, 16.680 passos em 48,6 min nesta máquina, ROM de 128 MB copiada para `builds/`.** O primeiro build é lento por reconstruir o jogo inteiro; os seguintes são incrementais (teste opcional `POKEHUB_MSYS2_HUB_ROOT` + `POKEHUB_DECOMP_BUILD=pokeplatinum`).
6. **Editor de áreas da Pokédex** (`Core/Gen4/PokedexAreas.cs`, `App/Views/PokedexAreaEditorWindow`, manifesto `pokedex-area-editor.json`). Port nativo do pk_dex_editor do autor: NARC `a/1/3/3` do HGSS, um arquivo por espécie e bloco (normal/especial × manhã/dia/noite/qualquer hora), lista de ids u32 terminada em 0; offsets e tabelas de áreas vindos da ferramenta original. Nomes de espécie lidos do jogo. Arquivos não alterados são regravados byte a byte. Só HGSS (DP/Pt usam outro formato; GEN5 idem).

Próximos: mais plugins (Nitro Studio 2, DeSmuME), editores de encontros e texto, publicar no GitHub.

## Pontos marcados para Gen 5 (`// GEN5:` no código)

- `Rom/GameInfo.cs`: enum `BaseGame` e tabela de game codes (IRB, IRA, IRE, IRD).
- `Projects/ProjectService.cs`: mensagem de ROM não suportada.
- `App/Views/NewProjectWindow.xaml.cs`: mensagem de detecção.
- `App/ViewModels/MainViewModel.cs`: filtro de plugins por geração.
- Manifestos: `generations` de melonds, nitropaint, tinke, apicula.
- Futuro: CTRMap-CE (Java, mesmo mecanismo do PokEditor), codificação de texto Gen 5, caminhos NARC por geração.
