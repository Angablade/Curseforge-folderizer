<img src="/Curseforge-folderizer/curseforge-folderizer.png" width="500" />

## Curseforge Folderizer

A tool that will build a semi-complete instance folder from a CurseForge URL or ZIP.

## Overview

This tool takes in a CurseForge modpack version URL or a .zip file location on disk. It attempts to convert it to a bare-bones structure that can be transplanted on top of a vanilla + modloader instance in MultiMC.

This tool works with both the v1 of the CurseForge API, which builds mod packs with the `modlist.html` file, and the v2, which was introduced later and doesn't include it. Note: v2 requires hooking Google/Yahoo for cache data to get project ID resolution. This project uses Puppeteer to navigate the wider web.

## Usage

To use the Curseforge Folderizer:

1. Provide a CurseForge modpack version URL or a .zip file location.
2. Run the tool to convert it to a structure pasteable over a compatible MultiMC + Modloader instance.
3. Enjoy playing with your modpack in MultiMC!

## Building
1. Clone the repository:

   ```bash
   git clone https://github.com/your-username/curseforge-folderizer.git

2. Install dependencies:
   ```bash
   cd curseforge-folderizer
   dotnet restore

3. Run the tool:
   ```bash
   dotnet run

## License

This project is licensed under the [Apache License 2.0](LICENSE).

## Screenshots

*coming soon, I swear!*

## Contributing

Feel free to contribute by opening issues or creating pull requests.

## Acknowledgements

Special thanks to [Puppeteer](https://github.com/puppeteer/puppeteer) for making web scraping and automation a breeze.

