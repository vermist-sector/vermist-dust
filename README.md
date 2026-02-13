<div class="header" align="center">
<img src="https://github.com/vermist-sector/vermist-dust/blob/master/Resources/Textures/Logo/logo_badge.png" width="300" alt="Vermist Dust Sector"/>

Vermist Dust Sector is an [Impstation](https://github.com/impstation/imp-station-14) downstream, which itself is a downstream of [Space Station 14](https://github.com/space-wizards/space-station-14), a remake of SS13 that runs on [Robust Toolbox](https://github.com/space-wizards/RobustToolbox).
</div>

---

## Links

<div class="links" align="center">

##### Vermist Dust Sector
Website: TODO

</div>

<div class="links" align="center">

##### Impstation
[Website](https://impstation.gay/)

</div>

<div class="links" align="center">

##### Space Station 14

[Website](https://spacestation14.io/) | [Discord](https://discord.ss14.io/) | [Forum](https://forum.spacestation14.io/) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Standalone Download](https://spacestation14.io/about/nightlies/)

</div>

## Documentation/Wiki

SS14's [docs site](https://docs.spacestation14.com/) has documentation on SS14's content, engine, game design, and more.

## Contributing

We're happy to accept contributions from anyone! There's a lot to be done, code, spriting, even audio and so on. WIP.

## Building

#### Build Dependencies

> - Git
> - .NET SDK 9.*
> - Python 3.7 or higher

#### Steps

> 1. Clone this repo:
```shell
git clone https://github.com/vermist-sector/vermist-dust.git
```
> 2. Go to the project folder and run `RUN_THIS.py` to initialize the submodules and load the engine:
```shell
cd vermist-dust
python RUN_THIS.py
```
> 3. Compile the solution:
```shell
dotnet build
```

[More detailed instructions on building the project.](https://docs.spacestation14.com/en/general-development/setup.html)

---

## License

Content contributed after and including Impstation/VDS commit [`7210960b2b30e17aa001f4e35a5d0f80ca548e53`](https://github.com/vermist-sector/vermist-dust/commit/7210960b2b30e17aa001f4e35a5d0f80ca548e53) (`15 August 2024 17:02:49 UTC`) is licensed under the GNU Affero General Public License version 3.0 unless otherwise stated. See [LICENSE-AGPLv3](./LICENSE-AGPLv3.TXT).

Content contributed before Impstation/VDS commit [`7210960b2b30e17aa001f4e35a5d0f80ca548e53`](https://github.com/vermist-sector/vermist-dust/commit/7210960b2b30e17aa001f4e35a5d0f80ca548e53) (`15 August 2024 17:02:49 UTC`) is licensed under the MIT license unless otherwise stated. See [LICENSE-MIT](./LICENSE-MIT.TXT).

Code underneath any `_VDS` folder (`Resources/Prototypes/_VDS`, `Content.Server/_VDS`, etc...) and any Vermist Dust Sector specific scripts in Tools are licensed under AGPLv3. Other files are originally from other codebases and are not owned by Vermist Dust Sector, though any code must be relicensable to AGPLv3. SS14 is MIT licensed and Impstation is AGPLv3 licensed, so this forking is possible.

Most media assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and copyright specified in the metadata file. For example, see the [metadata for a crowbar](https://github.com/space-wizards/space-station-14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

Note that some assets are licensed under the non-commercial [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) or similar non-commercial licenses and will need to be removed if you wish to use this project commercially.

## Attributions

When we pull content from other forks, we try to organize their content to their own subfolders in each of the projects to keep track of attribution and try to prevent merge conflicts.

Content under these subdirectories either originate from their respective fork, or are modifications related to content from their respective fork.

| Subdirectory     | Fork Name           | Fork Repository                                                       | License  |
|------------------|---------------------|-----------------------------------------------------------------------|----------|
| `_VDS`           | Vermist Dust Sector | https://github.com/vermist-sector/vermist-dust                        | AGPL 3.0 |
| `_Impstation`    | Impstation          | https://github.com/impstation/imp-station-14/                         | AGPL 3.0 |
| `_CD`            | Cosmatic Drift      | https://github.com/cosmatic-drift-14/cosmatic-drift                   | MIT      |
| `_Corvax`        | Corvax              | https://github.com/space-syndicate/space-station-14                   | MIT      |
| `_DEN`           | The Den             | https://github.com/TheDenSS14/TheDen                                  | AGPL 3.0 |
| `_DV`            | Delta-V             | https://github.com/DeltaV-Station/Delta-v/                            | AGPL 3.0 |
| `_EE`            | Einstein Engines    | https://github.com/Simple-Station/Einstein-Engines/                   | AGPL 3.0 |
| `_EstacaoPirata` | Estacao Pirata      | https://github.com/Day-OS/estacao-pirata-14/                          | AGPL 3.0 |
| `_Floof`         | Floof Station       | https://github.com/Floof-Station/Floof-Station                        | AGPL 3.0 |
| n/a              | Funky Station       | https://github.com/funky-station/funky-station                        | AGPL 3.0 |
| `_Goobstation`   | Goob Station        | https://github.com/Goob-Station/Goob-Station/                         | AGPL 3.0 |
| `_NF`            | Frontier Station    | https://github.com/new-frontiers-14/frontier-station-14               | AGPL 3.0 |
| `_Harmony`       | Harmony             | https://github.com/ss14-harmony/ss14-harmony                          | AGPL 3.0 |
| `_Mono`          | Monolith            | https://github.com/Monolith-Station/Monolith                          | AGPL 3.0 |
| `_White`         | White Dream         | https://github.com/WWhiteDreamProject/wwdpublic/                      | AGPL 3.0 |
