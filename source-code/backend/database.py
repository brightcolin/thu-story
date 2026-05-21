"""
《清华园物语》数据库模块 v2.2
v2.1 → v2.2 改动：
  - 新增 meal_log 表：记录每餐是否已吃（按 day_block + meal_type 幂等）
  - 新增 penalty_log 表：晚归/缺餐惩罚幂等去重
  - player_state 新增 gpa_committed 字段（学期交割时写入的累计GPA）
  - player_courses / course_schedule 新增 semester_index 字段
  - get_player_schedule() 仅返回当前学期课表
  - semester_transition() 冻结旧课+计算GPA+清空课表+推进学期
  - get_player_state().gpa 取 gpa_committed（学期中不随掌握度变化）
"""
import sqlite3
import os
import json
import time as _time
from typing import Optional, Dict, List, Any, Tuple

DB_NAME = os.getenv("QINGHUA_DB_PATH", "qinghua_story.db")

# ═══ 时间常量 ═══
REAL_SECONDS_PER_GAME_MINUTE = 0.9
DAY_START_MINUTES = 6 * 60 + 30  # 390
MINUTES_PER_DAY = 1110
DAYS_PER_SEMESTER = 28
TOTAL_SEMESTERS = 8

SEMESTER_NAMES = ["大一上","大一下","大二上","大二下","大三上","大三下","大四上","大四下"]
WEEKDAY_NAMES = ["星期一","星期二","星期三","星期四","星期五","星期六","星期日"]

COURSE_PERIODS = {
    1: {"start_offset": 90,  "end_offset": 180, "label": "第一节(8:00-9:30)"},
    2: {"start_offset": 200, "end_offset": 340, "label": "第二节(9:50-12:10)"},
    3: {"start_offset": 420, "end_offset": 510, "label": "第三节(13:30-15:00)"},
    4: {"start_offset": 770, "end_offset": 870, "label": "第四节(19:20-21:00)"},
}

# ═══ 缺餐配置 ═══
MEAL_DEADLINES = {
    "breakfast": 210,   # 日内偏移(从6:30起) → 10:00
    "lunch":     450,   # → 14:00
    "dinner":    870,   # → 21:00
}
MEAL_MISS_PENALTY = {"energy": -15, "health": -10}  # 每漏一餐

# ═══ 晚归配置 ═══
CURFEW_PENALTY = {"energy": -30, "health": -20}

# ═══ 精力/健康归零配置 ═══
ENERGY_ZERO_SKIP_DAYS = 1
HEALTH_ZERO_SKIP_DAYS = 3
RECOVERY_ENERGY_AFTER_SKIP = 50
RECOVERY_HEALTH_AFTER_SKIP = 50
MESSAGE_FAINT = "你因过度疲劳昏睡过去，整整一天后才勉强恢复意识……"
MESSAGE_HOSPITAL = "你病倒了，卧床休养多日才有所好转。"


# ═══════════════════════════════════════════
# 实时时钟
# ═══════════════════════════════════════════

def get_current_game_minutes() -> float:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT total_game_minutes, last_active_ts, is_paused FROM player_state WHERE player_id='player'")
    row = c.fetchone(); conn.close()
    if not row: return 0.0
    stored, last_ts, is_paused = row
    if is_paused or last_ts is None: return stored
    return stored + max(0, _time.time() - last_ts) / REAL_SECONDS_PER_GAME_MINUTE

def _flush_game_minutes():
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT total_game_minutes, last_active_ts, is_paused FROM player_state WHERE player_id='player'")
    row = c.fetchone()
    if not row: conn.close(); return
    stored, last_ts, is_paused = row
    if not is_paused and last_ts is not None:
        stored += max(0, _time.time() - last_ts) / REAL_SECONDS_PER_GAME_MINUTE
    c.execute("UPDATE player_state SET total_game_minutes=?, last_active_ts=? WHERE player_id='player'",
              (stored, _time.time()))
    conn.commit(); conn.close()

def _is_paused() -> bool:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT is_paused FROM player_state WHERE player_id='player'")
    row = c.fetchone(); conn.close()
    return bool(row[0]) if row else True

# ═══════════════════════════════════════════
# 时间工具
# ═══════════════════════════════════════════

def time_info_from_minutes(tgm: float) -> Dict[str, Any]:
    total_days = int(tgm // MINUTES_PER_DAY)
    mid = int(tgm % MINUTES_PER_DAY)
    sem = min(TOTAL_SEMESTERS - 1, total_days // DAYS_PER_SEMESTER)
    dis = (total_days % DAYS_PER_SEMESTER) + 1
    week = (dis - 1) // 7 + 1
    wd = total_days % 7
    am = mid + DAY_START_MINUTES
    hour = (am // 60) % 24
    minute = am % 60
    over = total_days >= TOTAL_SEMESTERS * DAYS_PER_SEMESTER
    if hour < 12:   phase, pn = "Morning", "上午"
    elif hour < 17:  phase, pn = "Afternoon", "下午"
    elif hour < 21:  phase, pn = "Evening", "晚上"
    else:            phase, pn = "Night", "深夜"
    wcn = ["一","二","三","四"]
    return {
        "total_game_minutes": round(tgm, 1),
        "semester_index": sem,
        "semester_name": SEMESTER_NAMES[sem] if sem < len(SEMESTER_NAMES) else "已毕业",
        "week": week, "day_in_semester": dis,
        "weekday": wd, "weekday_name": WEEKDAY_NAMES[wd] if wd < 7 else "?",
        "hour": hour, "minute": minute,
        "phase": phase, "phase_name": pn,
        "is_game_over": over, "total_days_elapsed": total_days,
        "date_display": f"{SEMESTER_NAMES[sem]}学期第{wcn[min(week,4)-1]}周" if not over else "毕业",
        "time_display": f"{WEEKDAY_NAMES[wd]} {hour}:{minute:02d}" if not over else "",
    }

def get_day_block(tgm: float) -> int:
    """计算游戏日编号（从0开始的第几个游戏日）"""
    return int(tgm // MINUTES_PER_DAY)

def get_day_offset(tgm: float) -> int:
    """当前日内偏移（从6:30起算的分钟数）"""
    return int(tgm % MINUTES_PER_DAY)

def calc_time_jump_to(tgm: float, th: int, tm: int = 0) -> float:
    mid = int(tgm % MINUTES_PER_DAY)
    to = th * 60 + tm - DAY_START_MINUTES
    if to < 0: to += 24 * 60
    d = to - mid
    return d if d > 0 else d + MINUTES_PER_DAY

def calc_time_jump_next_day(tgm: float) -> float:
    return MINUTES_PER_DAY - int(tgm % MINUTES_PER_DAY)

# ═══════════════════════════════════════════
# 时间控制
# ═══════════════════════════════════════════

def pause_game():
    _flush_game_minutes()
    conn = get_connection(); c = conn.cursor()
    c.execute("UPDATE player_state SET is_paused=1, last_active_ts=NULL WHERE player_id='player'")
    conn.commit(); conn.close()

def resume_game():
    conn = get_connection(); c = conn.cursor()
    c.execute("UPDATE player_state SET is_paused=0, last_active_ts=? WHERE player_id='player'", (_time.time(),))
    conn.commit(); conn.close()

def add_game_minutes(minutes: float):
    _flush_game_minutes()
    conn = get_connection(); c = conn.cursor()
    c.execute("UPDATE player_state SET total_game_minutes = total_game_minutes + ? WHERE player_id='player'", (minutes,))
    conn.commit(); conn.close()

def set_game_minutes(minutes: float):
    conn = get_connection(); c = conn.cursor()
    c.execute("UPDATE player_state SET total_game_minutes=?, last_active_ts=? WHERE player_id='player'",
              (minutes, _time.time() if not _is_paused() else None))
    conn.commit(); conn.close()

# ═══════════════════════════════════════════
# 数据库初始化
# ═══════════════════════════════════════════

def init_db():
    conn = sqlite3.connect(DB_NAME); c = conn.cursor()

    c.execute("""CREATE TABLE IF NOT EXISTS player_state (
        player_id TEXT PRIMARY KEY DEFAULT 'player',
        total_game_minutes REAL DEFAULT 0,
        last_active_ts REAL DEFAULT NULL,
        is_paused INTEGER DEFAULT 1,
        energy INTEGER DEFAULT 60, health INTEGER DEFAULT 60,
        research_ability INTEGER DEFAULT 0, social_ability INTEGER DEFAULT 0,
        social_org TEXT DEFAULT NULL, social_rank TEXT DEFAULT NULL,
        srt_project INTEGER DEFAULT 0, lab_status TEXT DEFAULT 'none',
        failed_credits INTEGER DEFAULT 0,
        gpa_committed REAL DEFAULT 0.0
    )""")

    c.execute("""CREATE TABLE IF NOT EXISTS memory (
        id INTEGER PRIMARY KEY AUTOINCREMENT, npc_id TEXT NOT NULL,
        content TEXT NOT NULL, timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS npc_state (
        npc_id TEXT PRIMARY KEY, friendship INTEGER DEFAULT 0, valence REAL DEFAULT 0.0
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS courses (
        course_id TEXT PRIMARY KEY, course_name TEXT NOT NULL, credits INTEGER NOT NULL,
        course_type TEXT DEFAULT 'required', semester_index INTEGER DEFAULT 0,
        description TEXT DEFAULT '', attribute_bonus TEXT DEFAULT '{}'
    )""")

    # ★ v2.2: player_courses 增加 semester_index + mastery_final(学期末快照)
    c.execute("""CREATE TABLE IF NOT EXISTS player_courses (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        course_id TEXT NOT NULL,
        semester_index INTEGER NOT NULL DEFAULT 0,
        mastery REAL DEFAULT 50.0,
        mastery_final REAL DEFAULT NULL,
        attendance_count INTEGER DEFAULT 0,
        absence_count INTEGER DEFAULT 0,
        status TEXT DEFAULT 'active',
        FOREIGN KEY (course_id) REFERENCES courses(course_id)
    )""")

    # ★ v2.2: course_schedule 增加 semester_index
    c.execute("""CREATE TABLE IF NOT EXISTS course_schedule (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        course_id TEXT NOT NULL, semester_index INTEGER NOT NULL DEFAULT 0,
        day_of_week INTEGER NOT NULL, period INTEGER NOT NULL,
        FOREIGN KEY (course_id) REFERENCES courses(course_id)
    )""")

    c.execute("""CREATE TABLE IF NOT EXISTS activity_log (
        id INTEGER PRIMARY KEY AUTOINCREMENT, activity_id TEXT NOT NULL,
        npc_id TEXT, result TEXT, game_minutes_at REAL DEFAULT 0,
        timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS unlock_flags (
        flag_key TEXT PRIMARY KEY, unlocked INTEGER DEFAULT 0, unlocked_at DATETIME
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS tasks (
        task_id TEXT PRIMARY KEY, task_name TEXT NOT NULL, task_type TEXT DEFAULT 'side',
        description TEXT DEFAULT '', trigger_conditions TEXT DEFAULT '{}',
        objectives TEXT DEFAULT '{}', rewards TEXT DEFAULT '{}', next_task_id TEXT DEFAULT NULL
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS player_tasks (
        task_id TEXT PRIMARY KEY, status TEXT DEFAULT 'locked', progress TEXT DEFAULT '{}',
        accepted_at DATETIME, completed_at DATETIME,
        FOREIGN KEY (task_id) REFERENCES tasks(task_id)
    )""")
    c.execute("""CREATE TABLE IF NOT EXISTS gpa_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT, semester_index INTEGER NOT NULL,
        semester_gpa REAL NOT NULL, cumulative_gpa REAL NOT NULL, detail TEXT DEFAULT '{}'
    )""")

    # ★ v2.2: 用餐记录（按 day_block + meal_type 幂等）
    c.execute("""CREATE TABLE IF NOT EXISTS meal_log (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        day_block INTEGER NOT NULL,
        meal_type TEXT NOT NULL,
        eaten_at_game_minutes REAL DEFAULT 0,
        UNIQUE(day_block, meal_type)
    )""")

    # ★ v2.2: 惩罚记录（幂等去重）
    c.execute("""CREATE TABLE IF NOT EXISTS penalty_log (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        day_block INTEGER NOT NULL,
        penalty_type TEXT NOT NULL,
        detail TEXT DEFAULT '',
        applied_at DATETIME DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(day_block, penalty_type)
    )""")

    conn.commit(); conn.close()
    _ensure_player_exists(); _ensure_unlock_flags(); _seed_courses()
    _migrate_add_gpa_committed()

def _migrate_add_gpa_committed():
    """为旧库添加 gpa_committed 列（如果不存在）"""
    conn = get_connection(); c = conn.cursor()
    try:
        c.execute("SELECT gpa_committed FROM player_state LIMIT 1")
    except sqlite3.OperationalError:
        c.execute("ALTER TABLE player_state ADD COLUMN gpa_committed REAL DEFAULT 0.0")
        conn.commit()
    conn.close()

def get_connection():
    return sqlite3.connect(DB_NAME)

# ═══════════════════════════════════════════
# 玩家状态
# ═══════════════════════════════════════════

def _ensure_player_exists():
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT player_id FROM player_state WHERE player_id='player'")
    if not c.fetchone():
        c.execute("INSERT INTO player_state (player_id) VALUES ('player')")
        conn.commit()
    conn.close()

def get_player_state() -> dict:
    tgm = get_current_game_minutes()
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("SELECT * FROM player_state WHERE player_id='player'")
    row = c.fetchone(); conn.close()
    if not row: return {}
    d = dict(row)
    d["total_game_minutes"] = round(tgm, 1)
    # ★ v2.2: gpa 取 gpa_committed（学期中不随掌握度变化）
    d["gpa"] = d.get("gpa_committed", 0.0)
    ti = time_info_from_minutes(tgm)
    d.update({k: ti[k] for k in [
        "semester_index","semester_name","weekday","weekday_name",
        "hour","minute","phase","phase_name","is_game_over","date_display","time_display",
    ] if k in ti})
    d["current_week"] = ti["week"]
    d.pop("last_active_ts", None); d.pop("player_id", None)
    return d

def get_player_state_raw() -> dict:
    """获取数据库原始行（含 last_active_ts 等内部字段），用于内部逻辑"""
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("SELECT * FROM player_state WHERE player_id='player'")
    row = c.fetchone(); conn.close()
    return dict(row) if row else {}

STAT_LIMITS = {"energy": (0, 100), "health": (0, 100),
               "research_ability": (0, 100), "social_ability": (0, 100),
               "failed_credits": (0, 999)}
DIRECT_SET_FIELDS = {"total_game_minutes","last_active_ts","is_paused",
                     "social_org","social_rank","srt_project","lab_status","gpa_committed"}

def update_player_state(changes: dict) -> dict:
    if "total_game_minutes" in changes:
        _flush_game_minutes()
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("SELECT * FROM player_state WHERE player_id='player'")
    row = c.fetchone()
    if not row: conn.close(); return {}
    state = dict(row)
    updates = {}
    for key, delta in changes.items():
        if key in DIRECT_SET_FIELDS:
            updates[key] = delta
        elif key in STAT_LIMITS:
            lo, hi = STAT_LIMITS[key]
            cur = state.get(key, lo)
            updates[key] = max(lo, min(hi, (cur if isinstance(cur, (int, float)) else lo) + delta))
    if not updates: conn.close(); return get_player_state()
    sc = ", ".join(f"{k}=?" for k in updates)
    c.execute(f"UPDATE player_state SET {sc} WHERE player_id='player'", list(updates.values()))
    conn.commit(); conn.close()
    return get_player_state()

def update_player_state_absolute(changes: dict) -> dict:
    """绝对值赋值（非增量），用于惩罚后精确设定 energy/health"""
    conn = get_connection(); c = conn.cursor()
    updates = {}
    for key, val in changes.items():
        if key in STAT_LIMITS:
            lo, hi = STAT_LIMITS[key]
            updates[key] = max(lo, min(hi, val))
        elif key in DIRECT_SET_FIELDS:
            updates[key] = val
    if not updates: conn.close(); return get_player_state()
    sc = ", ".join(f"{k}=?" for k in updates)
    c.execute(f"UPDATE player_state SET {sc} WHERE player_id='player'", list(updates.values()))
    conn.commit(); conn.close()
    return get_player_state()

# ═══════════════════════════════════════════
# GPA
# ═══════════════════════════════════════════

def mastery_to_grade_point(m: float) -> float:
    if m >= 95: return 4.0
    if m >= 90: return 3.7
    if m >= 85: return 3.3
    if m >= 80: return 3.0
    if m >= 75: return 2.7
    if m >= 70: return 2.3
    if m >= 65: return 2.0
    if m >= 60: return 1.5
    return 0.0

def _calc_cumulative_gpa() -> float:
    """内部用：计算所有已选课（含历史）的加权GPA"""
    conn = get_connection(); c = conn.cursor()
    c.execute("""SELECT COALESCE(pc.mastery_final, pc.mastery) as m, co.credits
                 FROM player_courses pc JOIN courses co ON pc.course_id=co.course_id""")
    rows = c.fetchall(); conn.close()
    if not rows: return 0.0
    tp = sum(mastery_to_grade_point(m) * cr for m, cr in rows)
    tc = sum(cr for _, cr in rows)
    return round(tp / tc, 2) if tc > 0 else 0.0

def get_current_gpa() -> float:
    """对外：返回 gpa_committed"""
    raw = get_player_state_raw()
    return raw.get("gpa_committed", 0.0)

# ═══════════════════════════════════════════
# 学期交割
# ═══════════════════════════════════════════

def semester_transition(old_sem: int) -> Tuple[float, dict]:
    """
    学期交割：冻结旧课 → 重算累计GPA → 清空旧课表 → 写入gpa_committed
    返回 (new_cumulative_gpa, detail_dict)
    """
    conn = get_connection(); c = conn.cursor()

    # 1. 冻结上学期课程：写入 mastery_final，标记 status='finished'
    c.execute("""UPDATE player_courses SET mastery_final = mastery, status = 'finished'
                 WHERE semester_index = ? AND status = 'active'""", (old_sem,))

    # 2. 计算不及格学分
    c.execute("""SELECT COALESCE(pc.mastery_final, pc.mastery) as m, co.credits
                 FROM player_courses pc JOIN courses co ON pc.course_id = co.course_id
                 WHERE pc.semester_index = ?""", (old_sem,))
    rows = c.fetchall()
    failed = sum(cr for m, cr in rows if mastery_to_grade_point(m) == 0.0)
    if failed > 0:
        c.execute("UPDATE player_state SET failed_credits = failed_credits + ? WHERE player_id='player'", (failed,))

    # 3. 重算累计GPA（含所有历史课程）
    c.execute("""SELECT COALESCE(pc.mastery_final, pc.mastery) as m, co.credits, co.course_name
                 FROM player_courses pc JOIN courses co ON pc.course_id = co.course_id""")
    all_rows = c.fetchall()
    detail = {}
    tp = 0.0; tc = 0
    for m, cr, cname in all_rows:
        gp = mastery_to_grade_point(m)
        detail[cname] = {"mastery": m, "grade_point": gp, "credits": cr}
        tp += gp * cr; tc += cr
    cum_gpa = round(tp / tc, 2) if tc > 0 else 0.0

    # 4. 写入 gpa_committed
    c.execute("UPDATE player_state SET gpa_committed = ? WHERE player_id='player'", (cum_gpa,))

    # 5. 清空上学期课表 slot
    c.execute("DELETE FROM course_schedule WHERE semester_index = ?", (old_sem,))

    # 6. 记录 GPA 历史
    # 计算本学期单独GPA
    sem_rows = [(m, cr) for m, cr, _ in all_rows]  # 简化：用全量
    c.execute("""SELECT COALESCE(pc.mastery_final, pc.mastery) as m, co.credits
                 FROM player_courses pc JOIN courses co ON pc.course_id = co.course_id
                 WHERE pc.semester_index = ?""", (old_sem,))
    sem_only = c.fetchall()
    sem_tp = sum(mastery_to_grade_point(m) * cr for m, cr in sem_only)
    sem_tc = sum(cr for _, cr in sem_only)
    sem_gpa = round(sem_tp / sem_tc, 2) if sem_tc > 0 else 0.0

    c.execute("INSERT OR REPLACE INTO gpa_history (semester_index, semester_gpa, cumulative_gpa, detail) VALUES (?,?,?,?)",
              (old_sem, sem_gpa, cum_gpa, json.dumps(detail, ensure_ascii=False)))

    conn.commit(); conn.close()
    return cum_gpa, detail

# ═══════════════════════════════════════════
# NPC
# ═══════════════════════════════════════════

def get_npc_state(npc_id: str) -> Tuple[int, float]:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT friendship, valence FROM npc_state WHERE npc_id=?", (npc_id,))
    row = c.fetchone(); conn.close()
    if row: return row[0], row[1]
    return (20, 0.0) if npc_id == "li_juan" else (0, 0.0)

def update_npc_state(npc_id: str, fc: int, vc: float = 0.0) -> int:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT friendship, valence FROM npc_state WHERE npc_id=?", (npc_id,))
    row = c.fetchone()
    if row:
        nf = max(0, min(100, row[0] + fc)); nv = row[1] + vc
        c.execute("UPDATE npc_state SET friendship=?, valence=? WHERE npc_id=?", (nf, nv, npc_id))
    else:
        init_f = 20 if npc_id == "li_juan" else 0
        nf = max(0, min(100, init_f + fc)); nv = vc
        c.execute("INSERT INTO npc_state (npc_id, friendship, valence) VALUES (?,?,?)", (npc_id, nf, nv))
    conn.commit(); conn.close(); return nf

def get_all_npc_friendships() -> dict:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT npc_id, friendship FROM npc_state")
    rows = c.fetchall(); conn.close()
    return {r[0]: r[1] for r in rows}

def get_recent_memory(npc_id: str, limit: int = 5) -> str:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT content FROM memory WHERE npc_id=? ORDER BY id DESC LIMIT ?", (npc_id, limit))
    rows = c.fetchall(); conn.close()
    return "\n".join([r[0] for r in reversed(rows)]) or ""

def add_memory(npc_id: str, content: str):
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT COUNT(*) FROM memory WHERE npc_id=?", (npc_id,))
    cnt = c.fetchone()[0]
    if cnt >= 30:
        c.execute("DELETE FROM memory WHERE id IN (SELECT id FROM memory WHERE npc_id=? ORDER BY id ASC LIMIT ?)", (npc_id, cnt - 29))
    c.execute("INSERT INTO memory (npc_id, content) VALUES (?,?)", (npc_id, content))
    conn.commit(); conn.close()

# ═══════════════════════════════════════════
# 课程系统（v2.2: semester-scoped）
# ═══════════════════════════════════════════

def _seed_courses():
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT COUNT(*) FROM courses")
    if c.fetchone()[0] > 0: conn.close(); return
    seed = [
        ("math_analysis_1","数学分析(1)",5,"required",0,"高等数学基础","{}"),
        ("linear_algebra","线性代数",4,"required",0,"矩阵与线性空间","{}"),
        ("programming_c","程序设计基础",3,"required",0,"C语言编程入门","{}"),
        ("physics_1","大学物理(1)",4,"required",0,"力学与热学","{}"),
        ("math_analysis_2","数学分析(2)",5,"required",1,"多元微积分","{}"),
        ("circuit_theory","电路原理",4,"required",1,"基础电路分析","{}"),
        ("physics_2","大学物理(2)",4,"required",1,"电磁学与光学","{}"),
        ("signals_systems","信号与系统",4,"required",2,"连续与离散信号","{}"),
        ("analog_circuits","模拟电子技术",4,"required",2,"模电基础","{}"),
        ("probability","概率论与数理统计",3,"required",2,"随机过程基础","{}"),
        ("digital_circuits","数字电子技术",4,"required",3,"数字逻辑设计","{}"),
        ("electromagnetic","电磁场与波",3,"required",3,"麦克斯韦方程组","{}"),
        ("dsp","数字信号处理",3,"required",3,"FFT、滤波器","{}"),
        ("communication","通信原理",3,"required",4,"调制解调、信道编码","{}"),
        ("microcontroller","微处理器系统",3,"required",4,"嵌入式开发","{}"),
        ("machine_learning","机器学习概论",3,"required",5,"ML基础算法","{}"),
        ("vlsi_design","VLSI设计",3,"required",5,"芯片设计基础","{}"),
        ("opera_art","京剧艺术鉴赏",2,"elective",-1,"传统文化选修",json.dumps({"npc_bonus":{"zhao_xiao":2}},ensure_ascii=False)),
        ("garden_culture","中国古典园林",2,"elective",-1,"健康恢复效率+10%",json.dumps({"health_recovery_bonus":0.1},ensure_ascii=False)),
        ("startup_intro","创业导引",2,"elective",-1,"提升社工能力",json.dumps({"social_ability_bonus":5},ensure_ascii=False)),
        ("music_emotion","音乐与情感表达",2,"elective",-1,"提升沈星辞好感",json.dumps({"npc_bonus":{"shen_xingci":2}},ensure_ascii=False)),
    ]
    c.executemany("INSERT INTO courses VALUES (?,?,?,?,?,?,?)", seed)
    conn.commit(); conn.close()

def get_courses_for_semester(si: int) -> List[dict]:
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("SELECT * FROM courses WHERE semester_index=? OR (course_type='elective' AND semester_index=-1)", (si,))
    rows = c.fetchall(); conn.close(); return [dict(r) for r in rows]

def select_course(course_id: str, schedule: List[dict], semester_index: int = None) -> bool:
    """选课，semester_index 默认取当前学期"""
    if semester_index is None:
        ti = time_info_from_minutes(get_current_game_minutes())
        semester_index = ti["semester_index"]
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT course_id FROM courses WHERE course_id=?", (course_id,))
    if not c.fetchone(): conn.close(); return False
    # 检查是否已在本学期选过
    c.execute("SELECT id FROM player_courses WHERE course_id=? AND semester_index=? AND status='active'",
              (course_id, semester_index))
    if c.fetchone(): conn.close(); return True  # 幂等
    c.execute("INSERT INTO player_courses (course_id, semester_index) VALUES (?,?)", (course_id, semester_index))
    for s in schedule:
        c.execute("INSERT INTO course_schedule (course_id, semester_index, day_of_week, period) VALUES (?,?,?,?)",
                  (course_id, semester_index, s["day_of_week"], s["period"]))
    conn.commit(); conn.close(); return True

def get_player_schedule(semester_index: int = None) -> List[dict]:
    """★ v2.2: 仅返回指定学期（默认当前学期）的课表"""
    if semester_index is None:
        ti = time_info_from_minutes(get_current_game_minutes())
        semester_index = ti["semester_index"]
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("""SELECT cs.day_of_week, cs.period, cs.course_id, co.course_name, co.credits
                 FROM course_schedule cs JOIN courses co ON cs.course_id=co.course_id
                 WHERE cs.semester_index=?
                 ORDER BY cs.day_of_week, cs.period""", (semester_index,))
    rows = c.fetchall(); conn.close(); return [dict(r) for r in rows]

def get_today_courses(weekday: int) -> List[dict]:
    """仅返回当前学期今天的课"""
    ti = time_info_from_minutes(get_current_game_minutes())
    sem = ti["semester_index"]
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    c.execute("""SELECT cs.period, cs.course_id, co.course_name, co.credits
                 FROM course_schedule cs JOIN courses co ON cs.course_id=co.course_id
                 WHERE cs.day_of_week=? AND cs.semester_index=?
                 ORDER BY cs.period""", (weekday, sem))
    rows = c.fetchall(); conn.close(); return [dict(r) for r in rows]

def update_course_mastery(course_id: str, delta: float):
    """更新当前学期活跃课程的掌握度"""
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT id, mastery FROM player_courses WHERE course_id=? AND status='active' ORDER BY id DESC LIMIT 1", (course_id,))
    row = c.fetchone()
    if row:
        c.execute("UPDATE player_courses SET mastery=? WHERE id=?", (max(0, min(100, row[1]+delta)), row[0]))
    conn.commit(); conn.close()

def record_attendance(course_id: str, attended: bool):
    conn = get_connection(); c = conn.cursor()
    col = "attendance_count" if attended else "absence_count"
    c.execute(f"UPDATE player_courses SET {col}={col}+1 WHERE course_id=? AND status='active'", (course_id,))
    conn.commit(); conn.close()

def get_player_courses(semester_index: int = None) -> List[dict]:
    """获取指定学期课程（默认当前学期活跃课程）"""
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    if semester_index is not None:
        c.execute("""SELECT pc.course_id, co.course_name, co.credits,
                            COALESCE(pc.mastery_final, pc.mastery) as mastery,
                            pc.attendance_count, pc.absence_count, pc.status, pc.semester_index
                     FROM player_courses pc JOIN courses co ON pc.course_id=co.course_id
                     WHERE pc.semester_index=?""", (semester_index,))
    else:
        c.execute("""SELECT pc.course_id, co.course_name, co.credits, pc.mastery,
                            pc.attendance_count, pc.absence_count, pc.status, pc.semester_index
                     FROM player_courses pc JOIN courses co ON pc.course_id=co.course_id
                     WHERE pc.status='active'""")
    rows = c.fetchall(); conn.close(); return [dict(r) for r in rows]

# ═══════════════════════════════════════════
# 用餐记录（v2.2 新增）
# ═══════════════════════════════════════════

def mark_meal_eaten(day_block: int, meal_type: str, game_minutes: float = 0):
    """标记某餐已吃（幂等）"""
    conn = get_connection(); c = conn.cursor()
    c.execute("INSERT OR IGNORE INTO meal_log (day_block, meal_type, eaten_at_game_minutes) VALUES (?,?,?)",
              (day_block, meal_type, game_minutes))
    conn.commit(); conn.close()

def is_meal_eaten(day_block: int, meal_type: str) -> bool:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT id FROM meal_log WHERE day_block=? AND meal_type=?", (day_block, meal_type))
    row = c.fetchone(); conn.close()
    return row is not None

def get_current_meal_type(tgm: float) -> Optional[str]:
    """根据当前时间判断应该吃哪一餐（用于 eat_canteen 自动标记）"""
    offset = get_day_offset(tgm)
    if offset < MEAL_DEADLINES["breakfast"]:
        return "breakfast"
    elif offset < MEAL_DEADLINES["lunch"]:
        return "lunch"
    elif offset < MEAL_DEADLINES["dinner"]:
        return "dinner"
    return None  # 已过所有餐点

# ═══════════════════════════════════════════
# 惩罚幂等记录（v2.2 新增）
# ═══════════════════════════════════════════

def record_penalty(day_block: int, penalty_type: str, detail: str = "") -> bool:
    """记录惩罚（幂等），返回 True=首次记录，False=已存在"""
    conn = get_connection(); c = conn.cursor()
    try:
        c.execute("INSERT INTO penalty_log (day_block, penalty_type, detail) VALUES (?,?,?)",
                  (day_block, penalty_type, detail))
        conn.commit(); conn.close(); return True
    except sqlite3.IntegrityError:
        conn.close(); return False

def is_penalty_applied(day_block: int, penalty_type: str) -> bool:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT id FROM penalty_log WHERE day_block=? AND penalty_type=?", (day_block, penalty_type))
    row = c.fetchone(); conn.close()
    return row is not None

# ═══════════════════════════════════════════
# 解锁 / 日志 / 任务 / 结局 / 重置
# ═══════════════════════════════════════════

UNLOCK_KEYS = ["lab_access","club_joined","research_project","boyfriend_unlocked",
               "mentor_close","srt_unlocked","social_org_joined","internship_unlocked"]

def _ensure_unlock_flags():
    conn = get_connection(); c = conn.cursor()
    for k in UNLOCK_KEYS: c.execute("INSERT OR IGNORE INTO unlock_flags (flag_key) VALUES (?)", (k,))
    conn.commit(); conn.close()

def get_unlock_flag(k: str) -> bool:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT unlocked FROM unlock_flags WHERE flag_key=?", (k,))
    row = c.fetchone(); conn.close(); return bool(row[0]) if row else False

def set_unlock_flag(k: str, v: bool = True):
    conn = get_connection(); c = conn.cursor()
    c.execute("INSERT INTO unlock_flags (flag_key,unlocked,unlocked_at) VALUES (?,?,CURRENT_TIMESTAMP) ON CONFLICT(flag_key) DO UPDATE SET unlocked=?,unlocked_at=CURRENT_TIMESTAMP",
              (k, int(v), int(v)))
    conn.commit(); conn.close()

def get_all_unlock_flags() -> dict:
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT flag_key, unlocked FROM unlock_flags")
    rows = c.fetchall(); conn.close(); return {r[0]: bool(r[1]) for r in rows}

def log_activity(aid: str, nid: str = None, result: str = "", gm: float = 0):
    conn = get_connection(); c = conn.cursor()
    c.execute("INSERT INTO activity_log (activity_id, npc_id, result, game_minutes_at) VALUES (?,?,?,?)", (aid, nid, result, gm))
    conn.commit(); conn.close()

def get_today_activity_count(aid: str, tgm: float = 0) -> int:
    ds = (int(tgm) // MINUTES_PER_DAY) * MINUTES_PER_DAY; de = ds + MINUTES_PER_DAY
    conn = get_connection(); c = conn.cursor()
    c.execute("SELECT COUNT(*) FROM activity_log WHERE activity_id=? AND game_minutes_at>=? AND game_minutes_at<?", (aid, ds, de))
    cnt = c.fetchone()[0]; conn.close(); return cnt

def get_player_tasks(status: str = None) -> List[dict]:
    conn = get_connection(); conn.row_factory = sqlite3.Row; c = conn.cursor()
    if status:
        c.execute("SELECT pt.*, t.task_name, t.task_type, t.description, t.rewards FROM player_tasks pt JOIN tasks t ON pt.task_id=t.task_id WHERE pt.status=?", (status,))
    else:
        c.execute("SELECT pt.*, t.task_name, t.task_type, t.description, t.rewards FROM player_tasks pt JOIN tasks t ON pt.task_id=t.task_id")
    rows = c.fetchall(); conn.close(); return [dict(r) for r in rows]

def check_endings() -> List[dict]:
    state = get_player_state()
    gpa=state.get("gpa",0.0); res=state.get("research_ability",0)
    soc=state.get("social_ability",0); fc=state.get("failed_credits",0)
    ends = []
    if fc >= 20: return [{"id":"dropout","name":"退学","available":True,"forced":True}]
    if gpa>=3.7 and res>=70: ends.append({"id":"phd_direct","name":"保研直博","available":True})
    if gpa>=3.8 and res>=80 and soc>=40: ends.append({"id":"study_abroad","name":"出国留学","available":True})
    if soc>=80 and gpa>=3.5 and res>=50: ends.append({"id":"startup","name":"自主创业","available":True})
    if gpa>=3.3 and res>=40: ends.append({"id":"external_grad","name":"外校保研","available":True})
    if gpa>=2.5: ends.append({"id":"exam_grad","name":"考研成功","available":True})
    ends.append({"id":"flexible_job","name":"灵活就业","available":True})
    return ends

def reset_save():
    conn = get_connection(); c = conn.cursor()
    for t in ["memory","npc_state","player_state","activity_log","player_courses",
              "course_schedule","player_tasks","gpa_history","meal_log","penalty_log"]:
        c.execute(f"DELETE FROM {t}")
    c.execute("UPDATE unlock_flags SET unlocked=0, unlocked_at=NULL")
    conn.commit(); conn.close(); _ensure_player_exists()
