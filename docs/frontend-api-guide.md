# 前端对接说明 v2.2

> 所有请求需带请求头 `X-Token: <API_TOKEN>`（除 `GET /` 和 `GET /health`）
> Content-Type: `application/json`
> 本地基地址示例：`http://localhost:8000`

---

## 一、接口清单（按模块）

### 1. 系统

| 方法 | 路径 | 说明 | 鉴权 |
|------|------|------|------|
| GET | `/` | 服务状态 | 否 |
| GET | `/health` | 健康检查 | 否 |

### 2. 时间系统

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/time` | 获取当前游戏时间 |
| POST | `/time/pause` | 暂停游戏时钟 |
| POST | `/time/resume` | 恢复游戏时钟 |
| POST | `/time/advance?minutes=N` | 推进N分钟（调试） |
| POST | `/time/nextday` | 跳到第二天6:30 |

### 3. 玩家状态

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/player` | 获取完整玩家状态 |
| PATCH | `/player` | 修改属性（调试用） |

### 4. 活动系统

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/activities` | 获取可用活动列表 |
| POST | `/activities/execute` | 执行活动 |

### 5. NPC 对话

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/chat` | 与NPC对话 |
| GET | `/npcs` | NPC列表 |
| GET | `/npcs/{npc_id}` | NPC详情 |

### 6. 课程系统

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/courses/available` | 当前学期可选课程 |
| POST | `/courses/select` | 选课 |
| GET | `/courses/schedule` | 当前学期课表（★v2.2: 仅当前学期） |
| GET | `/courses/mine` | 已选课程+掌握度 |
| POST | `/class/attend` | 上课 |

### 7. 惩罚系统（★v2.2 新增）

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/player/penalties/curfew` | 晚归惩罚（幂等） |
| POST | `/player/penalties/meals` | 缺餐惩罚（幂等） |

### 8. 学期系统（★v2.2 新增）

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/semester/transition` | 手动学期交割 |

### 9. 社工系统

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/social/orgs` | 组织列表 |
| POST | `/social/join` | 加入组织 |
| POST | `/social/promote` | 尝试晋升 |
| GET | `/social/status` | 社工状态 |

### 10. 结局 / 存档

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/endings` | 可达成结局 |
| POST | `/save/reset` | 重置存档 |
| GET | `/save/export` | 导出存档 |

---

## 二、各接口详细规格

### POST /time/advance

调试接口，`minutes` 必须为 `1` 到 `1110` 之间的整数。正常活动流程会自行推进时间，前端不应在活动完成后重复调用。

### PATCH /player

调试接口，按绝对值设置玩家属性，而不是应用增量。支持字段为 `energy`、`health`、`research_ability`、`social_ability`，取值范围均为 `0` 到 `100`。

```json
{"energy": 60, "health": 80}
```

### POST /courses/select

`schedule` 至少包含一个时段；`day_of_week` 范围为 `0..6`，`period` 范围为 `1..4`。同一请求不能包含重复时段，同一学期也不能把两门课程安排在相同时段。只能选择当前学期课程或通用选修课。

### POST /activities/execute（★v2.2 更新：支持 flags）

**请求体**：
```json
{
  "activity_id": "sleep",
  "course_id": null,
  "flags": ["curfew_penalty"]
}
```
- `flags` 可选。包含 `"curfew_penalty"` 时，活动结算后追加晚归惩罚（精力-30、健康-20），幂等。

**成功响应 200**：
```json
{
  "success": true,
  "activity_name": "睡觉",
  "npc_id": null,
  "effect_applied": {"energy": 20, "health": 0},
  "time_advanced_minutes": 600,
  "new_time": {
    "date_display": "大一上学期第一周",
    "time_display": "星期二 6:30",
    "hour": 6, "minute": 30
  },
  "new_state": { "energy": 70, "health": 60, "gpa": 0.0, ... },
  "newly_unlocked": [],
  "events": [
    {"type": "faint", "message": "你因过度疲劳昏睡过去……"}
  ],
  "semester_gpa_result": null
}
```

**`events[].type` 值说明**：

| type | 含义 | 前端展示建议 |
|------|------|-------------|
| `faint` | 精力归零，跳过1个游戏日 | 弹窗7秒 |
| `hospital` | 健康归零，跳过3个游戏日 | 弹窗7秒 |
| `energy_warning` | 精力≤30 | 短提示 |
| `health_warning` | 健康≤30 | 短提示 |

**失败响应 400**：
```json
{"detail": "精力不足，需要至少20点精力"}
```

---

### POST /player/penalties/curfew（★v2.2 新增）

**请求体**：无（空 POST）

**成功响应 200**：
```json
{
  "success": true,
  "applied": true,
  "player": { "energy": 30, "health": 40, ... },
  "events": []
}
```
- `applied: false` 表示本日已处理过（幂等），不会重复扣罚。
- `events` 可能包含 `faint`/`hospital`（如果扣罚导致归零）。

**前端调用时机**：宵禁触发并强制入睡后调用。**不需要**再额外 `PATCH /player`。

---

### POST /player/penalties/meals（★v2.2 新增）

**请求体**：无（空 POST）

服务端根据当前游戏时间自动判定哪些餐已错过且未吃。

**成功响应 200**：
```json
{
  "success": true,
  "applied": true,
  "missed_meals": ["breakfast", "lunch"],
  "energy_delta": -30,
  "health_delta": -20,
  "player": { "energy": 30, "health": 40, ... },
  "events": []
}
```
- `applied: false` 且 `missed_meals: []` 表示无需扣罚（已吃或已处理过）。

**前端调用时机**：
- 方案A：游戏时间跨过每个餐段截止点时调用
- 方案B：定时轮询（如每30秒）
- 前端**不需要**自行判断缺餐逻辑，只需发空POST请求

**餐段截止时间**（与客户端 `MealDeadlineMonitor` 一致）：

| 餐段 | 截止偏移(从6:30起) | 对应时刻 | 漏餐罚 |
|------|-------------------|----------|--------|
| breakfast | 210 | 10:00 | 精力-15,健康-10 |
| lunch | 450 | 14:00 | 精力-15,健康-10 |
| dinner | 870 | 21:00 | 精力-15,健康-10 |

---

### POST /semester/transition（★v2.2 新增）

**请求体**：无

**成功响应 200**：
```json
{
  "success": true,
  "semester_settled": 0,
  "cumulative_gpa": 2.85,
  "detail": {
    "数学分析(1)": {"mastery": 82, "grade_point": 3.0, "credits": 5},
    "线性代数": {"mastery": 90, "grade_point": 3.7, "credits": 4}
  },
  "player": { "gpa": 2.85, ... }
}
```

**说明**：
- 冻结上学期课程 → 计算累计GPA → 清空上学期课表 → 写入 `gpa_committed`
- 交割后 `GET /courses/schedule` 返回空数组（新学期无课）
- 交割后 `GET /player` 的 `gpa` 为更新后的累计GPA
- 通常由活动系统中的时间推进自动触发；手动调用只会处理最早一个已经结束且尚未交割的学期
- 没有待交割学期时返回 `409`；底层交割逻辑按学期幂等，不会重复累计挂科学分

---

### GET /courses/schedule（★v2.2 行为变更）

**响应**：仅包含**当前学期** `semester_index` 的课表，不含历史学期。

```json
{
  "schedule": [
    {"day_of_week": 0, "period": 1, "course_id": "math_analysis_1", "course_name": "数学分析(1)", "credits": 5}
  ]
}
```

学期交割后此接口返回空数组，直到新学期选课。

---

### POST /class/attend（★v2.2 更新：新增 events 字段）

**响应** 现在包含 `events` 字段：
```json
{
  "success": true,
  "course_name": "数学分析(1)",
  "attendance_status": "on_time",
  "mastery_delta": 5,
  "time_advanced_minutes": 90,
  "new_time": {"date_display": "...", "time_display": "..."},
  "events": []
}
```

---

### GET /player（★v2.2 行为变更）

`player.gpa` 现在是**已交割的累计GPA**（仅在学期交割时更新），学期中不随课程掌握度变化。

```json
{
  "player": {
    "energy": 60, "health": 60,
    "gpa": 2.85,
    "gpa_committed": 2.85,
    "research_ability": 0, "social_ability": 0,
    "semester_index": 1, "semester_name": "大一下",
    "total_game_minutes": 31000.0,
    ...
  },
  "friendships": {"chen_yiran": 15, ...},
  "unlocks": {"lab_access": false, ...},
  "courses": [
    {"course_id": "circuit_theory", "course_name": "电路原理", "credits": 4, "mastery": 55.0, ...}
  ]
}
```

---

## 三、关键流程时序

### 3.1 宵禁晚归流程

```
客户端检测到宵禁触发
    ↓
POST /activities/execute  {"activity_id":"sleep", "flags":["curfew_penalty"]}
    ↓ (后端: 执行睡觉 + 在同一事务内扣除晚归惩罚 + 检查归零)
    ↓
用 response.new_state 更新 HUD
用 response.events 弹窗（如 faint/hospital）
    ↓
不需要再 PATCH /player ✅
```

### 3.2 缺餐检测流程

```
客户端检测到时间跨过餐点（或定时轮询）
    ↓
POST /player/penalties/meals  （空POST）
    ↓ (后端: 根据服务端时间判定缺哪餐，按餐扣罚，幂等)
    ↓
if response.applied:
    用 response.player 更新 HUD
    用 response.events 弹窗
    ↓
不需要再 PATCH /player ✅
```

### 3.3 学期切换流程

```
活动/时间推进导致 semester_index 变化
    ↓ (后端自动调用 semester_transition)
    ↓
response.semester_gpa_result 包含交割信息
    ↓
前端弹出成绩单UI，显示 semester_gpa_result.detail
    ↓
GET /courses/schedule 返回空（新学期无课）
    ↓
弹出选课界面，POST /courses/select 选新课
```

### 3.4 精力/健康归零流程

```
任意活动/惩罚导致 energy=0 或 health=0
    ↓ (后端自动检测: 边沿检测，健康优先)
    ↓
health 归零: 跳过3个游戏日(+3330分钟)，精健恢复到50
energy 归零: 跳过1个游戏日(+1110分钟)，精健恢复到50
    ↓
response.events 包含 {"type":"hospital"} 或 {"type":"faint"}
response.new_state 和 new_time 已反映跳跃后的状态
    ↓
前端弹窗展示 event.message（建议显示7秒）
用 new_state 更新所有 HUD
```

---

## 四、eat_canteen 自动标记用餐

执行 `POST /activities/execute {"activity_id":"eat_canteen"}` 时，后端会自动根据当前游戏时间判断是早餐/午餐/晚餐，并标记该餐已吃。后续 `POST /player/penalties/meals` 不会对已吃的餐再扣罚。

前端**无需**额外调用任何接口来标记用餐。
