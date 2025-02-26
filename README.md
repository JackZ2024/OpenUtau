
# OpenUtau

OpenUtau is a free, open-source editor made for the UTAU community.

[![Build](https://img.shields.io/github/actions/workflow/status/stakira/OpenUtau/build.yml?style=for-the-badge)](https://github.com/stakira/OpenUtau/actions/workflows/build.yml)
[![Discord](https://img.shields.io/discord/551606189386104834?style=for-the-badge&label=discord&logo=discord&logoColor=ffffff&color=7389D8&labelColor=6A7EC2)](https://discord.gg/UfpMnqMmEM)
[![QQ Qroup](https://img.shields.io/badge/QQ-485658015-blue?style=for-the-badge)](https://qm.qq.com/cgi-bin/qm/qr?k=8EtEpehB1a-nfTNAnngTVqX3o9xoIxmT&jump_from=webapi)
[![Trello](https://img.shields.io/badge/trello-go-blue?style=for-the-badge&logo=trello)](https://trello.com/b/93ANoCIV/openutau)

## Getting started

[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=windows-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-win-x64.zip)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=windows-x86&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-win-x86.zip)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=macos-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-osx-x64.dmg)</br>
[![Download](https://img.shields.io/static/v1?style=for-the-badge&logo=github&label=download&message=linux-x64&labelColor=FF347C&color=4ea6ea)](https://github.com/stakira/OpenUtau/releases/latest/download/OpenUtau-linux-x64.tar.gz)

It is **strongly recommended** that you read these Github wiki pages before using the software.
- [Getting-Started](https://github.com/stakira/OpenUtau/wiki/Getting-Started)
- [Resamplers](https://github.com/stakira/OpenUtau/wiki/Resamplers-and-Wavtools)
- [Phonemizers](https://github.com/stakira/OpenUtau/wiki/Phonemizers)
- [FAQ](https://github.com/stakira/OpenUtau/wiki/FAQ)

- [中文使用说明](https://opensynth.miraheze.org/wiki/OpenUTAU/%E4%BD%BF%E7%94%A8%E6%96%B9%E6%B3%95)

## How to contribute

Tried OpenUtau and not satisfied? Don't just walk away! You can help:
- Report issues on our [Discord server](https://discord.gg/UfpMnqMmEM) or Github.
- Suggest features on Discord or Github.
- Add or update translations for your language on [Crowdin](https://crowdin.com/project/oxygen-dioxideopenutau).

Know how to code? Got an idea for an improvement? Don't keep it to yourself!
- Contribute fixes via pull requests.
- Check out the development roadmap on [Trello](https://trello.com/b/93ANoCIV/openutau) and discuss it on Discord.

## Plugin development

Want to contribute plugins to help other users? Check out our API documentation:
- [Editing Macros API Document](OpenUtau.Core/Editing/README.md)
- [Phonemizers API Document](OpenUtau.Core/Api/README.md)

## Main features

Navigate the interface naturally and fluently using the mouse and scroll wheel. Keyboard shortcuts are also available.

![Editor](Misc/GIFs/editor.gif)

Easily create songs and covers using the feature-rich MIDI editor.

![Editor](Misc/GIFs/editor2.gif)

Create expressive vibratos with the easy-to-use vibrato editor.

![Vibrato](Misc/GIFs/vibrato.gif)

Pre-rendering and built-in resamplers let you quickly preview your work.

![Playback](Misc/GIFs/playback.gif)

See the [Getting-Started Wiki page](https://github.com/stakira/OpenUtau/wiki/Getting-Started) for more!

## All features
- Modern user experience.
- Easy navigation using the mouse and keyboard.
- Feature-rich MIDI editor.
  - Support for importing VSQX (Vocaloid 4) tracks.
- Selective backward compatibility with UTAU.
  - OpenUtau aims to solve problems with fewer steps. It is not designed to replicate UTAU features exactly.
- Extensible real-time phonetic editing.
  - Includes phonemizers for different phonetic systems (VCV, CVVC, Arpasing, etc.) in many different languages (English, Japanese, Chinese, Korean, Russian and more).
- Expressions replace the standard UTAU "flags" for tuning.
  - The built-in WORLDLINE-R resampler supports curve tuning, similar to many vocal synth editors.
- Internationalisation, including UI translation and file system encoding support.
  - Unlike UTAU, there is no need to change your system locale to use OpenUtau.
- Smooth preview/rendering experience.
  - Pre-rendering allows OpenUtau to render vocals before playback, saving time during editing and tuning.
- Supports ENUNU AI singers. See the ![ENUNU wiki page](https://github.com/stakira/OpenUtau/wiki/Status-of-ENUNU-NNSVS-Support) for more info.
- Easy-to-use plugin system.
- Versatile resampling engine interface.
  - Compatible with most UTAU resamplers.
- Runs on Windows (32/64 bit), macOS, and Linux.

### What it doesn't do
- While OpenUtau can do very minimal mixing, it will not replace your digital audio workstation of choice.
- OpenUtau does not aim for Vocaloid compatibility, except for some limited features.

### 在官方原版的基础上新增的功能
- 添加节拍器
- 添加几个常用快捷键：加载音高渲染结果(Ctrl+R);自动连奏(Ctrl+L);重置滑音(Alt+B);重置表情(Alt+E);清除颤音(Alit+C);重置颤音(Alt+R)
- 添加曲线编辑功能，可以通过调整点来编辑曲线
- 添加标签功能，可以给指定位置添加标签说明
- 把暂停后动作的功能放到工具栏
- 添加合并音符的功能
- 实现更灵活的音轨范围调整
- 添加仅加载选择音符音高的功能
- 添加缩放音频波形的功能，可以把音频波形放大，利于观察
- 添加调号标签功能
- 实现时间改变时左边的调号显示也跟着更新的功能
- 添加拖拽调整音轨顺序的功能
- 添加时间线可以显示为单根线或者原来的宽时间线的切换
- 在编辑界面显示时间码
- 添加批量调整曲线值的功能
- 平滑音高线功能减少平滑触发次数
- 音高支持note_glide
- 内置法语音素器
- 添加批量删除气声的功能
- 添加跳转到指定时间点的功能



