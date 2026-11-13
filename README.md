# FoxesLauncher

[简体中文](#简体中文) | [English (US)](#english-us)

> **声明**：FoxesLauncher 是基于 [Plain Craft Launcher 2 (PCL2)](https://github.com/Meloong-Git/PCL) 的第三方分支，由终章 EndlessMC 组织维护。本项目与 PCL 官方无隶属关系，使用时请同时遵守 [LICENCE](LICENCE) 中的分发许可与合理使用指南。
> **Notice**: FoxesLauncher is a third-party fork of [Plain Craft Launcher 2 (PCL2)](https://github.com/Meloong-Git/PCL), maintained by the EndlessMC organization. This project is not affiliated with the official PCL project. Please also follow the distribution license and fair-use guidelines in [LICENCE](LICENCE).

[![Stars](https://img.shields.io/github/stars/FoxesLauncher/FoxesLauncher?style=flat&labelColor=444444&color=eac54f)](https://github.com/FoxesLauncher/FoxesLauncher)
[![Issues](https://img.shields.io/github/issues/FoxesLauncher/FoxesLauncher?style=flat&labelColor=444444&color=1F883D)](https://github.com/FoxesLauncher/FoxesLauncher/issues)
[![Build](https://github.com/FoxesLauncher/FoxesLauncher/actions/workflows/build.yml/badge.svg)](https://github.com/FoxesLauncher/FoxesLauncher/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-PCL%20Distribution%20License-blue.svg)](LICENCE)
[![Version](https://img.shields.io/badge/version-2.99.0.1-informational.svg)](https://github.com/FoxesLauncher/FoxesLauncher/releases)

---

## 简体中文

### 简介

FoxesLauncher（狐狐启动器）是由终章 EndlessMC 组织维护的 Minecraft 启动器，基于 Plain Craft Launcher 2 的源代码继续开发。项目包含 PCL 的大部分核心模块，包括 WPF UI 库、动画模块、下载模块、Minecraft 启动模块、实例管理、资源搜索与整合包处理等。

本仓库主要面向开发与构建。若只想使用启动器，请从 [Releases](https://github.com/FoxesLauncher/FoxesLauncher/releases) 获取构建产物。

### 功能特性

- **Minecraft 启动**：支持原版与 Forge、NeoForge、Fabric、OptiFine、LiteLoader 等加载器；提供 Java 自动补全、内存与 JVM 参数设置、版本隔离和崩溃分析。
- **实例管理**：创建、复制、删除、导出实例；管理 Mod、存档、资源包、光影和数据包。
- **资源下载**：接入 CurseForge 与 Modrinth，支持 Mod、整合包、资源包、光影和数据包的搜索与下载，并提供镜像源与 MC 百科跳转。
- **账号登录**：支持微软正版登录、统一通行证（Nide）、Authlib-Injector 外置登录和离线模式。
- **联机与主页**：内置 EasyTier 联机模块，支持自定义主页、主页预设与自定义事件。
- **个性化**：主题配色、背景图片、音乐播放器、启动器界面自定义等。
- **开源模块**：项目包含 UI 控件库、动画引擎、下载器、日志与网络工具等可复用模块。

### 环境要求

**运行**

- Windows 7 SP1 或更高版本（推荐 Windows 10/11）。
- .NET Framework 4.8 或更高版本。

**构建**

- Visual Studio 2022（安装“.NET 桌面开发”工作负载）或对应的 MSBuild。
- .NET Framework 4.8 Developer Pack。
- .NET SDK 8.0 或更高版本（用于构建 `PCLCS`）。
- Git（需要拉取 `MeloongCore` 子模块）。

### 构建

```bash
git clone --recursive https://github.com/FoxesLauncher/FoxesLauncher.git
cd FoxesLauncher

# 如果克隆时没有拉取子模块
git submodule update --init --recursive

# 复制本地构建配置，并按需填写私有值（.env 不会被提交）
cp .env.example .env

# 还原 NuGet 包
dotnet restore PCLCS/PCLCS.csproj

# 构建整个解决方案
msbuild "Plain Craft Launcher 2.sln" /p:Configuration=Release
```

也可以直接用 Visual Studio 2022 打开 `Plain Craft Launcher 2.sln`，选择 `Release` 配置后生成解决方案。

构建产物位于：

```text
Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
```

构建时会由 `build/GenerateSecrets.ps1` 读取仓库根目录的 `.env`（以及可选的 `.env.local`），生成 `Plain Craft Launcher 2/Modules/GeneratedSecrets.vb`。该生成文件与 `.env` 均已被 Git 忽略，请勿提交真实密钥。

主要配置项：

- `PCL_VERSION_BRANCH`：分支标识。开源版填 `OpenSource`，正式版填 `Official`，会影响更新、联网配置等功能的可用性判断。
- `PCL_REG_FOLDER`：注册表隔离目录。留空时，`Official` 使用 `PCL`，其他分支使用 `PCLDebug`。
- `PCL_MS_CLIENT_ID`：微软登录使用的 OAuth Client ID。
- `PCL_CURSEFORGE_API_KEY`：CurseForge API Key。
- `PCL_CLS_BASE_URL`：匿名遥测上报地址，留空则关闭。
- `PCL_COMMIT_HASH`：构建对应的 Commit Hash，留空时自动从 Git 读取。

也可以在构建前通过同名环境变量注入配置，环境变量优先于 `.env`（CI 即通过这种方式读取 repository secrets）。

未配置时，对应功能会使用空值或不可用，不影响其他模块构建。

### 项目结构

| 目录 | 说明 |
| --- | --- |
| `Plain Craft Launcher 2/` | VB.NET WPF 主程序，包含 UI、页面、动画、实例与启动逻辑。 |
| `PCLCS/` | C# 核心库，包含 Java、Minecraft 版本、安装清单与启动相关工具。 |
| `MeloongCore/` | Git 子模块（FoxesCore），提供通用 .NET 与 WPF 基础库。 |
| `Plain Craft Launcher 2.sln` | Visual Studio 解决方案。 |

### 版本与更新

当前版本为 `2.99.0.1`，版本号定义在 `Plain Craft Launcher 2/Modules/Base/ModBase.vb`，程序集版本定义在 `Plain Craft Launcher 2/My Project/AssemblyInfo.vb`。发布构建由 [Build 工作流](https://github.com/FoxesLauncher/FoxesLauncher/actions/workflows/build.yml) 生成。

### 变更日志

- `2.99.0.1`：同步 PCL 2.99.0.1；整理项目地址、Issue 模板、README 与仓库排除项；清理无引用代码、预编译发布包与未使用的重复清单文件。

### 许可证与致谢

本项目继续使用 PCL 的 [分发有限许可与存储库合理使用指南](LICENCE)。任何基于本项目的修改、分发或二次创作都必须遵守该文件的要求，并保留对原作者龙腾猫跃的署名与赞助链接。

- 原始项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)
- 帮助文档：[LTCatt/PCL2Help](https://github.com/LTCatt/PCL2Help)
- 感谢 PCL 社区、MeloongCore 贡献者以及所有提交反馈与代码的开发者。

---

## English (US)

### Overview

FoxesLauncher is a Minecraft launcher maintained by the EndlessMC organization, based on the source code of Plain Craft Launcher 2. It includes most of PCL's core modules, such as the WPF UI library, animation engine, download module, Minecraft launch module, instance management, resource search, and modpack handling.

This repository is primarily intended for development and building. If you only want to use the launcher, download a build from [Releases](https://github.com/FoxesLauncher/FoxesLauncher/releases).

### Features

- **Minecraft launching**: supports vanilla and loaders such as Forge, NeoForge, Fabric, OptiFine, and LiteLoader; includes Java detection, memory and JVM argument configuration, version isolation, and crash analysis.
- **Instance management**: create, copy, delete, and export instances; manage mods, saves, resource packs, shaders, and datapacks.
- **Resource downloads**: integrates CurseForge and Modrinth for mods, modpacks, resource packs, shaders, and datapacks, with mirror sources and MCMod links.
- **Account login**: supports Microsoft accounts, Nide, Authlib-Injector third-party authentication, and offline mode.
- **Multiplayer and homepage**: built-in EasyTier multiplayer, custom homepages, homepage presets, and custom events.
- **Customization**: themes, background images, music player, and launcher UI customization.
- **Reusable modules**: UI controls, animation engine, downloader, logging, and network utilities.

### Requirements

**Runtime**

- Windows 7 SP1 or newer (Windows 10/11 recommended).
- .NET Framework 4.8 or newer.

**Build**

- Visual Studio 2022 with the ".NET desktop development" workload, or equivalent MSBuild.
- .NET Framework 4.8 Developer Pack.
- .NET SDK 8.0 or newer (for `PCLCS`).
- Git (required for the `MeloongCore` submodule).

### Building

```bash
git clone --recursive https://github.com/FoxesLauncher/FoxesLauncher.git
cd FoxesLauncher

# If submodules were not fetched during cloning
git submodule update --init --recursive

# Copy local build configuration and fill in private values (.env is not committed)
cp .env.example .env

# Restore NuGet packages
dotnet restore PCLCS/PCLCS.csproj

# Build the whole solution
msbuild "Plain Craft Launcher 2.sln" /p:Configuration=Release
```

You can also open `Plain Craft Launcher 2.sln` in Visual Studio 2022, select the `Release` configuration, and build the solution.

The output is located at:

```text
Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
```

During the build, `build/GenerateSecrets.ps1` reads `.env` (and optionally `.env.local`) from the repository root and generates `Plain Craft Launcher 2/Modules/GeneratedSecrets.vb`. Both the generated file and `.env` are ignored by Git; never commit real secrets.

Main configuration keys:

- `PCL_VERSION_BRANCH`: branch marker. Use `OpenSource` for the open-source edition and `Official` for the official/release branch; this also controls update and online configuration features.
- `PCL_REG_FOLDER`: registry isolation folder. When empty, `Official` uses `PCL` and other branches use `PCLDebug`.
- `PCL_MS_CLIENT_ID`: OAuth client ID used for Microsoft login.
- `PCL_CURSEFORGE_API_KEY`: CurseForge API key.
- `PCL_CLS_BASE_URL`: anonymous telemetry endpoint; leave empty to disable.
- `PCL_COMMIT_HASH`: commit hash for the build; read from Git when empty.

You can also inject the same-named environment variables before building; they take precedence over `.env` (CI uses this to read repository secrets).

When unset, the corresponding features will use empty values or remain unavailable; other modules can still be built.

### Project Structure

| Directory | Description |
| --- | --- |
| `Plain Craft Launcher 2/` | VB.NET WPF application: UI, pages, animations, instances, and launch logic. |
| `PCLCS/` | C# core library: Java, Minecraft versions, install manifests, and launch utilities. |
| `MeloongCore/` | Git submodule (FoxesCore) providing shared .NET and WPF foundation libraries. |
| `Plain Craft Launcher 2.sln` | Visual Studio solution. |

### Version and Updates

The current version is `2.99.0.1`. The version name is defined in `Plain Craft Launcher 2/Modules/Base/ModBase.vb`, and the assembly version is defined in `Plain Craft Launcher 2/My Project/AssemblyInfo.vb`. Release builds are produced by the [Build workflow](https://github.com/FoxesLauncher/FoxesLauncher/actions/workflows/build.yml).

### Changelog

- `2.99.0.1`: synced with PCL 2.99.0.1; updated project URLs, issue templates, README, and repository ignore rules; removed unreferenced code, the prebuilt release archive, and the unused duplicate manifest.

### License and Credits

This project continues to use PCL's [Distribution License and Repository Fair-Use Guidelines](LICENCE). Any modification, distribution, or derivative work based on this project must comply with that document and retain the attribution and sponsorship links for the original author, 龙腾猫跃.

- Upstream project: [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)
- Help documentation: [LTCatt/PCL2Help](https://github.com/LTCatt/PCL2Help)
- Thanks to the PCL community, MeloongCore contributors, and everyone who has submitted feedback or code.

---

## 致谢 / Thanks

- [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL) 与龙腾猫跃 / and 龙腾猫跃
- PCL 社区与所有贡献者 / the PCL community and all contributors
- [FoxesCore](https://github.com/FoxesLauncher/FoxesCore)（基于 MeloongCore）贡献者 / contributors
- [JetBrains](https://www.jetbrains.com/) 提供的开源开发工具 / for open-source development tools
