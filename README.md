<img width="1872" height="721" alt="Vectorier-Editor-Title" src="https://github.com/user-attachments/assets/aebee1eb-7536-4dd8-9acf-5be96bcc66a6" />

<div align="center">
  <h1>
    Vectorier
  </h1>
</div>

<div align="center">
  A Fan-Made Level Creator to build your own dystopian stage for Vector
</div>

<div align="center">
  <h3>
    <a href="https://discord.com/invite/pVRuFBVwC2">
      Discord
    </a>
    <span> | </span>
    <a href="#introduction">
      Introduction
    </a>
    <span> | </span>
    <a href="#installation">
      Downloads
    </a>
    <span> | </span>
    <a href="#contributions">
      Contributions
    </a>
    <span> | </span>
    <a href="https://example.com/placeholder-wiki">
      Wiki
    </a>
  </h3>
</div>


# Introduction
Vectorier is a community-developed level editor for the game Vector to build your own dystopian stages. It runs as a standalone app on Windows, Linux and macOS, no Unity or .NET install needed. This project includes the following features:
- Export Level into an XML
- Import existing levels directly from the base game
- Built-in Game, allowing for quick playtest in both normal and Hunter mode
- Dynamic Editor to easily create dynamic objects
- Trigger Editor to quickly tweak the game's rule
- Move Visualizer to easily preview animations
- Parallax Preview directly in the Editor
- Object Editing Mode to edit objects and buildings in place
- Project Manager to manage levels, locations, textures, tricks, gears and more, then package and publish mods
- **Currently compatible with only Unity version**

A Discord server is open for the project : https://discord.com/invite/pVRuFBVwC2

<img width="1480" height="836" alt="Vectorier-Preview" src="https://github.com/user-attachments/assets/26176c5b-d541-4b64-8790-8572e2fb46e2" />

# Installation
- Go to the [Releases](https://github.com/FlipThoseTitle/Vectorier/releases) page
- Download the zip for your computer:
  - Windows: `Vectorier-x.x.x-Windows-x64.zip`
  - macOS (M1/M2/M3/M4): `Vectorier-x.x.x-macOS-AppleSilicon.zip`
  - macOS (Intel): `Vectorier-x.x.x-macOS-Intel.zip`
  - Linux: `Vectorier-x.x.x-Linux-x64.zip`
- Extract the zip file
- Run `Vectorier` (`Vectorier.exe` on Windows). Keep the `Editor`, `Game` and `Projects` folders next to it

On Windows, a blue "Windows protected your PC" window may show up the first time. This is because the app is not signed, not because it's a virus. Click More info, then Run anyway.

On macOS the app isn't signed by Apple, so the first time it may say it can't be opened. Right-click the app and choose Open, or go to System Settings > Privacy & Security and press Open Anyway. If it asks where the Vectorier folder is, pick the folder you extracted.

### Building from source
- Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Download the [Vectorier Project](https://github.com/FlipThoseTitle/Vectorier/archive/refs/heads/main.zip)
- Put the `Editor`, `Game` and `Projects` folders from a release into a `Data` folder at the root of the project
- Open the folder in VS Code and run the `Build Debug` or `Publish` task, or build `Vectorier.Desktop` with `dotnet build`
- Publishing also makes the release zip in the `publish` folder (`Vectorier-1.0.0-<OS>.zip`), with the macOS `.app` and the execute permissions already set

# Getting Started
Tutorial and Documentation is available on the [Vectorier Documentation Wiki](https://example.com/placeholder-wiki)

<img width="576" height="324" alt="Vectorier-Wiki" src="https://github.com/user-attachments/assets/872f0159-9ff9-4a6c-95c4-ff3b423d7892" />

# Contributions
This project was made possible through the collaboration and support of the following contributors. Each person has invested their time, knowledge, and effort to improve, refine, and expand this project. From identifying bugs and implementing new features, their involvement has been invaluable. The project stands as a result of shared effort and collective dedication.
- DoritoTheChips
- Supergoldapfel (sol)
- somemelguy (Mel)
- kubinka0505
- sonamenil (Vision)
- mirrorsedgegamer
- FloofIsFalaful (Floof)
- domnulinginer (domnul)
- enderdude290 (Ghosted)
- zayden1325682 (Zayden)

# AI Usage
Vectorier was made as an Avalonia-based application, with AI tools assisting throughout development. I use them to help understand the original code of the game, develop features, and put together the editor. This is still a work in progress, if there's any issues, feel free to open an issues right away!

# Disclaimer
This project is an independent, fan-made level editor for the game Vector developed and published by Nekki Limited. This project is not affiliated with, endorsed by, sponsored by, or approved by Nekki. Vector and all related assets, trademarks, and intellectual property belong to Nekki or their respective owners. Any Nekki assets included in this project are provided solely for modding and compatibility purposes, their inclusion does not grant any license or right to them, and they are not covered by this project's license. Nekki's License Agreement restricts copying, distributing, modifying, or creating derivative works from its games without written agreement, and you are solely responsible for complying with it. This project's own source code and original assets are licensed for non-commercial use only, no profit, including through donations, may be made from them. If you are a rights holder and would like any content removed, please open an issue. This software is provided for educational and personal use only, as is, and is maintained by non-paid contributors in their free time. This project is built using Avalonia, an open-source UI framework licensed under the MIT License. Avalonia is a trademark of AvaloniaUI OÜ, and this project is not affiliated with or endorsed by AvaloniaUI OÜ. For full license terms, see the [LICENSE](LICENSE) file.

Originally based on Vectorier-Unity-Editor by DoritoTheChips (2022). Maintained with his permission.
