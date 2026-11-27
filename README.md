# KSP Real Cosmonauts

A **Kerbal Space Program** mod that replaces the green Kerbals with real human cosmonauts.

## Features

- Human facial and body proportions.
- Realistic spacesuits and textures.
- Works in the cockpit, on EVA and in the Astronaut Complex.
- Save-compatible and leaves gameplay unchanged.

## Status

Early development. Code and assets are a work in progress.

## Installation (once the first release is out)

1. Download the archive from the Releases page.
2. Copy the `GameData/RealCosmonauts` folder into your game's `GameData` folder.

## Requirements

- Kerbal Space Program 1.12.x.

## Building

You need the .NET SDK (6.0 or newer) and a local KSP 1.12 install. The game's DLLs are
referenced from that install and are never committed.

```sh
dotnet build src/RealCosmonauts -c Release -p:KSPRoot="/path/to/Kerbal Space Program"
```

You can set the `KSP_ROOT` environment variable instead of passing `KSPRoot`.
Without the .NET SDK you can build with Mono instead:
`tools/build-mono.sh "/path/to/KSP_x64_Data/Managed"`.
The build copies `RealCosmonauts.dll` into `GameData/RealCosmonauts/Plugins/` and then
copies the whole `GameData/RealCosmonauts` folder into the game. Pass
`-p:InstallToKSP=false` to skip the copy into the game.

## Project layout

```
GameData/RealCosmonauts/   What ships to players (plugin, models, textures, config)
src/RealCosmonauts/        C# plugin source
art/                       Blender and texture sources
docs/                      Roadmap and technical notes
```

See [docs/ROADMAP.md](docs/ROADMAP.md) and [docs/TECHNICAL_NOTES.md](docs/TECHNICAL_NOTES.md).

## Developers

- **gauss** ([@gauss-1911](https://github.com/gauss-1911))
- **Claude** (AI assistant by [Anthropic](https://www.anthropic.com))

## License

[MIT](LICENSE)
