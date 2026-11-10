# Levantamento de ferramentas DS e licenças

Data: 2026-10-30. Foco: Gen 4 (DP, Pt, HGSS), Windows, com decomps da pret contempladas.
Fontes: páginas e API do GitHub de cada projeto. Datas de release e "último push" vêm da API.

## Como ler a coluna "Uso no hub"

- **Lançar**: o hub instala e abre a ferramenta como processo externo. Nenhuma licença open source restringe isso.
- **Linkar**: o hub referencia a biblioteca/código dentro do próprio exe. Aqui a licença importa.
- **Reutilizar**: usar dentro do hub um trecho pequeno de outro projeto (dados, tabelas, formato de arquivo), com o crédito e o aviso de licença dentro do próprio arquivo. Mesma regra de "linkar". Portar os editores de outra ferramenta para dentro do hub não é o objetivo: quem quer o editor completo abre a própria ferramenta pelo hub.

Regra prática para o hub (que pretendemos manter open source):
- MIT, BSD, 0BSD: pode linkar e reutilizar sem burocracia, só manter o aviso de copyright.
- GPL-3: pode linkar e reutilizar se o hub for GPL-3 também. Se o hub for MIT, só lançar.
- AGPL-3: igual GPL-3, com a exigência extra para uso em rede (irrelevante para app desktop), mas contamina igual. Só lançar, a menos que o hub seja AGPL/GPL.
- Sem licença: todos os direitos reservados por padrão. Só lançar. Reutilizar código exige autorização do autor (a "parceria").

## Gen 4 e ferramentas comuns

| Ferramenta | O que faz | Licença | Tecnologia | Atividade | Uso no hub |
|---|---|---|---|---|---|
| DSPRE (DS-Pokemon-Rom-Editor/DSPRE) | Mapas, eventos, scripts, treinadores, encontros, texto, Pokémon. Gen 4 completo (DP, Pt, HGSS) | **AGPL-3.0** | C# .NET Framework 4.8, WinForms | v2.3.4 em 2026-10-13, push 2026-10-29. Muito ativo | Lançar. Aceita abrir pasta já extraída ("Load extracted data from directory"), o que encaixa no modelo `workspace/`. Usa Velopack (auto-update), tem `DSPRE-win-Portable.zip` |
| PokEditor (turtleisaac) | Stats, learnsets, evoluções, moves, sprites de Pokémon. Gen 4 | **GPL-3.0** | Java 20 (jar) | v3.2.0 em 2026-08-29 | Lançar. Precisa de JRE 20+ gerenciado pelo hub |
| PokEditor-Core (turtleisaac) | Biblioteca de leitura/escrita dos dados Gen 4 | Sem LICENSE. README diz "pode usar dando crédito" | Java | push 2026-08-27 | Referência de formato. Reutilizar só com autorização formal |
| NitroPaint (Garhoogin) | Editor de gráficos DS: paletas, tiles, BG, OBJ cells, animações, fontes, texturas | **BSD-2-Clause** | C, Win32 nativo, exe único | v2.10.1 em 2026-10-07, push 2026-10-29. Muito ativo | Lançar. Código livre para reutilizar no futuro |
| Tinke (pleonex) | Explorar/extrair/reimportar arquivos da ROM, NARC, gráficos | **GPL-3.0** | C# .NET Framework 4.5, WinForms. Lib Ekona separada | v0.9.2 em 2022-11-26, repo **arquivado** | Lançar. Ekona é a base que DSPRE usa internamente |
| apicula (scurest) | Converte modelos NSBMD para glTF/COLLADA | **0BSD** | Rust, CLI, binário Windows | push 2025-01-21 | Lançar como CLI com UI própria. Livre para reutilizar |
| ndstool (devkitPro) | Extrair e empacotar ROM .nds | **GPL-3.0** | C++, CLI | v2.3.1 em 2024-11-11, sem binário no release | Lançar. **Atenção**: sem download direto, a distribuição oficial é via devkitPro pacman. Opções: instalar devkitPro, ou o hub implementar extração/empacotamento próprio em C# (formato é simples e bem documentado) |
| melonDS | Emulador | **GPL-3.0** | C++/Qt, binário Windows x64 | v1.1 em 2025-11-18 | Lançar. CLI: `melonDS.exe <rom.nds> [-f] [-b auto\|always\|never]` |
| DeSmuME | Emulador (debugger melhor) | **GPL-2.0** | C++, binário win64 | 0.9.13 em 2022-05-23 | Lançar, opcional |
| Nitro Studio 2 (Gota7) | Áudio SDAT | **Sem licença** | C# (.sln) | push 2026-04-14 | Lançar apenas |
| SDSME (khanduras/sdsme e forks) | Editor de mapas antigo Gen 4/5 | **Sem licença** ("open source" sem termos) | C# WinForms | push 2015, abandonado | Não incluir. DSPRE substitui |
| PPRE | Editor antigo Gen 4 | n/a | Python 2 | abandonado | Não incluir. DSPRE substitui |

## Decomps da pret (Gen 4)

| Repo | Status | Licença | Build no Windows | Observação |
|---|---|---|---|---|
| pret/pokeplatinum | WIP, push 2026-10-30 | **Nenhuma** | WSL2 recomendado, MSYS2 alternativa. make + meson/ninja. Compilador Metrowerks (mwcc) via wrapper `skrewrap` (pacote metroskrew, baixado pelo próprio `make`) | Correção (2026-10-30): o build baixa o metroskrew sozinho de mid-kid/metroskrew, o usuário não precisa fornecer o mwcc. O hub continua sem embutir nada: só orquestra o `make` |
| pret/pokeheartgold | WIP, push 2026-10-30 | **Nenhuma** | Igual ao pokeplatinum | Idem |
| pret/pokediamond | WIP, push 2026-10-11 | **Nenhuma** | Igual | Idem |

Implicações para o hub:
- Clonar os repos é tranquilo (git público). Reutilizar código deles não é, por falta de licença.
- O hub orquestra o build (detecta WSL2 ou MSYS2, roda `make rom`, mostra log). O compilador é baixado pelo próprio Makefile (metroskrew).
- A verificação do build depende dos hashes SHA-1 listados no README de cada repo.

## Gen 5 (referência futura)

| Ferramenta | O que faz | Licença | Tecnologia | Observação |
|---|---|---|---|---|
| CTRMap-CE (kingdom-of-ds-hacking) | Mapas e scripts BW/B2W2 | **GPL-3.0 + cláusula CFRU** (proibido lucrar com o jogo) | Java + Maven, sistema de plugins próprio | A cláusula extra torna o código não reutilizável para um hub MIT. Lançar apenas. Releases via organização, não via API padrão |
| BWTE / BWSE / BWOE (KazoWAR, Kaphotics) | Treinadores, sprites, overworld BW | Sem licença, fonte em zip no fórum Project Pokémon (2012) | C# WinForms | Lançar se ainda rodar. Candidato forte a editor próprio no hub |
| PPTXT | Texto Gen 5 | Sem repo encontrado | ? | Candidato a editor próprio |
| Tinke, NitroPaint, apicula, melonDS, Nitro Studio 2 | Comuns | ver acima | | Já cobertos |

## Veredito: o que dá para usar sem burocracia

1. **Lançar como processo externo**: tudo, sem exceção. Nenhuma licença impede isso.
2. **Reutilizar código no hub**: NitroPaint (BSD-2), apicula (0BSD). Nada mais sem decisão de licença.
3. **Decisão pendente sobre a licença do hub**:
   - Se o hub for **GPL-3**, a licença também permite reutilizar trechos do DSPRE (a seção 13 da GPL-3 permite combinar com AGPL-3), Tinke/Ekona, ndstool e melonDS. Licença permitir não basta: o hub só reutiliza o mínimo, mantém o copyright e o aviso da licença original em cada arquivo e respeita o que os autores pedem.
   - Se o hub for **MIT**, fica limitado a NitroPaint e apicula para reutilização, e tudo o mais só por lançamento.
   - Decisão tomada: **GPL-3**, como o resto do ecossistema. O DSPRE continua sendo aberto como ferramenta externa; do código dele o hub só reutiliza a tabela de caracteres, o formato de encontros de DP/Pt, a expansão do ARM9 e caminhos e offsets do RomInfo, cada um com o aviso AGPL-3.0 no arquivo (ver `THIRD_PARTY.md`).
4. **Exige parceria/autorização**: PokEditor-Core, Nitro Studio 2, SDSME, ferramentas Gen 5 antigas, decomps da pret.
5. **Nunca distribuir**: ROMs. O compilador mwcc também não entra no repositório do hub; quem o baixa é o Makefile da decomp.

## Primeiro conjunto de plugins (fase 1)

- DSPRE (portable zip, release GitHub)
- melonDS (zip, release GitHub)
- NitroPaint (exe, release GitHub)
- PokEditor (jar, release GitHub) + JRE gerenciado
- apicula (zip, build automático do master)
- Extração/empacotamento de ROM: implementar em C# no hub (evita a dependência de devkitPro)
- Decomp: clone git de pokeplatinum/pokeheartgold + detecção de WSL2/MSYS2 + `make rom`

## Pontos a marcar no código para Gen 5

Lista a ser mantida conforme o código for escrito. Marcar com `// GEN5:` nos fontes.

- Enum de jogo base e detecção pelo header da ROM (game code): hoje DP/Pt/HGSS, adicionar BW/B2W2.
- Mapeamento de caminhos NARC por jogo (treinadores, encontros, texto, mapas): tabela por geração.
- Codificação de texto: Gen 4 usa tabela própria, Gen 5 usa formato diferente (PPTXT).
- Manifestos de plugins com campo `generations: [4]` ou `[5]`, filtrando o que aparece em cada contexto.
- Decomp: Gen 5 não tem decomp madura, o contexto deve ficar oculto.
- CTRMap-CE como plugin Java, mesmo mecanismo do PokEditor (JRE gerenciado).
