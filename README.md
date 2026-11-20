# English Listen

[![UI](https://img.shields.io/badge/UI框架-WinUI%203-0078D4?style=flat&logo=windows)](https://learn.microsoft.com/windows/apps/winui/) [![.NET](https://img.shields.io/badge/.NET-11-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com) [![Platform](https://img.shields.io/badge/平台-Windows%2010%2F11-0078D6?style=flat&logo=windows11)](https://github.com/dongle-china/English-Listen-Winui/releases) [![License](https://img.shields.io/badge/许可证-MIT-blue?style=flat&logo=open-source-initiative)](LICENSE.txt) [![GitHub Release](https://img.shields.io/github/v/release/dongle-china/English-Listen-Winui?style=flat&logo=github&label=版本)](https://github.com/dongle-china/English-Listen-Winui/releases) [![GitHub Downloads](https://img.shields.io/github/downloads/dongle-china/English-Listen-Winui/total?style=flat&logo=github&label=下载量)](https://github.com/dongle-china/English-Listen-Winui/releases) [![GitHub last commit](https://img.shields.io/github/last-commit/dongle-china/English-Listen-Winui?style=flat&logo=git&label=最后提交)](https://github.com/dongle-china/English-Listen-Winui/commits/main) [![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/dongle-china/English-Listen-Winui/ci.yml?style=flat&logo=githubactions&label=持续集成)](https://github.com/dongle-china/English-Listen-Winui/actions/workflows/ci.yml)

**English Listen** 是一款**离线优先**的 Windows 桌面英语精听训练台，通过 **真实音视频导入**、**Whisper 本地转写切句**、**泛听 / 精听 / 复习三阶段训练**、**错误归因与自适应难度** 等能力，帮助你从"背单词"走向"听得懂真实语料"。

基于 WinUI 3 / Windows App SDK 构建，原生 Windows 11 视觉（Mica 背景、深浅色主题），支持 Windows 10 (17763+) 与 Windows 11，x86 / x64 / ARM64 三架构发布。

---

## 特性

- **单词听写**：在线模式（边听边输入、自动判分）与纸笔模式（纸上作答、录入成绩），随机顺序与朗读翻译可选。
- **词库管理**：创建 / 加载 / 导入 / 导出 / 删除词库，分组管理与搜索筛选。
- **背单词**：每日计划、自动朗读单词与中文翻译、学习记录自动保存。
- **素材库**：导入你有权使用的本地音视频，Whisper.net 全程离线转写并自动切句，内容不上传。
- **句子精听**：泛听（整段播放、标记难点）→ 精听（逐句逐词校验）→ 复习（只练未掌握句）三阶段训练。
- **错误归因**：错误标签体系自动区分拼写、连读、语速等错误类型。
- **自适应难度**：基于错误模式的规则引擎动态调整难度并推荐句子，纯本地、可解释。
- **百度翻译**：批量翻译未翻译单词，一键追加到词库。
- **多用户与云同步**：每用户数据独立存于本地，可选 Web API 云同步学习记录。
- **学习统计**：听写与精听历史、成绩图表、排序与导出。
- **原生体验**：Mica 材质背景、深浅色主题跟随系统、微软雅黑界面字体。

---

## 快速开始

对于一般用户，建议直接在 [Releases](https://github.com/dongle-china/English-Listen-Winui/releases) 下载最新版本的 MSIX 安装包。

### 单词听写

1. 进入「查看单词」，添加或导入词库
2. 勾选单词，点击「开始听写」
3. 选择在线 / 纸笔模式开始训练
4. 完成后查看答案与统计

### 句子精听

1. 进入「素材库」，导入本地音视频
2. 首次使用：在「设置」页面下载 Whisper 转写模型（从魔搭社区 ModelScope 一键下载，显示速度与剩余时间；也可指定本地 ggml 模型文件）
3. 等待本地转写切句完成
4. 开始「泛听 → 精听 → 复习」三阶段训练

### 快捷键（听写测试中）

| 快捷键 | 功能 |
| ------ | ---- |
| 空格键 | 重复朗读当前单词 |
| ← / → | 上一个 / 下一个单词 |
| ESC | 暂停 / 继续 |

---

## 构建与开发

```bash
git clone https://github.com/dongle-china/English-Listen-Winui.git
cd English-Listen-Winui
dotnet build English-Listen-WinUI.csproj -c Debug -p:Platform=x64
```

或在 Visual Studio 中打开 `English-Listen-WinUI.slnx`。要求 .NET 11 SDK 与 Windows App SDK 工作负载。

技术栈：C# / .NET 11（`net11.0-windows10.0.19041`，单项目 MSIX）、Windows App SDK 1.8、Whisper.net 1.9、NAudio、System.Speech / Windows TTS 双语音引擎、Newtonsoft.Json。

架构分层：`Views/`（XAML 页面）→ `ViewModels/`（状态编排）→ `Services/`（转写 / 语音 / 同步 / 统计等接口化可插拔服务）→ `Models/`（数据模型）。

---

## 文档

- **[PRIVACY.md](PRIVACY.md)** — 隐私政策：所有学习数据默认保存在本地。
- **[LICENSE.txt](LICENSE.txt)** — MIT 许可证全文。
- 应用内「帮助」页面 — 各功能的完整使用教程。

---

## 项目活跃度与贡献者

### 活跃度徽章

[![GitHub contributors](https://img.shields.io/github/contributors/dongle-china/English-Listen-Winui?style=flat&label=总贡献者)](https://github.com/dongle-china/English-Listen-Winui/graphs/contributors)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/dongle-china/English-Listen-Winui?style=flat&label=月均提交)](https://github.com/dongle-china/English-Listen-Winui/graphs/contributors)
[![GitHub last commit](https://img.shields.io/github/last-commit/dongle-china/English-Listen-Winui?style=flat&label=最近提交)](https://github.com/dongle-china/English-Listen-Winui/commits/main)

### 贡献者图谱

<div align="center">
<a href="https://github.com/dongle-china/English-Listen-Winui/graphs/contributors" target="_blank">
<img src="https://contrib.rocks/image?repo=dongle-china/English-Listen-Winui" alt="Contributors" />
</a>
</div>

## Star History

<a href="https://www.star-history.com/?repos=dongle-china%2FEnglish-Listen-Winui&type=timeline">
<picture>
<source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=dongle-china%2FEnglish-Listen-Winui&type=timeline&theme=dark" />
<source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=dongle-china%2FEnglish-Listen-Winui&type=timeline" />
<img alt="Star History Chart" src="https://api.star-history.com/chart?repos=dongle-china%2FEnglish-Listen-Winui&type=timeline" />
</picture>
</a>

---

## 许可

[MIT License](LICENSE.txt)（MIT）
