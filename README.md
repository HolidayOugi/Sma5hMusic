# Sma5hMusic Extra

This is a fork of [Sma5shMusic by Deinonychus71](https://github.com/Deinonychus71/Sma5hMusic) that adds extra functionality to the original executable.

> [!WARNING]
> The various new features are experimental and have not yet been tested extensively. Please make backups of your JSONs and music files before using them.

## CSK Pack Building

> [!NOTE]
> To run the CSK Music Packs in-game both [ARCropolis](https://github.com/raytwo/arcropolis) and the [CSK Collection](https://gamebanana.com/mods/499008) have to be installed on your system.


**CSK-compatible Music Packs** can be now be built. The software offers two options:

* **Single Pack**: A single Music Pack comprised of all the Mods and Series currently loaded.
* **Modular Packs**: Multiple Modular Music Packs for each Series and Mod. The user can choose which Series to generate. 

![Series Selection](https://elixi.re/i/bfyok.png)

> [!TIP]
> When using the Modular Packs option, if one or more Vanilla Series did not contain any songs during the generation, an optional Series Order pack is generated. If loaded, it ensures that each Series is in its correct order in the Music Select / Sound Test screen.

## Icon Selection and Conversion

**Icons** can now be assigned to Series directly from the Create Series / Edit Series menu. The Software accepts PNGs or BNTXs as input and assigns them to a Series, converting them if needed. The Icons are automatically copied to the output build.

![Icon Selection](https://elixi.re/i/t0jjq.png)

> [!TIP]
> To obtain the best results, please choose a square icon with a transparent background and an all-white texture.

## Importing of Audio Files

**Standard Audio files** (.mp3, .flac, .wav, .ogg, .m4a) can now be loaded directly into the software without prior conversion.

When loaded, the user is prompted to input the loop points for the given song. They can then be previewed by playing the track itself slightly before and after each loop point. The audio file is finally converted using the chosen loop points.

![Loop Point Selection](https://elixi.re/i/qj2ap.png)

> [!TIP]
> The preview duration can be set in the Global Settings.

### Automatic Loop Detection

> [!NOTE]
> Requires the installation of [PyMusicLooper](https://github.com/arkrow/PyMusicLooper) and for it to be accessible in PATH.

The software can also automatically detect potential loop points, making it easy to preview and choose from the suggested loops.

![PyMusicLooper](https://elixi.re/i/b8l0c.png)

> [!TIP]
> You can use this feature on songs you've already added!

## Normalization of Songs

> [!NOTE]
> Requires the download of [FFmpeg](https://www.ffmpeg.org/) and for its path to be set in the Global Settings.

Songs can now be **normalized** to a certain LUFS normalization level. This can be applied in three ways.

* **At Import Time**: when adding a song, either already converted or not, a checkbox can be checked for Normalization.
* **In BGM Properties**: a new option is available in the BGM Properties tab to normalize songs that've already been added.
* **Batch Normalization**: in the Extra submenu, a new option can be chosen to normalize all of the songs already added to the software.

![Normalization](https://elixi.re/i/xat1c.png)

> [!TIP]
> The LUFS Normalization value can be set in the Global Settings.

## Direct YouTube Download support

> [!NOTE]
> Requires the download of [FFmpeg](https://www.ffmpeg.org/) and [yt-dlp](https://github.com/yt-dlp/yt-dlp) and for their paths to be set in the Global Settings.

Songs can now be directly downloaded from **YouTube** and added to the software in a single action.

The songs can be imported either from their URL or from a text file containing all of the links, one per line.

![YouTube](https://elixi.re/i/ygkah.png)

> [!TIP]
> Playlists are also supported. The software notifies the user that all the songs from a given playlist will be downloaded.

## Miscellaneous fixes and improvements

* (*Should have*) fixed bug where songs would stop playing in-game due to their order in the global song list.
* Packs now are output in a subfolder of the build folder.
* Volume can now be set to the mean or median value of all songs' volume in a Mod.
* Songs can now be sorted alphabetically per game automatically.
* If a song from custom Series was not manually added to a playlist, the software will automatically add it to the Battlefield playlist to ensure it shows up in-game.
* Settings can now be saved when output folder is missing.
* Fixed importing of files with Japanese characters.

## Thanks & Repos of the different tools
1.  Original Code and Author: Deinonychus71
2.  Research: soneek
3.  Testing: Demonslayerx8, Segtendo
4.  Original Icon: Segtendo
5.  prcEditor: https://github.com/BenHall-7/paracobNET - BenHall-7
6.  paramLabels: https://github.com/ultimate-research/param-labels - BenHall-7, jam1garner, Dr-HyperCake, Birdwards, ThatNintendoNerd, ScanMountGoat, Meshima, Blazingflare, TheSmartKid, jugeeya, Demonslayerx8
7.  msbtEditor: https://github.com/IcySon55/3DLandMSBTeditor - IcySon55, exelix11
8.  nus3audio: https://github.com/jam1garner/nus3audio-rs - jam1garner
9.  bgm-property: https://github.com/jam1garner/smash-bgm-property - jam1garner
10.  VGAudio: https://github.com/Thealexbarney/VGAudio - Thealexbarney, soneek, jam1garner, devlead, Raytwo, nnn1590
11.  vgmstream: https://github.com/vgmstream/vgmstream - bnnm, kode54, NicknineTheEagle, bxaimc, Thealexbarney
All contributors: https://github.com/vgmstream/vgmstream/graphs/contributors
12. SoX: https://sox.sourceforge.net/ - SoX contributors
13. PyMusicLooper: https://github.com/arkrow/PyMusicLooper - arkrow and contributors
14. CrossArc: https://github.com/Ploaj/ArcCross Ploaj, ScanMountGoat, BenHall-7, shadowninja108, jam1garner, M-1-RLG
15. yt-dlp: https://github.com/yt-dlp/yt-dlp - yt-dlp contributors
16. FFmpeg: https://github.com/FFmpeg/FFmpeg - FFmpeg contributors