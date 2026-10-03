# Vector 2 SDK - Windows

A toolkit for making custom content for Vector 2 on Windows. Build rooms,
create chapters and zones, edit triggers, import animations and manage your
content from one editor.

This is the Windows version of the Vector 2 SDK. It's an unofficial fan project,
not an official Nekki tool.

## What's included

- A room editor with object placement, transforms, a hierarchy and XML editing.
- Project Manager tools for chapters, zones, room pools and custom content.
- Trigger Designer for events, conditions and actions.
- Player and character designers with model and animation previews.
- Custom animation importing, a trick library and animation overrides.
- Tools for models, traps, backgrounds, textures and audio.
- Quest, mission, reward, tutorial and localization editors.
- An XML assistant with local checks, completions and repair previews.
- -Modified Build of vector 2

Shop editing is disabled in this release. Custom animation importing is still
available, but custom trick cards aren't currently supported in the game's shop.

## Using the SDK

Download the full release and extract it, then run
`Build/Vector2LevelEditor.exe`. Keep the Build folder together. The release
includes a compatible game build and the editor sets up its paths automatically.

Create a project or open an existing one. Save your changes in the relevant
studio, then use **Install Changes** to install your project content into the
game. **Play** tests the room you have open in the editor.

## Building from source

You'll need Windows and the .NET 10 SDK.

```bat
dotnet build Vector2LevelEditor.Wpf.csproj -c Release
```

To make a standalone Windows build:

```bat
dotnet publish Vector2LevelEditor.Wpf.csproj -c Release -r win-x64 --self-contained true -o Build
```

The editor's preview assets are included in `GameData` and `ProjectAssets`.
They are copied into the build automatically. The game player is supplied with
the full release, not this source repository. For playtesting a source build,
select a compatible game executable in Settings.

## Bug reports

Open an issue with the SDK version, what you were doing and what went wrong.
Screenshots and a small example room or project help when reproducing a bug.
Please don't include personal saves or account details.

## License

See [LICENSE.md](LICENSE.md). Original SDK code is available for non-commercial
use under that license. Vector 2 and its assets belong to Nekki and their
respective owners; they aren't covered by the SDK license.
