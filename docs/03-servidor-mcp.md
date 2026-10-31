# Servidor MCP: agentes de IA operando o hub

`src/PokeHub.Mcp` é um servidor [MCP](https://modelcontextprotocol.io) (Model Context Protocol) sobre o `PokeHub.Core`. Um agente (Claude Code, Claude Desktop ou qualquer cliente MCP) roda o executável como processo filho e conversa por stdio. Não há porta de rede. Ele usa a mesma pasta do hub e os mesmos serviços da janela principal, então tudo que o agente faz aparece no PokeHub e fica no git do workspace.

## Como ligar

1. `dotnet build src/PokeHub.Mcp`
2. Pasta do hub: a configurada pelo PokeHub (`%AppData%\PokeHub\settings.json`), ou `--root <pasta>`, ou a variável `POKEHUB_ROOT`.
3. Claude Code: o `.mcp.json` na raiz do repositório já registra o servidor `pokehub`; abrindo o Claude Code nesta pasta ele aparece em `/mcp`. Em outro cliente, aponte o comando para `PokeHub.Mcp.exe`.

Teste manual sem agente: mandar JSON-RPC por stdin, uma mensagem por linha (`initialize`, `notifications/initialized`, `tools/list`, `tools/call`).

## Ferramentas

| Ferramenta | O que faz | Proteção |
|---|---|---|
| `hub_info` | pasta raiz, idioma, projetos, ferramentas e runtimes instalados | leitura |
| `list_projects`, `project_info` | projetos e estado do workspace | leitura |
| `create_project` | cria projeto de uma ROM .nds e extrai o workspace | cria pasta nova |
| `extract_rom` | extrai a ROM base para o workspace | `overwrite=true` se já existe |
| `pack_rom` | gera a ROM em `builds/` (salva versão antes) | |
| `save_version`, `list_versions` | ponto de restauração e histórico do git interno | |
| `restore_version` | volta o workspace a uma versão | `confirm=true` |
| `list_trainers`, `get_trainer` | treinadores com classe, time, golpes e itens | leitura |
| `list_encounter_areas`, `get_encounters` | tabelas de encontros selvagens com nomes de espécie e local | leitura |
| `list_tools` | catálogo com estado de instalação, propósito e custo | leitura |
| `install_tool` | instala ou atualiza uma ferramenta | `confirm=true` para itens pesados (MSYS2, decomps) |
| `launch_tool` | abre uma ferramenta externa apontando para o projeto | |
| `test_rom` | empacota e abre no emulador | |

Regras de projeto:

- Ações destrutivas ou pesadas exigem um parâmetro de confirmação explícito; o agente precisa perguntar ao usuário antes de repetir com `confirm=true`.
- Erros voltam com a mensagem real (projeto inexistente, workspace vazio, ferramenta não instalada), para o agente corrigir a chamada em vez de adivinhar.
- Os dados do jogo são lidos pelos repositórios dos editores (`TrainerRepository`, `EncounterRepository`), então os ids e nomes batem com a interface.
- Escrita de dados do jogo (editar time, encontros, textos) ainda não está exposta: próxima fatia, com versão salva automaticamente antes de cada gravação.

## Lição registrada

Um processo filho iniciado pelo Core herdava o stdin do hub. No servidor MCP, o SDK fica lendo esse stdin, e a captura da saída do filho (`git log`) travava até o timeout. `ProcessRunner` agora redireciona e fecha o stdin de todo filho e drena o stderr, o que vale para a janela também.
