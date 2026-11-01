# Roteiro do hack de SoulSilver

Data: 2026-10-31. Projeto: `Pokemon SS` (`Documents\Projeto de Teste\projects`), workspace no formato ndstool (o antigo).

Este documento lista o que falta construir no PokeHub para chegar ao jogo que o usuário quer, e em que ordem. Nada aqui foi implementado ainda, salvo o que está em "O que já dá para fazer hoje". A seção 5 separa o que foi confirmado do que é hipótese. As etapas devem ser lidas junto com ela.

Termos usados:

- **Flag:** marca de sim ou não guardada no save. É como o jogo lembra que algo já aconteceu.
- **Novo evento:** tela do hub que cria um NPC completo a partir de um modelo (`Gen4/Scripts/NewEventService`).
- **Injeção de código:** colocar código novo dentro da ROM (`Core/CodeInjection`). É o único jeito de fazer o que os comandos de script não fazem.
- **ds-rom e ndstool:** os dois formatos de workspace do hub.

Esforço, em tamanho relativo: **pequeno** (estende algo que já existe), **médio** (modelo ou tela nova, sobre comandos conhecidos), **grande** (parte desconhecida ou mexe no código do jogo).

## 0. Situação em 01/11/2026

| Etapa | Situação | Conferido no jogo? |
|---|---|---|
| 1. Missão, condições, vender Pokémon | Pronta | Sim: missões encadeadas, 3 Potions, dinheiro, trava da Elite e venda com Sim e Não, em cópia do `Pokemon SS` |
| 1B. Eventos especiais | Enigma Stone liberada por Missão. Missão também pode dar o Pokémon em si, mas isso não abre os eventos de Celebi, Arceus e Pichu de orelha espigada | Só por teste de script; a sala de Latias e Latios não foi visitada no jogo |
| 2. Treinador | Pronta, com "Duplicar como novo" no editor | Parcial: visão e batalha sim. Revanche, derrota do jogador, sumir e a fala de desafio corrigida, não |
| 3. IA em lote | Pronta | Tela conferida; batalha com IA competitiva, não |
| 4. O que falta capturar | Pronta: 65 espécies faltando na base do SoulSilver | Tela conferida; a lista fixa continua não confirmada |
| Lote por área e por evolução | Pronto | Sim, em ds-rom: selvagens evoluídos na Rota 29 e método por pedra |
| Depois da Elite: liberar treinadores, × nível, santuário de Ilex | Pronto, card próprio | Sim, em ds-rom, com o fim da Elite simulado por um NPC: treinadores voltam, níveis × 1,3 e evento do Giovanni com Celebi de Missão. Falta o comando real no Hall da Fama |
| Enigma Stone por Missão, revanche, sumir, fala de desafio e derrota do jogador | Prontos | Sim, em ds-rom, testados pelo usuário em 01/11/2026 |
| 5. Evolução paga | Pesquisada, sem código; parada por decisão do usuário | Não |
| 6. Cara nova | Pesquisada, sem código | Não |

**Enigma Stone.** A leitura anterior estava errada: `ScrCmd_546` modo 0 não quer dizer "ainda não leu". Ele compara a variável `0x4043` com `0x06F2`, e quem grava esse valor é o Presente Misterioso ao entregar a pedra. Uma Missão com a Enigma Stone como recompensa grava os dois.

**Celebi, Arceus e Pichu de orelha espigada.** O jogo só abre esses eventos se o Pokémon que segue o jogador tiver os dados de um Pokémon de evento (`ScrCmd_FollowerPokeIsEventTrigger`, `MonMetadataMatchesEvent`). O `GiveMon` não aceita esses dados, então um Celebi dado por Missão serve para a Pokédex, mas não abre o evento. Para abrir, seria preciso editar os scripts desses eventos, o que ainda não foi testado. Detalhes em `05-lacunas-da-historia.md`.

**Evolução paga: um caminho sem injeção.** O menu de item evolui quando o item tem o atributo de evolução e a espécie tem método 7 com esse item (`GetMonEvolution`, `EVOCTX_ITEM_USE`). A tabela de evoluções do hub já muda o método e funciona no `Pokemon SS`. Então "pague e evolua" pode ser: trocar o método das 17 evoluções por pedra e vender a pedra por um preço alto. Mantém apelido, IVs e treinador, e não exige converter para ds-rom. A diferença é que o jogador usa o item em vez de entregar o Pokémon. A troca de método não chegou a ser vista no jogo: o teste parou antes. Uma pedra nova exigiria editar o atributo de evolução, que a tabela de itens ainda não mostra; com as pedras existentes, funciona já pelo código lido.

**Evolução por injeção:** continua possível (`sub_02075A7C` abre a tela de evolução), mas exige ds-rom e um comando novo.

**Cara nova.** A tela de título usa modelos 3D e animações do NARC `demo/title/titledemo`, mais fundos 2D (`src/title_screen.c`). Trocar a arte dos fundos e as texturas é o caminho sem reimportação 3D; o logo precisa de teste próprio.

Fontes da comunidade: [exclusivos de HG/SS](https://www.serebii.net/heartgoldsoulsilver/exclusives.shtml) e [rotas do Pokéwalker](https://www.serebii.net/heartgoldsoulsilver/pokewalker-area.shtml), ambos do Serebii. A Bulbapedia recusou o acesso automático.

## 1. Visão do jogo

O que o usuário quer que o jogo seja:

- Um desafio completo, com líderes mais difíceis e IA competitiva, com NPCs usando cura.
- Missões secundárias: buscar item, capturar Pokémon para ganhar item, trocas em cadeia.
- Locais que recebem Pokémon de evolução por troca e devolvem evoluído por um valor alto.
- Pessoas vendendo Pokémon.
- Rebatalhar treinadores vencidos. Nem todo treinador chama: é preciso procurá-los.
- Pokémon fortes bloqueando caminhos no início, como um Snorlax acima do nível do jogador, para voltar depois.
- Continuar jogando depois da Elite, com outras histórias.
- Uma tarefa pós-Elite em Kanto que libera eventos especiais.
- Capturar todas as espécies.
- Mais encontros com a Equipe Rocket, mesmo como NPCs.
- Cara nova.

Áreas novas não agora.

## 2. O que já dá para fazer hoje

Sem desenvolver nada, só com o hub atual:

- [ ] **Pokémon bloqueando caminho:** Novo evento, modelo Pokémon no mapa, com nível alto e a opção de sumir depois da batalha. O nível só desencoraja: o jogador pode tentar a batalha a qualquer momento.
- [ ] **Líderes mais difíceis:** editor de Treinadores, com time, 4 itens e 11 opções de IA.
- [ ] **Trocas em cadeia:** Novo evento, modelo Trocar Pokémon, com 118 vagas livres no SoulSilver. Cada NPC pede o que o anterior deu. A cadeia existe só pela espécie pedida: ainda não dá para exigir que a troca anterior tenha acontecido.
- [ ] **NPC que dá item uma vez:** Novo evento, modelo Dar item.
- [ ] **Equipe Rocket como figurante:** Novo evento, modelo Falar, com sprite de Rocket. Sem batalha.
- [ ] **Espécies nos encontros selvagens:** editor de Encontros e Operações em lote.
- [ ] **Stats, golpes e evoluções:** editor de stats e Tabelas do jogo.

Limites que valem hoje:

- As tabelas do hub que ficam no `arm9` (lojas, TMs, tutores) seguem escondidas no formato ndstool. A Master Ball da loja foi aplicada à mão.
- Até 255 mensagens por mapa.
- 51 NPCs é o máximo visto no jogo original. Passar disso é terreno não testado.
- NPC novo só aparece ao reentrar no mapa se o save foi feito nele.

## 3. Etapas

Antes de cada etapa, salvar uma versão do projeto. Todo teste em jogo é no próprio `Pokemon SS`, porque os testes só em ds-rom já deixaram passar erros do formato ndstool.

### Etapa 1: modelo de missão no Novo evento

**Objetivo:** criar missões sem escrever script: buscar item, capturar e mostrar um Pokémon, receber recompensa, encadear missões e exigir condições. A venda de Pokémon entra aqui porque usa as mesmas peças.

**O que construir:**

- [ ] Modelo Missão, com o que é pedido: item (com quantidade) ou Pokémon (mostrar uma espécie).
- [ ] Recompensa: item, Pokémon ou dinheiro.
- [ ] Falas: pedido, ainda não cumpriu, recompensa, depois de concluída e condição não atendida.
- [ ] Condições, combináveis: depois da Elite, número de insígnias ou insígnia específica, outra missão concluída.
- [ ] Modelo Vender Pokémon: o NPC oferece uma espécie e um nível por um preço, mostra o dinheiro do jogador e pergunta sim ou não. Usa as mesmas condições, para haver vendedores que só aparecem com ofertas depois de certa insígnia ou da Elite.
- [ ] Levar as mesmas condições aos modelos que já existem, a começar por Trocar Pokémon, para a cadeia exigir a troca anterior.
- [ ] Editar e remover missões pelo `pokehub-events.json`, sem quebrar os NPCs já criados nesse arquivo.
- [ ] Guia na Ajuda e ferramentas no servidor MCP, como nos outros modelos.

**Comandos e dados:**

| Para quê | O que usar |
|---|---|
| Conferir e tirar item | `HasItem`, `TakeItem` |
| Dar item | `CallStd` 2033, com `HasSpaceForItem` antes, como o modelo Dar item já faz |
| Conferir Pokémon | `CountPartyMonsOfSpecies`, `PlayerHasSpecies`, ou a escolha no time do modelo de troca (`ScrCmd_566`, `GetPartySelection`, `GetPartyMonSpecies`) |
| Dar Pokémon | `GiveMon` |
| Dar dinheiro | `AddMoney` |
| Cobrar na venda | `ShowMoneyBox`, `HasEnoughMoneyImmediate`, `SubMoneyImmediate`, `UpdateMoneyBox`, `HideMoneyBox` |
| Depois da Elite | `CheckFlag` com `FLAG_GAME_CLEAR` (0x964) |
| Insígnias | `CheckBadge`, `CountBadges` |
| Missão concluída | Uma flag livre por missão, escolhida por `PickFreeFlag` |

**Esforço:** médio. O construtor de scripts já tem rótulos e saltos, e os modelos atuais servem de exemplo.

**Dependências:** nenhuma. É a base das etapas seguintes, porque a Etapa 1B e as histórias pós-Elite usam as condições.

**Riscos:**

- Ordem das operações: conferir espaço na mochila e no time antes de tirar o item pedido, senão o jogador perde o item e fica sem a recompensa. Na venda vale o mesmo: conferir dinheiro e time antes de cobrar.
- Os argumentos de `GiveMon` depois da espécie, e os de `CountBadges`, ainda precisam ser conferidos em scripts do próprio jogo.
- Não está confirmado se `PlayerHasSpecies` olha o time, a caixa ou a Pokédex. O modelo "mostrar" deve usar o comando que o teste confirmar.
- "Capturar" na prática é "mostrar": o script não distingue um Pokémon capturado de um recebido em troca. A missão não tira o Pokémon do jogador. Para pedir que ele o entregue, a saída é o modelo Trocar.
- Um NPC que só deve aparecer depois de uma condição é mais difícil: a flag do objeto esconde quando está ligada, o contrário do que se quer. A primeira versão deixa o NPC visível, com a fala "volte depois".
- Cada missão acrescenta várias falas ao banco do mapa, que tem o limite de 255.

**O que testar no jogo:**

- [ ] Falar sem o item pedido: mostra a fala de "ainda não" e nada muda.
- [ ] Falar com o item: ele sai da mochila e a recompensa entra.
- [ ] Falar com menos do que a quantidade pedida.
- [ ] Mochila cheia na hora da recompensa: o item pedido não some.
- [ ] Time cheio com recompensa em Pokémon, e time com ovo.
- [ ] Mostrar a espécie certa, a errada, e a certa só na caixa.
- [ ] Recompensa só uma vez, mesmo depois de salvar, sair e voltar.
- [ ] Condição de insígnias com menos, com o número exato e com mais.
- [ ] Missão B recusada antes da A e aceita depois.
- [ ] Depois da Elite: recusada antes dela. Para testar rápido, ligar 0x964 com um NPC só numa ROM de teste, e confirmar uma vez com o final real, porque essa flag ainda não foi vista ligando em jogo.
- [ ] Venda sem dinheiro suficiente: recusa e não cobra.
- [ ] Venda com dinheiro: cobra o valor exato, o saldo atualiza na caixa e o Pokémon chega com a espécie e o nível certos.
- [ ] Recusar a oferta: não cobra nada.
- [ ] Venda com o time cheio: o Pokémon vai para a caixa, ou a venda é recusada sem cobrar.
- [ ] Pokédex registra a espécie comprada.
- [ ] Vendedor de oferta única não vende de novo depois de salvar e voltar. O de oferta repetida continua vendendo.
- [ ] Save antigo continua carregando com as missões novas.

### Etapa 1B: eventos especiais

**Objetivo:** liberar eventos que o jogo já traz prontos, e dar por NPC os que ele não traz, como recompensa da tarefa pós-Elite em Kanto.

Começa logo após a Etapa 1, testando primeiro Latias e Latios. Essa sequência é a menor forma de responder à pergunta que decide o resto: **basta entregar o item, ou o evento exige algo mais?**

**O que a varredura de scripts achou:**

| Pokémon | O que há no jogo | Mapa (nome interno) |
|---|---|---|
| Latias, Latios | Enigma Stone | `T03R0101` |
| Dialga, Palkia, Giratina | `GiveMon` | `D51R0201` |
| Arceus | Checagem | `D51R0301` |
| Deoxys | Script | `R03` |
| Ho-Oh | Checagem | `D24R0208` |
| Mew, Jirachi, Manaphy, Darkrai, Shaymin | Nenhum script encontrado | |
| Celebi | Nenhuma checagem encontrada | |

A GS Ball não existe em HG/SS: é do Crystal. A ideia vira uma missão pós-Elite em Kanto cujo NPC entrega o item ou o Pokémon.

**O que construir:**

- [ ] Teste de Latias e Latios, com um NPC que dá a Enigma Stone, usando só o que a Etapa 1 entrega. Ler antes o script de `T03R0101` para saber o que ele confere.
- [ ] Missão pós-Elite em Kanto com a condição `FLAG_GAME_CLEAR`, como porta de entrada dos eventos.
- [ ] Repetir o teste nos outros eventos com script, um de cada vez, começando pelos que dependem de item ou flag.
- [ ] Para os que não têm script: recompensa de missão com `GiveMon`, ou modelo Pokémon no mapa quando fizer sentido uma batalha.
- [ ] Se os eventos exigirem a marca de encontro especial, avaliar como gravá-la. Pode exigir injeção de código e nesse caso passa a depender da decisão sobre o formato (seção 5).

**Comandos e dados:** `GiveItem` ou `CallStd` 2033, `GiveMon`, `WildBattle`, `CheckFlag`, além dos scripts dos mapas da tabela acima, lidos no Mapa de eventos.

**Esforço:** pequeno para Latias e Latios. O restante é desconhecido até o primeiro teste.

**Dependências:** Etapa 1, com as condições.

**Riscos:**

- Não se sabe se os eventos exigem marca de encontro especial. Pelo que se conhece da comunidade, alguns míticos só obedecem com essa marca, mas isso não foi conferido neste jogo.
- Um evento pode conferir flags ou variáveis que só a distribuição oficial ligaria.
- Script com sequência própria pode depender de outros passos que não apareceram na varredura.

**O que testar no jogo:**

- [ ] Receber a Enigma Stone do NPC e ir a `T03R0101`: o evento dispara.
- [ ] Depois de capturar, o evento não se repete.
- [ ] Usar o Pokémon em batalha com poucas insígnias e ver se obedece.
- [ ] Sem a Enigma Stone, o evento não dispara.
- [ ] A missão de Kanto recusa antes da Elite.
- [ ] Repetir os itens de disparo, repetição e obediência para cada evento novo.

### Etapa 2: modelo de treinador

**Objetivo:** colocar treinadores no mapa pelo Novo evento, com ou sem visão, com rebatalha e com a Equipe Rocket.

**O que construir, nesta ordem:**

- [ ] **Sem visão primeiro:** NPC comum cujo script inicia a batalha ao falar com ele. É o que atende "nem todo treinador chama", e não depende do campo de visão.
- [ ] Falas: desafio, derrota dele, depois de vencido. Perder leva ao mesmo fim do modelo Pokémon no mapa (`WhiteOut`).
- [ ] **Rebatalha:** liberar outra batalha com `ClearTrainerFlag`, com uma condição da Etapa 1, por exemplo depois da Elite. A frequência é decisão do usuário.
- [ ] **Equipe Rocket:** o mesmo modelo, com sprite e classe de Rocket, opção de sumir depois de vencido e condições, para a história avançar por flags.
- [ ] **Com visão:** só depois de pesquisar como o jogo faz isso. Ponto de partida: o hub hoje não escreve os bytes 14 a 19 do registro de NPC (`EventFile.WriteObject`) e sempre grava `Type` 0. Comparar com treinadores do jogo e testar.
- [ ] Treinadores novos: testar se criar funciona. Até lá, o modelo escolhe entre os treinadores que já existem.

**Comandos e dados:** `TrainerBattle`, `CheckTrainerFlag`, `SetTrainerFlag`, `ClearTrainerFlag`, `GoToIfTrainerDefeated`, `CheckBattleWon`, `WhiteOut`. Para a visão, a investigar: `GetTrainerEyeType`, `GetEyeTrainerNum`, `TrainerStepTowardsPlayer`, `TrainerMessage`. Dados: arquivos de treinadores (time e nome) e o arquivo de eventos do mapa.

**Esforço:** médio sem visão e com rebatalha. Grande e ainda desconhecido com visão e treinadores novos.

**Dependências:** Etapa 1, para as condições da rebatalha e da Rocket.

**Riscos:**

- Um treinador do jogo usado por um NPC novo divide com o original a flag de vencido.
- Rebatalha com time mais forte pede outro registro de treinador. Se criar não funcionar, é preciso achar registros sem uso, e não foi verificado se existem.
- Cada treinador novo usa uma flag da faixa de treinadores. Hoje a faixa começa em 0x550 e o hub calcula o fim pelo total de treinadores, então uma flag já entregue a um NPC pode cair dentro dela. Conferir antes de criar.
- `docs/02-arquitetura-fase1.md` (item 4 da fase 3) registra que adicionar treinadores exige expandir tabelas no `arm9`. Isso não foi testado. Se for verdade, a dependência do formato também vale aqui.

**O que testar no jogo:**

- [ ] O treinador sem visão não chama quando o jogador passa na frente dele.
- [ ] Falar inicia a batalha, com o time certo.
- [ ] Vencer: passa à fala de vencido, e o prêmio em dinheiro é pago.
- [ ] Perder: o jogador volta ao Centro Pokémon e o treinador continua disponível.
- [ ] Salvar, sair e voltar: ele continua vencido.
- [ ] A rebatalha só libera quando a condição é atendida, e o time certo aparece.
- [ ] Rocket some depois de vencido e não volta ao reentrar no mapa.
- [ ] O treinador original do jogo, se reaproveitado, continua correto.
- [ ] Batalha em dupla, se o registro for desse tipo.

### Etapa 3: IA em lote

**Objetivo:** aplicar de uma vez as opções de IA e os itens de cura, por classe ou nome, nas Operações em lote.

**O que construir:**

- [ ] Em `TrainerBatchOptions`: ligar e desligar opções de IA, definir os 4 itens do treinador e a dificuldade.
- [ ] Prévia e aplicação pela mesma função, como o lote já faz, com versão salva antes e depois.
- [ ] Mostrar a mudança em "O que mudou". O relatório já cobre IA e itens.
- [ ] Sugestão de receitas: por exemplo, líderes e Elite com todas as opções competitivas e cura.
- [ ] Só depois avaliar mudar o comportamento da IA com injeção, se os dados não bastarem.

**Comandos e dados:** nenhum comando de script. Só dados de treinador: 11 opções de IA, 4 itens e dificuldade, que ficam em arquivos e não no `arm9`, então não sofrem a limitação do ndstool.

**Esforço:** pequeno.

**Dependências:** nenhuma. Pode ser antecipada se o usuário quiser sentir a dificuldade logo.

**Riscos:**

- Os nomes das 11 opções vêm da decomp do Platinum. O significado em HG/SS é presumido igual, e não testado um a um.
- Filtro largo demais altera treinadores que não deviam mudar. A prévia reduz esse risco.
- Treinadores comuns com IA forte podem deixar o início do jogo frustrante. É questão de ajuste, a decidir jogando.
- Mudar o comportamento da IA com injeção pede o formato ds-rom e tem risco maior. hg-engine não serve (seção 4).

**O que testar no jogo:**

- [ ] Um líder usa o item de cura com a vida baixa, e não fica repetindo o uso.
- [ ] A escolha de golpes muda de forma perceptível entre a IA antiga e a nova, na mesma batalha.
- [ ] Treinadores fora do filtro continuam iguais.
- [ ] Batalha em dupla e Elite terminam sem travar.

### Etapa 4: tela "O que falta capturar"

**Objetivo:** saber quais espécies ainda não se consegue no jogo e depois distribuí-las por fase, para que dê para capturar todas.

**Situação atual:** a varredura achou 372 de 493 espécies. Os 121 restantes são um teto, não a lista real. Ela não enxerga iniciais, Ho-Oh, Lugia, Raikou, Entei, fósseis, prêmios, concurso e scripts com variável. Falta classificar o que sobrar em exclusivos do HeartGold, Pokéwalker e eventos.

**O que construir:**

- [ ] Tela com cada espécie, como se obtém e onde. Fontes: encontros (com Headbutt e Safari), scripts, trocas.
- [ ] Seguir as evoluções e os filhotes (Tabelas do jogo) a partir do que já é obtido.
- [ ] Lista fixa, marcada como tal, para o que a varredura não enxerga.
- [ ] Classificação do que falta: exclusivo do HeartGold, Pokéwalker, evento, outro.
- [ ] Clicar numa espécie abre o lugar para editá-la. Encontro editado reflete na tela.
- [ ] Aviso quando uma edição faz uma espécie deixar de ser obtida.
- [ ] Depois da tela: distribuir as espécies por fase do jogo, e atualizar as áreas da Pokédex.

**Comandos e dados:** tabelas de encontros, `WildBattle`, `GiveMon`, trocas (`a/1/1/2`), evoluções e filhotes. É só leitura até a distribuição, que usa o editor de Encontros e o lote.

**Esforço:** médio para a tela. A distribuição é trabalho de design do jogo, mais do que de código.

**Dependências:** para evolução por troca, a Etapa 5 muda a resposta. Para eventos, a Etapa 1B. A tela pode sair antes e ser atualizada depois.

**Riscos:**

- Falso "falta": a lista fixa pode errar para o jogo editado. Tem que aparecer como "não confirmado", sem afirmar.
- Encontro com taxa muito baixa conta como obtido, mas na prática é difícil. Mostrar a taxa.
- Tirar uma espécie de uma rota para dar lugar a outra pode criar uma falta nova. O aviso acima cobre isso.
- Forma de um Pokémon com identidade própria (como Unown) fica fora da conta de espécies.

**O que testar no jogo:**

- [ ] Por amostra: escolher 10 espécies marcadas como obtidas, de fontes diferentes, e confirmar o local.
- [ ] Escolher 5 marcadas como faltando e procurar uma forma de obter.
- [ ] Depois da distribuição, capturar uma espécie de cada fase no local e horário indicados.
- [ ] A área da Pokédex mostra o novo local.

### Etapa 5: evolução paga

**Objetivo:** um local que recebe um Pokémon de evolução por troca e devolve evoluído por um valor alto. Afeta 17 evoluções.

**Primeiro, pesquisa de viabilidade.** Nenhum comando de script evolui Pokémon, e só a injeção devolve o mesmo Pokémon. O código fica para depois do resultado.

**Bloqueio já conhecido:** a injeção de código do hub só aceita workspace ds-rom (`CodePatchService.RequireDsRom`), e o `Pokemon SS` está em ndstool. O caminho recomendado não roda no projeto real enquanto isso não for resolvido: ou o projeto é convertido, ou a injeção passa a aceitar ndstool. É decisão do usuário, com o custo levantado na pesquisa.

**Caminhos:**

| Caminho | Mesmo Pokémon? | Pede injeção? | Observação |
|---|---|---|---|
| Comando novo por injeção | Sim | Sim | Recomendado para pesquisar primeiro. É o único que cumpre a ideia original |
| Item que evolui | Sim, com o jogador usando o item | Não | Plano B. Muda a evolução nas Tabelas do jogo, e o NPC vende o item por um valor alto |
| Troca disfarçada | Não | Não | Perde a identidade: apelido, treinador, golpes, natureza. Só como último recurso |

**O que construir:**

- [ ] Pesquisa, sobre uma cópia de teste: achar na decomp pokeheartgold a rotina de evolução e como chamar a tela de evolução fora de uma batalha.
- [ ] Pesquisa: como acrescentar um comando à tabela do jogo, ou reaproveitar um sem uso.
- [ ] Pesquisa: custo e risco de converter o `Pokemon SS` para ds-rom, frente a estender a injeção ao ndstool.
- [ ] Protótipo mínimo: evoluir um Pokémon do time a partir de um script, em ROM de teste.
- [ ] Resultado escrito: seguir com a injeção ou ir para o plano B.
- [ ] Depois do sim: modelo Evolução paga no Novo evento, com a lista das 17 evoluções e o preço.

**Comandos e dados:** `HasEnoughMoneyImmediate`, `SubMoneyImmediate`, `ShowMoneyBox`, `UpdateMoneyBox`, `HideMoneyBox` para o pagamento, e `ScrCmd_566`, `GetPartySelection`, `GetPartyMonSpecies` para escolher o Pokémon. A evolução em si seria o comando novo, ou, no plano B, a tabela de evoluções e os itens.

**Esforço:** grande, e só estimável depois da pesquisa. O plano B é pequeno.

**Dependências:** decisão sobre o formato do workspace. Etapa 1, para reaproveitar a escolha e o pagamento.

**Riscos:**

- Código injetado pode travar o jogo ou corromper o save. Só avança sobre versão salva e ROM de teste.
- A tela de evolução pode depender de um estado que só existe depois de batalha ou troca.
- Evolução que depende de item segurado precisa conferir e consumir o item.
- Converter o workspace do projeto real mexe em tudo o que o usuário já fez. Precisa de versão salva e de uma comparação do resultado.

**O que testar no jogo:**

- [ ] Sem dinheiro: recusa e não muda nada.
- [ ] Com dinheiro: cobra o valor exato e o saldo atualiza.
- [ ] O Pokémon volta evoluído com apelido, treinador, nível, natureza e golpes preservados.
- [ ] Espécie que não evolui por troca é recusada, sem cobrar.
- [ ] Evolução com item segurado, com e sem o item.
- [ ] Cancelar na escolha do time.
- [ ] Salvar, reiniciar e conferir o Pokémon na caixa e no time.
- [ ] Testar as 17 evoluções, não só uma.

### Etapa 6: cara nova

**Objetivo:** o jogo parecer outro quando aberto, sem reimportação 3D.

**O que definir quando a etapa começar (nada disso está pendente agora):**

- [ ] Nome do jogo.
- [ ] Logo e tela de título: traz a arte ou pede sugestão?
- [ ] Banner e ícone da ROM.
- [ ] O que muda de aparência: paletas de personagens, Pokémon, menus?
- [ ] Música nova ou só a original.
- [ ] Idioma das falas novas (seção 5).

**O que construir:**

- [ ] Descobrir onde estão a tela de título e o logo no HG/SS, e se o formato deles é editável.
- [ ] Guia passo a passo com NitroPaint, navegador de NARCs e Nitro Studio 2.
- [ ] Edição do banner, se faltar no hub.
- [ ] Trocar textos fixos com o nome do jogo, pelo editor de Textos.

**Comandos e dados:** nenhum comando de script. Gráficos em NARCs (`Graphics/NarcBrowser`), banner, bancos de texto, áudio SDAT. A prévia de texto ajuda a conferir linhas longas.

**Esforço:** pequeno a médio no hub. A maior parte é arte do usuário. A tela de título está em aberto até ser localizada.

**Dependências:** nenhuma técnica. Fica por último porque depende do jogo definido.

**Riscos:**

- A tela de título não foi investigada: pode ter partes em código ou em outro formato.
- O navegador de NARCs não garante as cores quando o arquivo não indica a paleta.
- Imagem maior ou com mais cores que o original pode quebrar a tela.

**O que testar no jogo:**

- [ ] A tela de título completa, com animação e passagem para o menu.
- [ ] Banner e ícone no menu do emulador.
- [ ] Sem cores erradas nas telas alteradas, nas duas telas do DS.
- [ ] Nome novo em todas as falas onde o antigo aparecia.
- [ ] Música, se trocada: toca em loop e volta depois de uma batalha.

## 4. Fora do escopo por enquanto

- **Áreas novas:** talvez no futuro. Hoje o Map Studio foi só instalado e aberto pelo hub, sem desenhar nem exportar.
- **Diamond e Pearl:** o Novo evento não os cobre, e nada deste roteiro depende deles.
- **Reimportação 3D:** o hub só converte modelos para fora e lê mapas para mostrar.
- **hg-engine:** só serve para HeartGold em inglês e usa outra base de ROM. O hack é de SoulSilver.

## 5. Decisões e pontos não confirmados

### Decisões do usuário

Registradas em 01/11/2026. **Valem para o jogo oficial, que ainda não será feito.** O trabalho de agora é só construir as ferramentas que tornam isso possível. A ROM oficial virá depois, de um projeto novo, feito do zero.

| Assunto | Decisão |
|---|---|
| Missão de Pokémon | O jogador só mostra. Para entregar, usa o modelo de troca |
| NPC com condição | Sempre visível, com uma fala que indique uma possível missão escondida |
| Formato | O jogo oficial nasce em projeto novo. O teste de conversão numa cópia do `Pokemon SS` foi aceito, mas deixa de ser necessário para o jogo oficial |
| Idioma das falas novas | Inglês |
| Selvagens de Johto | Manter Rota 46 e a entrada de Dark Cave. Subir um pouco Ilex. Corrigir as Rotas 38 a 43 |
| IA | Competitiva também nas rotas. Líderes difíceis como a Whitney |
| Rebatalha | Vencer a Elite dispara um gatilho que libera todos os treinadores de novo e sobe os níveis por 1,3 |
| Arco pós-Elite | Aprovado como base, sem fechar detalhes (`05-lacunas-da-historia.md`, seção 4) |
| Evento do Giovanni | Uma missão dá o Celebi, e o santuário confere só a espécie |
| Capturar todas | Todas as espécies, com distribuição proposta para o usuário revisar. Lendários em batalha no mapa, míticos como recompensa de missão, iniciais depois da Elite em missões ou vendedores |
| Vendedores | Oferta única para raras, repetida para comuns |

**Ainda em aberto:**

- **Evolução paga:** parada por decisão do usuário, inclusive o plano B e o preço.

**Interpretações a confirmar quando o jogo oficial começar:**

- "No nível da Whitney" foi lido como dificuldade, pela fama da batalha dela, e não como os níveis 17 a 19.
- "Reseta todos os treinadores ou sobe o nível" foi lido como as duas coisas juntas, uma vez só, sem acumular a cada vitória na Elite.

### Ferramentas que essas decisões pedem

| Ferramenta | Situação | O que se sabe |
|---|---|---|
| Filtro por área no lote de encontros | Pronta | Região e lugares, nas Operações em lote. Serve para Johto e, depois, Kanto |
| Trocar espécie por evoluída ao subir nível | Pronta | Treinadores e encontros. Pedra, amizade e troca entram num nível à escolha |
| Gatilho pós-Elite: liberar treinadores | Pronta | Card Depois da Elite: script chamado antes de `HOFCredits 0` em `T10R0701`. Testado com o fim da Elite simulado; falta o comando real no Hall da Fama |
| Gatilho pós-Elite: níveis × 1,3 | Pronta | Card Depois da Elite: gancho na leitura do time, só SoulSilver US em ds-rom. Testado no jogo com o fim da Elite simulado |
| Destrancar o evento do Giovanni | Pronta | Card Depois da Elite: o santuário confere só a espécie. Testado no jogo |
| Fala de pista no NPC com condição | Pode já bastar | O modelo tem fala de condição não atendida. Falta só o texto |
| Vendedor de oferta única ou repetida | Pronta | `SellBehavior.Once` |

**Pontos a resolver no multiplicador × 1,3:**

- Nível máximo: Red iria de 88 a 114, então precisa de limite em 100.
- Times de rebatalha já altos (701 a 705, 712 a 727, 489 a 491) podem ficar de fora ou acumular demais.
- Treinadores do início de Johto continuam fracos: nível 15 vira 20, com o jogador perto de 50. Um piso de nível pode ser necessário para o "crescimento" valer em todo lugar.

**Testes:** como o jogo oficial nasce em projeto novo, que o hub cria em ds-rom quando o ds-rom está disponível, as ferramentas precisam ser validadas também em ds-rom (`Teste SS`), e não só no `Pokemon SS`.

### Plano de progressão (decidido em 01/11/2026)

**Direção:** Johto muito difícil, experiência mais lenta e Kanto como recomeço por escolha do jogador. O jogo não obriga nada: os desafios opcionais dão prêmios. Voltar o nível dos Pokémon e travar a evolução final em Kanto ficaram de fora.

**Johto:**

- Líderes com IA competitiva, itens de batalha e times mais fortes.
- Treinadores de rota mais fortes, com IVs altos, e selvagens subindo junto. Vale o que já foi decidido para os selvagens de Johto.

**Kanto:**

- Selvagens com faixa variada, de 2 a 60: comuns fracos, raros fortes. O teto sobe de lugar para lugar, de cerca de 45 no começo até 60 ou mais no fim. O nível de 60 no começo da região fica nos espaços de 1%.
- Todos os treinadores de Kanto escalam pelas insígnias de Kanto, com um nível-alvo por insígnia e mantendo a diferença interna de cada time. Treinadores de rota ficam cerca de 5 níveis abaixo do líder. Times de rebatalha ficam de fora.
- Nível-alvo inicial do Pokémon mais forte do líder, com 0 a 7 insígnias: 45, 48, 50, 53, 55, 57, 59 e 62. São um ponto de partida para ajustar jogando.
- Incentivos ao time novo: inicial de Kanto oferecido na chegada, capturas de nível útil, espécies que não existem em Johto, missões da região e um ginásio opcional com limite de nível e prêmio.

**Experiência:** começar com a experiência base das espécies perto de 70%. Se treinar um time novo em Kanto ficar lento demais, fazer os Pokémon abaixo do nível do adversário ganharem mais, o que pede injeção e pesquisa nova.

**Riscos a medir jogando:** experiência lenta contra time novo; Johto muito difícil obrigar a treinar demais; captura de nível 60 facilitar a chegada a Kanto.

| Ferramenta | Situação | O que se sabe |
|---|---|---|
| Faixa de nível por raridade, por lugar | Pronta e testada no jogo | Operações em lote: piso e teto, junto com região e lugares |
| Experiência base em lote | Pronta e testada no jogo | Operações em lote, grupo Espécies |
| IVs em lote nos treinadores | Pronta, só com testes automáticos | Operações em lote: dificuldade de 0 a 255. O jogo não mostra os IVs do adversário |
| Dar Pokémon sem pedir nada, no Novo evento | Pronta e testada no jogo | Missão com "Nada: é um presente" |
| Condição de nível máximo do time | Pronta e testada no jogo | Condições do Novo evento, com `PartyCountMonsAtOrBelowLevel` |
| Níveis por insígnia em Kanto | Pronta e testada no jogo | Card Depois da Elite. A lista de quem é de Kanto sai dos mapas de região 1, tirando a Liga e o caminho até ela (T10, D43, D45, R26 e R27), que vêm antes do fim do jogo. Giovanni, em Tohjo Falls, fica fora da escala. Esses treinadores ficam fora do × 1,3, porque Kanto só se alcança depois da Elite. Com 8 insígnias, o alvo é 65 |
| Experiência maior para quem está atrás | Não feita | Só se precisar. Pesquisa nova |

### Jogo oficial: onde está

Fica em `pokemon_ss_from_pokhub`, um commit por contexto. Já aplicado em Johto, sem ser jogado: selvagens de grama, líderes, Elite, Lance, treinadores de rota e cavernas, e experiência em 70%.

Falta decidir ou aplicar: treinadores de ginásio, Rival e Equipe Rocket; treinadores novos nos locais vazios; alvos de Kanto, com a sugestão de 54 a 70 por causa do Lance no 56; Depois da Elite no projeto. Times de rebatalha por telefone não mudaram e podem ficar abaixo da primeira luta.

### Mais conteúdo pedido (01/11/2026)

1. Treinadores novos, principalmente onde há poucos.
2. Treinadores fortes, com iniciais e times equilibrados.
3. Tipo dragão cedo para alguns treinadores. Cuidado: Dragon Rage tira 40 de dano fixo, então escolher os golpes à mão.
4. Fósseis com dicas de NPCs. O reviver já existe em `T03R0101`, que confere os 7 fósseis.
5. Fósseis para encontrar no mapa.
6. Evoluções por troca recebidas em trocas de NPC, e trocas novas nas duas regiões: pedindo Pokémon raros, filhotes que forçam o daycare e, em alguns casos, nível mínimo. Há 118 vagas livres. O jogo não sabe se o filhote nasceu de ovo.
7. NPCs que ajudam nas tarefas do jogo novo.

Pontos 2, 3 e 4 já dão para fazer com o hub. As ferramentas dos outros estão prontas e foram aprovadas no jogo em ds-rom em 01/11/2026, inclusive reviver o Old Amber em `T03R0101`: card Treinadores por local, falas de treinador novo com visão, nível mínimo na troca, item em bola e escondido e NPC guia. Item escondido é um evento de tile comum, não o dos itens do jogo, então a Dowsing Machine não o encontra.

### O que foi confirmado

**Validado no jogo** (SoulSilver, 2026-10-31):

- Troca na vaga nova 26.
- Compra de Master Ball com o `arm9` descomprimido, no formato ndstool.

**Confirmado na lista de comandos do HG/SS** (`src/PokeHub.Core/Gen4/scrcmd-hgss.json`). Os comandos existem, mas o uso em missões não foi testado:

- `HasItem`, `TakeItem`, `GiveItem`, `GiveMon`
- `CountPartyMonsOfSpecies`, `PlayerHasSpecies`
- `ClearTrainerFlag`, `SetTrainerFlag`, `CheckTrainerFlag`, `TrainerBattle`
- `SubMoneyImmediate`, `HasEnoughMoneyImmediate`, `AddMoney`, `ShowMoneyBox`, `UpdateMoneyBox`, `HideMoneyBox`
- `CheckBadge`, `CountBadges`

**Confirmado por leitura de código ou de dados:**

- `FLAG_GAME_CLEAR` é 0x964 na decomp.
- Nenhum comando de script evolui Pokémon.
- A injeção de código recusa workspace que não seja ds-rom (`CodePatchService.RequireDsRom`).
- As tabelas do `arm9` seguem escondidas no formato ndstool.
- Os eventos e checagens da tabela da Etapa 1B existem nos scripts, por varredura. Não foram disparados em jogo.
- A GS Ball não existe em HG/SS.
- hg-engine só serve para HeartGold em inglês, com outra base de ROM.

### O que ainda é hipótese

Nada disto deve ser tratado como certo até o teste indicado:

| Ponto | Como confirmar |
|---|---|
| Se os eventos exigem marca de encontro especial | Etapa 1B, com Latias e Latios, e obediência em batalha |
| Campo de visão do treinador: onde fica e se funciona em NPC novo | Etapa 2, comparando com treinadores do jogo |
| Se criar treinadores novos funciona, e o que isso exige no `arm9` | Etapa 2, sem esquecer que cada um usa uma flag da faixa de treinadores |
| Se há registros de treinador sem uso | Etapa 2, por varredura |
| Tela de título: onde está e se é editável | Etapa 6 |
| Se dá para evoluir por injeção a partir de um script | Etapa 5, pesquisa e protótipo |
| Que a flag 0x964 liga ao terminar a Elite neste jogo | Etapa 1, com um save real |
| O que `PlayerHasSpecies` confere, e os argumentos de `GiveMon` e `CountBadges` | Etapa 1 |
| Que os nomes das 11 opções de IA, vindos do Platinum, valem em HG/SS | Etapa 3 |
| Que dados bastam para a IA ficar competitiva | Etapa 3, jogando |
| A lista real do que falta capturar (121 é o teto) | Etapa 4 |
| Que ninguém carrega o arquivo substituído pela injeção (`a/0/2/8`) | Etapa 5. A decomp não mostra uso, mas uma referência indireta não foi descartada |
| Platinum em jogo: os modelos novos valem lá? | Fora do foco. Não foi testado, e este roteiro não promete nada para Platinum |
