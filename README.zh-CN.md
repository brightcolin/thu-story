# 清华园物语 · ThuStory

[English](README.md) | [中文](README.zh-CN.md)

![Project status: archived](https://img.shields.io/badge/status-archived-lightgrey.svg)
[![Backend tests](https://github.com/brightcolin/thu-story/actions/workflows/backend-tests.yml/badge.svg)](https://github.com/brightcolin/thu-story/actions/workflows/backend-tests.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> 清华大学软件设计大赛 **优胜奖** 作品

一款以清华大学校园生活为背景的模拟养成游戏。玩家在四年八学期中管理精力、健康、GPA、科研与社工能力，与 NPC 建立关系，并走向不同结局。

> [!IMPORTANT]
> 本仓库包含可独立运行的 FastAPI 后端、Unity C# 脚本快照、设计文档和演示素材，但不包含完整 Unity 工程，也不包含 GitHub 可下载的游戏可执行文件。

> [!NOTE]
> 本项目是比赛结束后的归档作品，当前不再积极维护。仓库主要用于作品展示、技术交流和历史留档，不承诺继续开发新功能或提供运行支持。

## 截图

<table>
  <tr>
    <td><img src="media/screenshots/screenshot-20260410-212008.png" width="360" alt="清华园物语游戏截图 1"/></td>
    <td><img src="media/screenshots/screenshot-20260410-212302.png" width="360" alt="清华园物语游戏截图 2"/></td>
  </tr>
  <tr>
    <td><img src="media/screenshots/screenshot-20260410-222314.png" width="360" alt="清华园物语游戏截图 3"/></td>
    <td><img src="media/screenshots/screenshot-20260410-223146.png" width="360" alt="清华园物语游戏截图 4"/></td>
  </tr>
</table>

演示视频：[视频 1](media/videos/demo-video-1.mp4) · [视频 2](media/videos/demo-video-2.mp4) · [视频 3](media/videos/demo-video-3.mp4)

## 游戏特色

- **服务端游戏时钟**：0.9 秒对应 1 游戏分钟，前端以服务器时间为准
- **校园养成系统**：课程掌握度、GPA、科研、社工、精力和健康相互影响
- **NPC 对话**：可接入 DeepSeek API 实时生成对话，无密钥时自动使用离线降级回复
- **多结局**：包含保研直博、出国留学、自主创业、考研和灵活就业等路线
- **日常与惩罚机制**：课程、用餐、宵禁、精力和健康共同驱动游戏进程

## 技术架构

```text
Unity C# 前端（薄客户端）
        ↕ HTTP API / X-Token
Python 3.10+ / FastAPI 后端（应用版本 v2.2）
        ↕
SQLite 单人存档
        ↕
DeepSeek API（可选）
```

后端是游戏状态的权威来源。时间、玩家属性、课程、活动、NPC 好感度和结局判定均存放在服务端 SQLite 数据库中。

## 快速开始：运行后端

### 环境要求

- Python 3.10+
- Windows PowerShell，或可执行等价命令的 macOS/Linux shell

### Windows PowerShell

```powershell
cd source-code/backend
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env

# 按需编辑 .env；不配置 DEEPSEEK_API_KEY 时使用离线 NPC 回复
python main.py
```

启动后可访问：

- 健康检查：<http://localhost:8000/health>
- Swagger API 文档：<http://localhost:8000/docs>

macOS/Linux 使用 `source venv/bin/activate` 和 `cp .env.example .env`。

### 运行后端测试

```powershell
cd source-code/backend
pip install -r requirements-dev.txt
python -m pytest -q
```

测试使用临时 SQLite 数据库，不会修改本地游戏存档。

## Unity 前端说明

`source-code/csharp-client/` 只保存比赛版本的 C# 脚本和 Unity `.meta` 文件。仓库缺少 `Assets/`、`Packages/`、`ProjectSettings/` 及场景资源，因此不能仅凭本仓库重新构建完整客户端。

本地工作目录可能存在被 `.gitignore` 排除的 `game-build/game.exe`，但该文件不会随 GitHub 克隆提供。如需公开试玩版本，建议单独发布到 GitHub Releases 或其他文件分发平台。

现有本地 `game.exe` 是此前编译的旧二进制，本次源码调整不会改变它；它可能仍包含已经停用的服务器地址。只有使用完整 Unity 工程重新构建后，新的本地默认地址和可配置令牌才会进入客户端。

## 项目结构

```text
source-code/
  backend/                  # FastAPI 后端与 pytest 回归测试
  csharp-client/            # Unity C# 脚本快照（非完整 Unity 工程）
    API/                    # 请求模型、HTTP 传输与流程控制
    Activities/             # 活动触发与展示
    Courses/                # 选课、课表与成绩单
    HUD/                    # HUD 与游戏菜单
    Monitors/               # 宵禁、缺餐、课程等后台监控
    NPC/                    # NPC、对话与好感系统
    Player/                 # 玩家状态与移动
    Scene/                  # 场景切换与摄像机
docs/                       # 玩法、系统、API 与部署文档
media/                      # 截图与演示视频
```

## 文档

- [玩家玩法说明](docs/gameplay-guide.md)
- [养成系统设计](docs/game-systems-design.md)
- [前端 API 对接说明](docs/frontend-api-guide.md)
- [后端部署说明](docs/deployment-guide.md)

## 当前限制

- 后端采用单人、单存档设计，不支持多个玩家共享同一服务实例
- Unity 工程资源和预编译客户端未纳入仓库
- 本地旧版 `game.exe` 未重新编译，不能用于验证本次 C# 配置改动
- SQLite 适合本地运行和低并发演示，不适合作为公网多用户数据库
- 客户端源码仍保留部分 v2.1 命名，实际后端 API 版本为 v2.2

将服务部署到公网前，请更换 `API_TOKEN`、启用 HTTPS、限制跨域来源，并避免直接暴露 SQLite 管理或存档重置能力。详见[部署说明](docs/deployment-guide.md)。

## 团队成员

| 成员 | GitHub |
|------|--------|
| brightcolin | [@brightcolin](https://github.com/brightcolin) |
| zhangchee25-cloud | [@zhangchee25-cloud](https://github.com/zhangchee25-cloud) |
| galaxy-3000 | [@galaxy-3000](https://github.com/galaxy-3000) |

## 参赛信息

- **赛事**：清华大学软件设计大赛
- **奖项**：优胜奖

## 许可证

仅以下内容采用 [MIT License](LICENSE)：`source-code/` 中的原创程序与测试代码、`.github/workflows/` 中的 CI 自动化，以及构建、运行或测试这些代码所需的原创软件配置文件。

README、`AGENTS.md`、`CLAUDE.md`、`docs/`、`media/`、`game-build/`、项目名称、标识和第三方材料不属于 MIT 授权范围；除另有说明外，相关权利保留。详细范围见 [NOTICE](NOTICE)。
