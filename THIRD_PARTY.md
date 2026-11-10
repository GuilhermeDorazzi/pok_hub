# Créditos e licenças de terceiros

O PokHub é distribuído sob a GPL-3.0 (ver `LICENSE`). Ele usa dados, formatos e trechos de outros projetos da comunidade, listados abaixo com a licença de cada um.

## Incorporados ao código do hub

### DSPRE: DS Pokémon ROM Editor
- Repositório: <https://github.com/DS-Pokemon-Rom-Editor/DSPRE>
- Licença: **AGPL-3.0**
- O que o hub usa:
  - a tabela de caracteres dos jogos de Gen 4 (`src/PokeHub.Core/Gen4/charmap.json`, lida por `CharMap.cs`);
  - o formato dos arquivos de encontros de Diamond, Pearl e Platinum (`Encounters.cs`, a partir do `EncounterFileDPPt`);
  - a expansão do ARM9 do ROM Toolbox: endereços, tamanhos e a rotina de carga (`CodeInjection/Arm9Expansion.cs`, a partir do `ToolboxDB.cs`). O hub grava o mesmo patch para que as ROMs continuem compatíveis com os patches do DSPRE;
  - os caminhos dos arquivos da ROM e os offsets da tabela de cabeçalhos de mapa por jogo e idioma (`Gen4/Gen4Game.cs`, a partir do `RomInfo.cs`);
  - os offsets da tabela de sprites de campo (`Gen4/OverworldSprites.cs`, a partir do `SetOWtable` do `RomInfo.cs`).
- Copyright (C) Nømura e os contribuidores do DSPRE / DSPRE Reloaded.
- Cada um desses arquivos traz no topo o aviso de copyright do DSPRE, o que foi modificado e a declaração de que está sob a AGPL-3.0 (no `charmap.json`, nos campos `copyright` e `license` de `metadata`). O texto completo da licença está em [`LICENSES/AGPL-3.0.txt`](LICENSES/AGPL-3.0.txt).
- Essas partes continuam sob a AGPL-3.0. A seção 13 da GPL-3.0 permite combiná-las com o restante do hub.
- Os editores do hub (treinadores, encontros, textos, scripts e os demais) não são uma cópia dos editores do DSPRE. Para os editores do DSPRE, o hub abre o próprio DSPRE.

### pret: pokeheartgold e pokeplatinum
- Repositórios: <https://github.com/pret/pokeheartgold> e <https://github.com/pret/pokeplatinum>
- Licença: nenhuma declarada nos repositórios.
- O que o hub usa:
  - os nomes dos comandos de script e dos argumentos (`scrcmd-hgss.json`, `scrcmd-platinum.json`);
  - os nomes das flags (`flagnames-hgss.json`, `flagnames-platinum.json`);
  - os formatos das tabelas do jogo e endereços de funções, conferidos na decomp (`GameTables.cs`, `PostEliteService.cs` e outros);
  - as listas de sprites de campo dos Pokémon e as áreas da Pokédex do HG/SS (`follower-sprites-hgss.json`, `field-sprites-platinum.json`, `pokedex-areamap-hgss.json`).
- O trabalho de engenharia reversa é dos contribuidores da pret.

### apicula
- Repositório: <https://github.com/scurest/apicula>
- Licença: 0BSD
- O que o hub usa: a descrição do formato NSBMD (`Graphics/Nsbmd.cs`).

### Formato BPS
- Formato de patch criado por byuu para o beat, usado pela comunidade para distribuir hacks sem a ROM. O hub tem uma implementação própria (`Patch/Bps.cs`).

## Pacotes NuGet

| Pacote | Licença |
|---|---|
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.Hosting | MIT |
| ModelContextProtocol | Apache-2.0 |
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |

## Ferramentas que o hub instala e abre

Nenhuma delas vem com o hub. O usuário escolhe instalar, e o hub baixa a versão publicada pelo próprio projeto e a abre como um programa separado.

| Ferramenta | Repositório | Licença |
|---|---|---|
| ds-rom | <https://github.com/AetiasHax/ds-rom> | MIT |
| DSPRE | <https://github.com/DS-Pokemon-Rom-Editor/DSPRE> | AGPL-3.0 |
| melonDS | <https://github.com/melonDS-emu/melonDS> | GPL-3.0 |
| DeSmuME | <https://github.com/TASEmulators/desmume> | GPL-2.0 |
| NitroPaint | <https://github.com/Garhoogin/NitroPaint> | BSD-2-Clause |
| Tinke | <https://github.com/pleonex/tinke> | GPL-3.0 |
| PokEditor | <https://github.com/turtleisaac/PokEditor> | sem licença declarada |
| Pokémon DS Map Studio | <https://github.com/Trifindo/Pokemon-DS-Map-Studio> | ver o repositório |
| apicula | <https://github.com/scurest/apicula> | 0BSD |
| Universal Pokémon Randomizer ZX | <https://github.com/Ajarmar/universal-pokemon-randomizer-zx> | GPL-3.0 |
| hg-engine | <https://github.com/BluRosie/hg-engine> | ver o repositório |
| Decomps da pret | <https://github.com/pret> | sem licença declarada |

## Marcas

Pokémon, Nintendo DS, Pokémon HeartGold, Pokémon SoulSilver, Pokémon Diamond, Pearl e Platinum são marcas da Nintendo, da Game Freak, da Creatures e da The Pokémon Company. O PokHub é um projeto de fã, sem ligação com elas nem apoio delas.
