# AGENTS.md

This file provides persistent project guidance for coding agents working in this repository.

## Project Overview

"清华园物语" (Qinghua Garden Story) is a university life simulation game. Architecture: Unity C# frontend (thin client) + Python FastAPI backend (all game state and logic live server-side). Current backend version: **v2.2**.

## Repository Layout

```
source-code/
  backend/          # Python FastAPI backend (4 files)
  csharp-client/    # Unity C# scripts (~130 .cs files + API/ and Editor/ subdirs)
game-build/         # Pre-built Windows executable (game.exe)
docs/               # Design docs: game-systems-design.md, frontend-api-guide.md, deployment-guide.md
media/              # Screenshots and videos
```

## Backend (Python/FastAPI)

Source: `source-code/backend/`

```powershell
# Setup
cd source-code/backend
python -m venv venv
.\venv\Scripts\activate
pip install -r requirements.txt

# Configure environment (copy and fill in DEEPSEEK_API_KEY and API_TOKEN)
# Create .env with:
#   DEEPSEEK_API_KEY=sk-xxx
#   API_TOKEN=thustory

# Run (listens on 0.0.0.0:8000)
python main.py

# Run tests
pytest

# API docs (while server is running): http://localhost:8000/docs
```

**Key files:**
- `main.py` — FastAPI app entry point; all REST endpoints and request/response models
- `database.py` — SQLite ORM layer (`qinghua_story.db`); all time constants defined here
- `activity_system.py` — Activity execution, penalty application, semester transition logic
- `npc_engine.py` — NPC dialogue generation via DeepSeek/OpenAI API

**Full API surface** (all routes require `X-Token` header except `GET /` and `GET /health`):
- System: `GET /`, `GET /health`
- Time: `GET /time`, `POST /time/pause`, `POST /time/resume`, `POST /time/advance?minutes=N`, `POST /time/nextday`
- Player: `GET /player`, `PATCH /player`
- Activities: `GET /activities`, `POST /activities/execute`
- NPC: `POST /chat`, `GET /npcs`, `GET /npcs/{npc_id}`
- Courses: `GET /courses/available`, `POST /courses/select`, `GET /courses/schedule`, `GET /courses/mine`, `POST /class/attend`
- Penalties (v2.2): `POST /player/penalties/curfew`, `POST /player/penalties/meals`
- Semester (v2.2): `POST /semester/transition`
- Social: `GET /social/orgs`, `POST /social/join`, `POST /social/promote`, `GET /social/status`
- Save/Endings: `GET /endings`, `POST /save/reset`, `GET /save/export`

**Key v2.2 behavioral rules:**
- `player.gpa` only updates at semester transition (not during the semester as mastery changes)
- Penalty endpoints (`/curfew`, `/meals`) are idempotent per game-day — safe to call multiple times
- `GET /courses/schedule` returns only the current semester's schedule (not historical)
- `POST /activities/execute` with `flags:["curfew_penalty"]` combines sleep + curfew penalty atomically

**Game time constants** (all in `database.py`):
- 0.9 real seconds = 1 game minute; day runs 6:30→1:00 (1110 minutes/day)
- Meal deadlines: breakfast 10:00, lunch 14:00, dinner 21:00
- 28 days/semester, 8 semesters total (大一上 → 大四下)

## Frontend (Unity C#)

Source: `source-code/csharp-client/`

Pre-built executable: `game-build/game.exe`

**Central gateway:** `API/APIManager.cs` — all HTTP calls go through here. Hard-coded backend URL: `http://39.105.203.179:8000`, auth header `X-Token: thustory`. On startup it auto-resumes the server clock.

**Module layout:**
- `API/` — Backend integration: `BackendModelsV21.cs` (request/response models), `ApiTransport.cs` (HTTP transport), `ServerActivityFlow.cs`, `ServerAttendClassFlow.cs`, `ServerPauseCoordinator.cs` (per-feature flow controllers), plus schedule/penalty/social utilities
- `Activities/` — Activity trigger and presentation: `ActivityTrigger.cs`, `ActivityBehaveTriggerMap.cs`, `ActivityPresentationUI.cs`, `SceneRActivityUI.cs`, `LibrarySelfStudyUI.cs`
- `Monitors/` — Ambient background monitors that poll server time: `ClassPeriodAutoAbsenceMonitor.cs`, `ClassPeriodNotifier.cs`, `LateNightCurfewMonitor.cs`, `LibraryHoursMonitor.cs`, `MealDeadlineCrossingDetector.cs`, `MealMissPenaltyMonitor.cs`, `DayEndSummaryMonitor.cs`
- `Courses/` — Academic UI and data: `CourseSelectionUI.cs`, `ScheduleViewUI.cs`, `SemesterTranscriptUI.cs`, `MyCoursesSnapshotCache.cs`
- `NPC/` — NPC and social systems: `NPCManager.cs`, `NPCInteraction.cs`, `FriendshipPersistence.cs`, `DialogueBox.cs`
- `Player/` — Player state and movement: `PlayerManager.cs`, `PlayerControl.cs`, `PlayerStatsSnapshot.cs`, `PlayerStatsText.cs`
- `HUD/` — Heads-up display and in-game overlays: `GameHUD.cs`, `GameTimeHUD.cs`, `DailySummaryUI.cs`, `DailyProgressBaseline.cs`, `BackendGameplayMenu.cs`, meal miss UI
- `Scene/` — Scene transitions, camera, and scene-level buttons: `SceneTransition.cs`, `PanelController.cs`, `FixedCameraSize.cs`, `FixedResolutionCamera.cs`, `PlayButton.cs`, `ExitButton.cs`, `GameTimeRestart.cs`
- `Utils/` — Utilities: `MathUtils.cs`, `ThustoryUIFont.cs`, `GetSpriteCorners.cs`
- `Editor/` — Unity editor utilities

## Architecture Notes

- **State is server-side.** The Unity client treats the FastAPI backend as the source of truth for all game state (time, player stats, activity progress, course schedule). Client-side objects (`MyCoursesSnapshotCache`, `PlayerStatsSnapshot`, etc.) are read-only snapshots.
- **Server time drives everything.** Monitors poll `GET /time` and fire penalties or UI events based on server-reported game time, not wall-clock time.
- **Penalty idempotency.** Both curfew and meal penalties use server-side logs (`penalty_log`, `meal_log` tables) to deduplicate — the client should call the penalty endpoint whenever the trigger condition is detected without worrying about double-application.
- **Semester transitions** are triggered automatically by activity execution when the semester boundary is crossed; `POST /semester/transition` exists for manual/compensating calls.
- **Energy/health zero events** use edge detection (only fires when value transitions from >0 to 0). Health zero takes priority over energy zero (hospitalisation skips 3 days; exhaustion skips 1). After either event both stats recover to 50.
- **NPC dialogue** is generated at runtime by `npc_engine.py` via DeepSeek/OpenAI; set `DEEPSEEK_API_KEY` in `.env`.
- **Production deployment** targets Aliyun ECS Ubuntu 22.04 with PM2 or systemd. See `docs/deployment-guide.md`. Upgrading from v2.1 requires dropping the old SQLite DB (schema is incompatible).
