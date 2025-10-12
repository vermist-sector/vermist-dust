<p align="center"> <img src = "https://github.com/impstation/imp-station-14/blob/master/Resources/Textures/Logo/logo.png" width ="300" /> <img src = "https://github.com/vermist-sector/vermist-dust/blob/master/Resources/Textures/Logo/logo_badge.png" width ="300" /> </p>

Vermist Dust Sector is a weekly-run, private [Impstation](https://github.com/impstation/imp-station-14) instance that aims to provide a slower pace than Impstation proper. What if I want to play lowpop at 4PM? What then?

---

Vermist Dust Sector is a closely linked fork of Impstation, which is a fork of Space Station 14, a remake of SS13 that runs on [Robust Toolbox](https://github.com/space-wizards/RobustToolbox), a homegrown engine written in C#.

While we are not a server that allows sexual content, <b>we do not allow people under the age of 20 to play on Vermist.</b>

## Links
[Website](https://impstation.gay/) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Standalone Download](https://spacestation14.io/about/nightlies/)

## Documentation/Wiki

SS14's [docs site](https://docs.spacestation14.com/) has documentation on SS14's content, engine, game design, and more.
Additionally, see these resources for license and attribution information:
- [Robust Generic Attribution](https://docs.spacestation14.com/en/specifications/robust-generic-attribution.html)
- [Robust Station Image](https://docs.spacestation14.com/en/specifications/robust-station-image.html)

## Contributing

We are happy to accept contributions from anybody. However, unless your changes would fit the low-pop Vermist atmosphere more, it's [likely better to contribute to Impstation instead](https://github.com/impstation/imp-station-14). We always make sure we're up to date!

If you do wish to contribute to Vermist in particular, our PR guidelines are pretty much the same as Imp's. As a baseline make sure your changes and pull requests are in accordance with the upstream [contribution guidelines](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html). We're generally not as strict, but it's good practice to follow these examples.

If you are adding completely custom content that would go into the normal SS14 file structure in a certain spot, add that content to the `_VDS` folder with that same file path instead. For example, when adding the `ServerChatOOCColorManager.cs` chat manager for OOC color stuff, it would have gone in `Content.Server/Chat/Managers`. Instead, the `ServerChatOOCColorManager.cs` file is in `Content.Server/_VDS/Chat/Managers`.

The Vermist Dust Sector folders are located at `Content.Client/_VDS`, `Content.Server/_VDS`, and `Content.Shared/_VDS`. The Resources folder is kind of its own beast, and has a lot of depth. For that reason it makes sense to have the _VDS folder inside the subfolder it is modifying. As another example, the main prototypes folder for our custom content is located in `Resources/Prototypes/_VDS`. This applies for recipes, clothing, everything.

Keeping things defined like this makes the lives of the people maintaining the server much, much easier.

We are not currently accepting translations of the game on our main repository. If you would like to translate the game into another language, consider creating a fork or contributing to a fork.


## Building

1. Clone this repo:
```shell
git clone https://github.com/space-wizards/space-station-14.git
```
2. Go to the project folder and run `RUN_THIS.py` to initialize the submodules and load the engine:
```shell
cd space-station-14
python RUN_THIS.py
```
3. Compile the solution:

Build the server using `dotnet build`.

[More detailed instructions on building the project.](https://docs.spacestation14.com/en/general-development/setup.html)

## License

Content contributed to Impstation's repository after and including commit 7210960b2b30e17aa001f4e35a5d0f80ca548e53 (`15 August 2024 17:02:49 UTC`) is licensed under the GNU Affero General Public License version 3.0 unless otherwise stated. See [LICENSE-AGPLv3](./LICENSE-AGPLv3.TXT).

Content contributed to Impstation's repository before commit 7210960b2b30e17aa001f4e35a5d0f80ca548e53 (`15 August 2024 17:02:49 UTC`) is licensed under the MIT license unless otherwise stated. See [LICENSE-MIT](./LICENSE-MIT.TXT).

Code in Content./VDS, Resources//VDS and any VDS specific scripts in Tools are licensed under AGPLv3. Other files are originally from other codebases and are not owned by VDS, though any code must be relicensable to AGPLv3. SS14 is MIT licensed so this forking is possible.

Most assets are licensed under [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) unless stated otherwise. Assets have their license and copyright specified in the metadata file. For example, see the [metadata for a crowbar](https://github.com/space-wizards/space-station-14/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

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
| n/a              | Monolith            | https://github.com/Monolith-Station/Monolith                          | AGPL 3.0 |
| `_Offbrand`      | Offbrand            | https://github.com/space-wizards/space-station-14/tree/offmed-staging | MIT      |
| `_White`         | White Dream         | https://github.com/WWhiteDreamProject/wwdpublic/                      | AGPL 3.0 |
