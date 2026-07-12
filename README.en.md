# ThuStory · Qinghua Garden Story

[中文](README.md) | [English](README.en.md)

![Project status: archived](https://img.shields.io/badge/status-archived-lightgrey.svg)
[![Backend tests](https://github.com/brightcolin/thu-story/actions/workflows/backend-tests.yml/badge.svg)](https://github.com/brightcolin/thu-story/actions/workflows/backend-tests.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> Merit Award project from the Tsinghua University Software Design Competition

ThuStory is a campus-life simulation game set at Tsinghua University. Players manage energy, health, GPA, research progress, and extracurricular development across four academic years, build relationships with NPCs, and work toward multiple endings.

> [!NOTE]
> This is an archived competition project and is no longer under active development. The repository is maintained primarily as a portfolio, technical reference, and historical record.

> [!IMPORTANT]
> The repository contains a runnable FastAPI backend, a snapshot of the Unity C# scripts, technical documentation, screenshots, and demo videos. It does not contain the complete Unity project or a downloadable game build.

## Screenshots

<table>
  <tr>
    <td><img src="media/screenshots/screenshot-20260410-212008.png" width="360" alt="ThuStory screenshot 1"/></td>
    <td><img src="media/screenshots/screenshot-20260410-212302.png" width="360" alt="ThuStory screenshot 2"/></td>
  </tr>
  <tr>
    <td><img src="media/screenshots/screenshot-20260410-222314.png" width="360" alt="ThuStory screenshot 3"/></td>
    <td><img src="media/screenshots/screenshot-20260410-223146.png" width="360" alt="ThuStory screenshot 4"/></td>
  </tr>
</table>

Demo videos: [Video 1](media/videos/demo-video-1.mp4) · [Video 2](media/videos/demo-video-2.mp4) · [Video 3](media/videos/demo-video-3.mp4)

## Highlights

- **Server-authoritative game clock:** 0.9 real seconds equals one in-game minute
- **Campus progression systems:** courses, GPA, research, extracurricular activities, energy, and health
- **NPC dialogue:** optional DeepSeek integration with offline fallback responses
- **Multiple endings:** postgraduate study, overseas study, entrepreneurship, employment, and other routes
- **Daily-life mechanics:** classes, meals, curfew, exhaustion, and health penalties

## Architecture

```text
Unity C# thin client
        ↕ HTTP API / X-Token
Python 3.10+ / FastAPI backend (application version 2.2)
        ↕
SQLite single-player save
        ↕
DeepSeek API (optional)
```

The backend is the source of truth for game time, player attributes, courses, activities, NPC relationships, and ending evaluation.

## Run the Backend

### Windows PowerShell

```powershell
cd source-code/backend
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env
python main.py
```

After startup:

- Health check: <http://localhost:8000/health>
- Swagger API documentation: <http://localhost:8000/docs>

On macOS or Linux, use `source venv/bin/activate` and `cp .env.example .env`.

The `DEEPSEEK_API_KEY` setting is optional. Without it, NPC conversations use local fallback responses.

### Run Tests

```powershell
cd source-code/backend
pip install -r requirements-dev.txt
python -m pytest -q
```

Tests use temporary SQLite databases and do not modify a local game save.

## Repository Layout

```text
source-code/
  backend/                  # FastAPI backend and pytest regression tests
  csharp-client/            # Unity C# script snapshot, not a complete Unity project
docs/                       # Gameplay, system, API, and deployment documentation in Chinese
media/                      # Screenshots and demo videos
```

## Known Limitations

- The backend uses one global single-player save and is not designed as a multi-user service.
- Unity scenes, assets, packages, and project settings are not included.
- The ignored local `game.exe` is a legacy binary and was not rebuilt after the server configuration changes.
- SQLite is suitable for local use and low-concurrency demonstrations, not a public multi-user deployment.
- Some Unity class names and comments still use the earlier v2.1 naming convention.

## Documentation

The detailed documentation is maintained in Chinese:

- [Gameplay guide](docs/gameplay-guide.md)
- [Game systems design](docs/game-systems-design.md)
- [Frontend API guide](docs/frontend-api-guide.md)
- [Backend deployment guide](docs/deployment-guide.md)

## Team

| Member | GitHub |
|--------|--------|
| brightcolin | [@brightcolin](https://github.com/brightcolin) |
| zhangchee25-cloud | [@zhangchee25-cloud](https://github.com/zhangchee25-cloud) |
| galaxy-3000 | [@galaxy-3000](https://github.com/galaxy-3000) |

## License

Original source code, automation, and associated technical documentation are available under the [MIT License](LICENSE).

Screenshots and demo videos under `media/`, compiled artifacts under `game-build/`, project names, and visual assets are excluded from the MIT grant. See [NOTICE](NOTICE) for details.
