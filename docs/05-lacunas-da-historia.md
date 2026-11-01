# Lacunas da história e do jogo em SoulSilver

Data: 2026-10-31. Complementa `docs/04-roteiro-do-hack.md`, sem alterá-lo. Nada aqui foi testado em jogo.

Este documento responde a três perguntas: o que o jogo deixa em aberto hoje, o que já existe na ROM e pode ser aproveitado, e como isso muda a ordem do roteiro.

**Como ler as fontes:**

- **Medido:** lido dos arquivos do projeto `Pokemon SS`, sem alterar nada.
- **Decomp:** lido do código de `pret/pokeheartgold`. Mostra o que o jogo original faz, mas não foi testado no projeto.
- **Comunidade:** guias e relatos de jogadores, listados no fim. As opiniões variam.
- **Proposta:** sugestão minha, a decidir.

## Resumo

1. **A maior descoberta é o evento do Giovanni.** A história do pai de Silver está inteira na ROM, mas fica trancada atrás de um Celebi de distribuição. Dá para destrancá-la editando scripts, sem injeção de código.
2. **A curva de níveis é a queixa mais repetida, e os números confirmam.** Em Kanto, os líderes têm nível 44 a 60 e as primeiras rotas têm selvagens de nível 2 a 10.
3. **Kanto tem lugares, mas quase não tem trama.** Depois da Rádio, a Rocket se desfaz, e os executivos não têm continuação.
4. **Os 122 Pokémon que faltam cabem em 10 grupos**, e cada grupo tem uma solução. A lista da comunidade soma 122, perto do teto de 121 que a varredura do hub achou. As duas não foram comparadas nome a nome.
5. **Já existem times de rebatalha na ROM.** Isso pode reduzir a necessidade de criar treinadores novos.

## 1. A história do Giovanni está pronta e trancada

**Como é o evento (comunidade e medido):** levando o Celebi ao santuário de Ilex, o jogador volta três anos no tempo. Em Route 22, vê Silver discutindo com o pai. Depois, em Tohjo Falls, encontra Giovanni com um rádio que transmite o chamado da Rocket, e os dois batalham. É a única cena do jogo que mostra Silver e Giovanni juntos.

Os mapas e o treinador estão no projeto:

| O quê | Onde |
|---|---|
| Santuário e início do evento | `D36R0101`, script 13 (arquivo de scripts 92) |
| Cena de Silver e Giovanni | `R22`, variável `0x40FE` = 1 |
| Batalha com Giovanni | `D45R0102`, variável `0x40FE` = 2 |
| Giovanni | Treinador 700: Nidoking 42, Kangaskhan 40, Honchkrow 43, Nidoqueen 46 |

**O que tranca o evento:**

- **Medido:** o santuário confere `FLAG_BEAT_RADIO_TOWER_ROCKETS` e depois chama `FollowerPokeIsEventTrigger` com o tipo 3 sobre o primeiro Pokémon do time.
- **Decomp:** esse comando só aceita um Pokémon com a marca de encontro especial. Com a marca e a espécie Celebi, o evento segue.
- **Decomp:** `GiveMon` não recebe nenhum argumento para essa marca. Argumentos: espécie, nível, item, forma, habilidade e resultado.

**Consequência:** um Celebi entregue por NPC provavelmente não dispara o evento. Ainda falta testar se `GiveMon` deixa a marca desligada.

**O Celebi do evento não está na ROM:**

- **Decomp:** o Pokémon de um presente vem dos dados do Presente Misterioso guardados no save (`mgData->mon` em `scrcmd_mystery_gift.c`). Ele chega com a distribuição, não com o jogo.
- **Medido:** nos scripts alcançados pela varredura, os usos literais de 251 que conferi são item de loja de fichas, salto ou movimento, nenhum entrega um Celebi.
- **Decomp:** a marca só é gravada por `BoxMonSetTrainerMemo` no modo 4, que `ScrCmd_GiveMon` não oferece como opção.

**Alternativa com injeção (não testada):** um trecho de código que grave a marca depois de `GiveMon`. Pede ds-rom, que o projeto oficial terá. Só compensa se os míticos desobedecerem sem a marca, porque resolveria Celebi, Arceus e obediência de uma vez. Sem isso, a edição do script abaixo é mais simples.

**Caminho proposto, sem injeção:** trocar no script do santuário a chamada de `FollowerPokeIsEventTrigger` por uma destas condições:

- Conferir só a espécie, com `GetPartyMonSpecies`. Mantém o Celebi como chave.
- Conferir a flag de uma missão. Assim o evento vira capítulo de uma cadeia e não exige Celebi nenhum.

O editor de scripts do hub já insere e remove comandos e recalcula os saltos. O time de Giovanni deve subir de nível, já que o evento passaria a ser jogado mais tarde: hoje ele é mais fraco que os líderes de Kanto.

## 2. Outros eventos com a mesma trava

O mesmo comando aparece em outros scripts:

| Tipo (ordem presumida pelo código) | Exigência na decomp | Arquivos de scripts |
|---|---|---|
| 0, Pichu de orelha espigada | Marca especial, e Pichu, Pikachu ou Raichu shiny | 92 |
| 1, Arceus do Hall of Origin | Arceus encontrado no Hall of Origin de DP/Pt | 37, 42, 53, 55, 131 |
| 2, Arceus do filme | Marca especial e espécie Arceus | 37, 42, 53, 55, 131 |
| 3, Celebi | Marca especial e espécie Celebi | 92 |

Isso responde em parte a uma hipótese do roteiro, "se os eventos exigem marca de encontro especial":

- **Celebi, Pichu e Arceus:** sim, segundo a decomp. A varredura não os achava por espécie porque a espécie fica dentro desse comando, não no script. Por isso o roteiro dizia "Celebi sem checagem achada", e isso precisa ser corrigido.
- **Latias e Latios (Enigma Stone):** a marca não apareceu nessa checagem. Segue para o teste da Etapa 1B.
- **Obediência:** é outra pergunta. Relatos da comunidade dizem que alguns míticos só obedecem com a marca. Segue como hipótese.

Em SoulSilver, a Latias consta como exclusiva do HeartGold. O teste da Enigma Stone deve render o Latios, e a Latias precisa de outro caminho.

## 3. Curva de níveis

Selvagens de grama, medidos no projeto. Líderes e chefes também são medidos. O Falkner com Pidgey 30 é uma edição já feita no `Pokemon SS`, então ficou de fora.

**Johto:**

| Fase | Selvagens por perto | Chefe |
|---|---|---|
| Início | Rotas 29 a 32: 2 a 6 | Bugsy: 15 a 17 |
| Meio | Rotas 34 a 37: 10 a 15 | Whitney: 17 a 19 |
| | Rotas 38 e 39: 13 a 17 | Morty: 21 a 25 |
| Virada | Rotas 42 e 43: 13 a 17 | Chuck 29 a 31, Jasmine 30 a 35, Pryce 30 a 34 |
| Fim | Rotas 44 e 45: 20 a 27 | Clair: 38 a 41 |
| Elite | Victory Road: 32 a 36 | Elite 40 a 47, Lance 46 a 50 |

Rota 46 e parte de Dark Cave têm nível 2 a 4, e Ilex tem 5 a 6. Fazem sentido só no começo do jogo, mas continuam iguais depois.

**Kanto:**

| Área | Selvagens | Comparação |
|---|---|---|
| Rotas 1, 2, 22 | 2 a 10 | Blue: 52 a 60 |
| Rotas 3, 4 | 5 a 10 | Brock: 51 a 54 |
| Rotas 24, 25 | 8 a 14 | Misty: 49 a 54 |
| Rotas 5 a 11 | 12 a 19 | Surge 47 a 53, Erika 51 a 56, Sabrina 53 a 55 |
| Rotas 13 a 18 | 22 a 32 | Janine: 44 a 50 |
| Rota 21 | 20 a 35 | Blaine: 54 a 59 |
| Rotas 26, 27 | 28 a 32 | Volta à Liga |
| Rota 28, fora do Mt. Silver | 39 a 44 | |
| Cerulean Cave | 37 a 49 | |
| Mt. Silver Cave | até 51 | Red: 80 a 88 |

**O que os números mostram:**

- **Virada em Johto:** os chefes ganham cerca de 10 níveis enquanto os selvagens ficam parados.
- **Kanto:** selvagens até 45 níveis abaixo do líder da mesma cidade. Capturar ali não ajuda o time.
- **Red:** cerca de 30 níveis acima do selvagem mais forte, sem lugar natural para treinar.
- **Treinadores de rota em Kanto:** amostras em Rotas 3 e 9 ficam em geral entre 37 e 47. Parte de uma amostra saiu baixa, e a ligação entre script e treinador não foi validada. Trate como indício.

Isso reforça a visão do usuário: a tarefa pós-Elite em Kanto precisa de selvagens à altura para ter graça.

**O que falta no hub:** `EncounterBatchOptions` escala o nível em todas as tabelas de uma vez. Para corrigir a curva, o lote precisa de filtro por área, como já há filtro por classe nos treinadores.

## 4. Lacunas de trama

| Lacuna | Situação hoje | Fonte | Aproveitamento proposto |
|---|---|---|---|
| Rocket some depois da Rádio | Archer desfaz a equipe. O treinador Rocket mais forte fora o Giovanni tem nível 39 | Comunidade e medido | Remanescentes em Kanto, com NPCs da Etapa 2 |
| Executivos sem continuação | Archer vai a 38, Ariana a 32, Petrel a 32, Proton a 33. Nenhum volta depois | Medido | Rebatalhas pós-Elite, com times novos |
| Giovanni nunca aparece sem evento | Rádio chama por ele, mas ele não responde | Comunidade | Seção 1 |
| Silver termina em rebatalhas | Depois da Elite, só rebatalha na Liga. Times vão a 60 | Comunidade e medido | Ligar as rebatalhas à cena de Route 22 |
| Depois do Red, nada | Ele pode ser enfrentado de novo, sem história depois | Comunidade | Última etapa da cadeia pós-Elite |
| Kanto só tem pequenas tarefas | Machine Part, boneca da Copycat, Snorlax. Pouca coisa pede volta | Comunidade | Boas referências de formato para missões |
| Lendários de outras regiões | 13 lendários e 9 míticos não existem no SoulSilver sem troca ou evento | Comunidade | Encontros em mapas de Kanto, a partir das missões |

### Arco pós-Elite proposto (sem áreas novas)

1. **Rastros da Rocket.** Depois da Elite, Grunts aparecem em Kanto, e NPCs dão pistas. Usa `FLAG_GAME_CLEAR`, e Etapas 1 e 2.
2. **Os executivos.** Quatro batalhas escondidas, sem chamar o jogador. Cada vitória libera a próxima, por flag de missão.
3. **O santuário.** A última pista leva a Ilex. Destranca a cena de Route 22 e a batalha em Tohjo Falls, com Giovanni mais forte.
4. **Silver.** Depois da cena, uma fala nova dele e uma rebatalha mais difícil.
5. **Os lendários.** Com a cadeia completa, as missões de Kanto soltam os encontros.
6. **Mt. Silver.** Red continua sendo o último desafio.

Falas, locais e times ficam para o usuário definir. É uma sugestão de esqueleto.

## 5. O que falta capturar, por grupo

Contagem da comunidade para SoulSilver sozinho, sem troca nem Pokéwalker. Soma 122. As soluções são proposta.

| Grupo | Qtd. | Quais | Solução |
|---|---|---|---|
| Iniciais de Johto | 6 | Os dois não escolhidos e evoluções | Vendedor, ou missão pós-Elite |
| Iniciais de Kanto | 6 | Idem | Idem |
| Iniciais de Hoenn | 6 | Idem | Idem |
| Evolução por troca | 16 | Alakazam, Machamp, Golem, Gengar, Politoed, Slowking, Scizor, Kingdra, Porygon2, Dusknoir, Huntail, Gorebyss, Rhyperior, Electivire, Magmortar, Porygon-Z | Etapa 5 |
| Evolução por local | 4 | Magnezone, Probopass, Leafeon, Glaceon | Mudar a condição nas Tabelas do jogo. Sem local especial, nem pagamento resolve |
| Exclusivos do HeartGold | 21 | Growlithe, Arcanine, Mankey, Primeape, Omanyte, Omastar, Spinarak, Ariados, Phanpy, Donphan, Mantyke, Mantine, Gligar, Gliscor, Sableye, Baltoy, Claydol, Anorith, Armaldo, Latias, Kyogre | Encontros por fase. Fósseis, Latias e Kyogre por evento |
| Pokéwalker | 21 | Skitty, Delcatty, Carvanha, Sharpedo, Wailmer, Wailord, Feebas, Milotic, Castform, Kecleon, Tropius, Snorunt, Glalie, Froslass, Shellos, Gastrodon, Spiritomb, Finneon, Lumineon, Snover, Abomasnow | Encontros por fase |
| Sinnoh | 20 | Turtwig, Grotle, Torterra, Chimchar, Monferno, Infernape, Piplup, Prinplup, Empoleon, Cranidos, Rampardos, Shieldon, Bastiodon, Drifloon, Drifblim, Glameow, Purugly, Stunky, Skuntank, Rotom | Encontros, vendedores e missões |
| Lendários | 13 | Regirock, Regice, Registeel, Rayquaza, Uxie, Mesprit, Azelf, Dialga, Palkia, Heatran, Regigigas, Giratina, Cresselia | Pokémon no mapa, pós-Elite |
| Míticos e eventos | 9 | Mew, Celebi, Jirachi, Deoxys, Phione, Manaphy, Darkrai, Shaymin, Arceus | Recompensas de missão |

O hack Sacred Gold / Storm Silver resolveu o mesmo problema assim: as 493 espécies ficam disponíveis, quase todas antes da Elite, e as evoluções por troca viram itens. Isso mostra que o plano B da Etapa 5 é uma solução aceita na comunidade.

Só com os grupos de iniciais, evolução por local e evolução por troca, 38 espécies dependem pouco da distribuição de encontros. Elas podem ser resolvidas primeiro.

## 6. Rebatalhas já existem nos dados

**Medido:** a lista de treinadores tem times repetidos e mais fortes para o mesmo personagem:

| Quem | Original | Rebatalha |
|---|---|---|
| Líderes de Johto | Até 41 | 712 a 719, até 62 |
| Líderes de Kanto | Até 60 | 720 a 727, até 72 |
| Elite | Até 47 | 702 a 705, até 64 |
| Lance | 244, até 50 | 701, até 75 |
| Silver | Até 50 | 489 a 491, até 60 |
| Alfred (exemplo de treinador comum) | 401, nível 22 | 672 a 674, até 58 |

**Comunidade:** treinadores comuns só rebatalham por telefone, depois de dar o número. O telefonema pode voltar a ocorrer a cada dia. Líderes vão ao Fighting Dojo, depois de ligar para eles em horário certo.

**Consequência para a Etapa 2:** uma rebatalha sem telefone pode apontar para esses times que já existem, sem criar treinador. Atende "é preciso procurá-los", e deixa a pergunta sobre criar treinadores para depois.

**Não resolvido:** tentei listar os registros sem uso em script e cheguei a 268 candidatos, mas a lista tem falsos positivos conhecidos. Silver e os times de telefone aparecem nela porque são chamados por variável ou por tabela no código. Não dá para dizer que algum está livre.

## 7. Mudanças propostas no roteiro

Nenhuma foi aplicada em `docs/04-roteiro-do-hack.md`.

1. **Nova etapa: curva de níveis por área.** Filtro por área no lote de encontros, e depois ajuste de Johto e Kanto. Esforço pequeno, sem dependência e sem `arm9`. Pode ser a primeira coisa a chegar ao jogo.
2. **Etapa 1B: acrescentar o destrancar do Giovanni**, logo depois de Latias e Latios. Usa o editor de scripts atual, e o arco pós-Elite depende disso.
3. **Etapa 1B: corrigir o texto sobre o Celebi** e a hipótese da marca especial, conforme a seção 2.
4. **Etapa 1: argumentos de `GiveMon`** agora estão na decomp: espécie, nível, item, forma, habilidade, resultado. Falta só o teste.
5. **Etapa 2: rebatalha com os times existentes** primeiro. Criar treinadores vai para depois.
6. **Etapa 4: usar os 10 grupos** da seção 5 como lista de partida, conferida com a varredura do hub.
7. **Etapa 5: separar as 4 evoluções por local.** Elas se resolvem nas Tabelas do jogo, sem depender da pesquisa de injeção.

**Ordem sugerida:** curva de níveis, IA em lote, missões, destrancar do Giovanni, treinadores, capturas, evolução paga. A IA e a curva vêm antes porque dão o desafio pedido sem depender de nada novo.

## 8. Decisões para o usuário

Respondidas em 01/11/2026, salvo a curva de Kanto, que ficou para depois. O registro está na seção 5 de `04-roteiro-do-hack.md`. As recomendações abaixo ficam como contexto.

1. **Evento do Giovanni:** a chave segue sendo um Celebi, ou vira uma flag de missão? Recomendo as duas coisas: uma missão dá o Celebi, e o santuário confere só a espécie. As falas da cena citam o Celebi, e a cena não foi testada com outro Pokémon.
2. **Curva de Kanto:** subir tudo para perto dos líderes, ou deixar rotas mais fracas com um caminho de dificuldade crescente? Recomendo um piso perto de 40 a 45, subindo até cerca de 60 nas áreas finais. Espécies básicas nesses níveis precisam ser trocadas por evoluídas, porque o lote não faz isso.
3. **Selvagens baixos em Johto:** recomendo manter Rota 46 e a entrada de Dark Cave, que são visitadas cedo, subir um pouco Ilex (5 a 6, depois de Bugsy, com 15 a 17) e tratar a virada das Rotas 38 a 43, que é o problema maior.
4. **Arco pós-Elite da seção 4:** serve como esqueleto?
5. **Iniciais:** todos disponíveis, e por qual caminho?

## Fontes

- [pret/pokeheartgold, `scrcmd_c.c`](https://github.com/pret/pokeheartgold/blob/master/src/scrcmd_c.c), [`scrcmd_party.c`](https://github.com/pret/pokeheartgold/blob/master/src/scrcmd_party.c) e [`trainer_memo.c`](https://github.com/pret/pokeheartgold/blob/master/src/trainer_memo.c)
- [Serebii: evento do Celebi](https://serebii.net/heartgoldsoulsilver/celebi.shtml)
- [Serebii: espécies não obteníveis](https://www.serebii.net/heartgoldsoulsilver/unobtainable.shtml)
- [Altissimo: Pokédex só com SoulSilver](https://altissimo1.github.io/Main-Series/HGSS/Pokedex/soulsilver-only.html)
- [Dittobase: exclusivos de versão](https://www.dittobase.com/heartgold-soulsilver/version-exclusives)
- [Mirage Island: atividades pós-jogo](https://mirage-island.neocities.org/hgss-activities)
- [GameBrew: Sacred Gold e Storm Silver](https://gamebrew.org/wiki/Pokemon_Sacred_Gold_and_Storm_Silver)
- [Serebii: Equipe Rocket](https://serebii.net/heartgoldsoulsilver/teamrocket.shtml)
- [Bulbapedia: Archer](https://bulbapedia.bulbagarden.net/wiki/Archer)
- [Bulbapedia: Machine Part e Power Plant](https://bulbapedia.bulbagarden.net/wiki/Appendix:HeartGold_and_SoulSilver_walkthrough/Section_20)
- [Medium: Johto, Flawed Nostalgia](https://medium.com/super-jump/johto-flawed-nostalgia-badc58b330ec)
- [Redbrick: retrospectiva de HG/SS](https://www.redbrick.me/retrospective-pokemon-heartgold-and-soulsilver/)
- [Nota sobre a curva de nível em HeartGold](https://publish.obsidian.md/myaiba/Z0+Zettels/The+Level+Curve+in+Heart+Gold+is+Bad)
