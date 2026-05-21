"""
《清华园物语》活动系统 v2.2
v2.1 → v2.2 改动：
  - _check_health_energy_events: 边沿检测(prev>0→0)，健康优先，+N*1110跳跃，恢复值可配
  - eat_canteen 自动标记当前餐段已吃
  - 新增 apply_curfew_penalty() / apply_missed_meals_penalty() 幂等惩罚
  - 学期切换时调用 semester_transition() 冻结课表+交割GPA
"""
from dataclasses import dataclass
from typing import Optional, List, Dict, Any
from database import (
    get_player_state, update_player_state, update_player_state_absolute,
    get_player_state_raw,
    get_npc_state, get_unlock_flag, set_unlock_flag,
    log_activity, get_today_activity_count, get_all_npc_friendships,
    get_current_game_minutes, time_info_from_minutes,
    calc_time_jump_to, calc_time_jump_next_day,
    add_game_minutes, pause_game, resume_game,
    get_today_courses, update_course_mastery, record_attendance,
    get_all_unlock_flags, MINUTES_PER_DAY, COURSE_PERIODS,
    check_endings, semester_transition, get_day_block, get_day_offset,
    mark_meal_eaten, is_meal_eaten, get_current_meal_type,
    record_penalty, is_penalty_applied,
    CURFEW_PENALTY, MEAL_MISS_PENALTY, MEAL_DEADLINES,
    ENERGY_ZERO_SKIP_DAYS, HEALTH_ZERO_SKIP_DAYS,
    RECOVERY_ENERGY_AFTER_SKIP, RECOVERY_HEALTH_AFTER_SKIP,
    MESSAGE_FAINT, MESSAGE_HOSPITAL,
)


@dataclass
class ActivityEffect:
    energy: int = 0
    health: int = 0
    research_ability: int = 0
    social_ability: int = 0

@dataclass
class Activity:
    id: str
    name: str
    description: str
    effect: ActivityEffect
    time_jump_type: str = "fixed"
    time_cost_minutes: int = 60
    jump_target_hour: int = 0
    jump_target_minute: int = 0
    hour_range: Optional[tuple] = None
    npc_id: Optional[str] = None
    requires_unlock: Optional[str] = None
    daily_limit: int = 99
    min_energy: int = 0


ACTIVITIES: Dict[str, Activity] = {
    "study_library_morning": Activity(
        id="study_library_morning", name="图书馆自习(上午场)", description="去图书馆刷题，到12:20结束",
        effect=ActivityEffect(energy=-15),
        time_jump_type="jump_to", jump_target_hour=12, jump_target_minute=20,
        hour_range=(6, 12), min_energy=20),
    "study_library_afternoon": Activity(
        id="study_library_afternoon", name="图书馆自习(下午场)", description="去图书馆自习，到17:00结束",
        effect=ActivityEffect(energy=-15),
        time_jump_type="jump_to", jump_target_hour=17, jump_target_minute=0,
        hour_range=(12, 17), min_energy=20),
    "study_library_evening": Activity(
        id="study_library_evening", name="图书馆自习(晚上场)", description="去图书馆自习，到22:00结束",
        effect=ActivityEffect(energy=-15),
        time_jump_type="jump_to", jump_target_hour=22, jump_target_minute=0,
        hour_range=(17, 22), min_energy=20),
    "research_morning": Activity(
        id="research_morning", name="做实验(上午场)", description="在实验室做科研，到12:20",
        effect=ActivityEffect(energy=-20, research_ability=2),
        time_jump_type="jump_to", jump_target_hour=12, jump_target_minute=20,
        hour_range=(6, 12), npc_id="lin_wanqing", requires_unlock="lab_access", min_energy=25),
    "research_afternoon": Activity(
        id="research_afternoon", name="做实验(下午场)", description="在实验室做科研，到17:00",
        effect=ActivityEffect(energy=-20, research_ability=2),
        time_jump_type="jump_to", jump_target_hour=17, jump_target_minute=0,
        hour_range=(12, 17), npc_id="lin_wanqing", requires_unlock="lab_access", min_energy=25),
    "research_evening": Activity(
        id="research_evening", name="做实验(晚上场)", description="在实验室做科研，到22:00",
        effect=ActivityEffect(energy=-20, research_ability=2),
        time_jump_type="jump_to", jump_target_hour=22, jump_target_minute=0,
        hour_range=(17, 22), npc_id="lin_wanqing", requires_unlock="lab_access", min_energy=25),
    "chat_roommate": Activity(
        id="chat_roommate", name="和室友聊天", description="与陈奕然聊天放松",
        effect=ActivityEffect(energy=5, health=5, social_ability=1),
        time_cost_minutes=30, hour_range=(18, 25), npc_id="chen_yiran"),
    "date_boyfriend": Activity(
        id="date_boyfriend", name="约会", description="和沈星辞约会",
        effect=ActivityEffect(energy=-5, health=10, social_ability=2),
        time_cost_minutes=90, hour_range=(12, 22),
        npc_id="shen_xingci", requires_unlock="boyfriend_unlocked", daily_limit=1),
    "club_activity": Activity(
        id="club_activity", name="社团活动", description="参加电子科技协会活动",
        effect=ActivityEffect(energy=-10, social_ability=3, research_ability=1),
        time_cost_minutes=90, hour_range=(13, 21),
        npc_id="zhang_kunlin", requires_unlock="club_joined"),
    "help_tourist": Activity(
        id="help_tourist", name="帮助游客", description="为游客介绍清华",
        effect=ActivityEffect(energy=-5, social_ability=1),
        time_cost_minutes=60, hour_range=(8, 17), npc_id="zhao_xiao", daily_limit=2),
    "eat_canteen": Activity(
        id="eat_canteen", name="食堂吃饭", description="去食堂吃饭",
        effect=ActivityEffect(energy=20, health=15),
        time_cost_minutes=30, npc_id="li_juan", daily_limit=3),
    "rest": Activity(
        id="rest", name="休息", description="回宿舍休息一下",
        effect=ActivityEffect(energy=30, health=10), time_cost_minutes=60),
    "sleep": Activity(
        id="sleep", name="睡觉", description="睡觉到第二天6:30",
        effect=ActivityEffect(energy=50, health=20),
        time_jump_type="next_day", hour_range=(21, 25), daily_limit=1),
    "exercise": Activity(
        id="exercise", name="运动", description="去操场跑步锻炼",
        effect=ActivityEffect(energy=-5, health=20),
        time_cost_minutes=60, hour_range=(6, 21), min_energy=15),
    "tour_campus": Activity(
        id="tour_campus", name="游览校园", description="参观清华校园风景",
        effect=ActivityEffect(energy=5, health=5, social_ability=1),
        time_cost_minutes=60, hour_range=(8, 18)),
    "consult_teacher": Activity(
        id="consult_teacher", name="请教老师", description="向王老师请教学习问题",
        effect=ActivityEffect(energy=-10),
        time_cost_minutes=45, hour_range=(8, 17), npc_id="wang_yuxia", daily_limit=1),
    "consult_mentor": Activity(
        id="consult_mentor", name="导师面谈", description="与林晚晴导师面谈",
        effect=ActivityEffect(energy=-10, research_ability=3),
        time_cost_minutes=45, hour_range=(13, 17), npc_id="lin_wanqing", daily_limit=1),
    "social_meeting": Activity(
        id="social_meeting", name="组会", description="参加社工组织组会",
        effect=ActivityEffect(energy=-10, social_ability=5),
        time_cost_minutes=90, hour_range=(18, 21), requires_unlock="social_org_joined", daily_limit=1),
}


def _in_range(hour: int, hr: Optional[tuple]) -> bool:
    if hr is None: return True
    s, e = hr
    return hour >= s or hour < (e - 24) if e > 24 else s <= hour < e


def get_available_activities() -> List[Dict[str, Any]]:
    state = get_player_state()
    tgm = get_current_game_minutes()
    ti = time_info_from_minutes(tgm); h = ti["hour"]
    out = []
    for aid, act in ACTIVITIES.items():
        if not _in_range(h, act.hour_range): continue
        if act.requires_unlock and not get_unlock_flag(act.requires_unlock): continue
        if get_today_activity_count(aid, tgm) >= act.daily_limit: continue
        if state.get("energy", 0) < act.min_energy: continue
        out.append({
            "id": act.id, "name": act.name, "description": act.description,
            "npc_id": act.npc_id,
            "time_cost": f"至{act.jump_target_hour}:{act.jump_target_minute:02d}" if act.time_jump_type == "jump_to"
                         else ("至第二天6:30" if act.time_jump_type == "next_day" else f"{act.time_cost_minutes}分钟"),
            "effect_preview": {"energy": act.effect.energy, "health": act.effect.health,
                               "research_ability": act.effect.research_ability,
                               "social_ability": act.effect.social_ability},
        })
    return out


def execute_activity(activity_id: str, flags: List[str] = None) -> Dict[str, Any]:
    """
    执行活动。flags 可包含 "curfew_penalty" 表示宵禁晚归上下文。
    """
    if activity_id not in ACTIVITIES:
        return {"success": False, "message": "活动不存在"}

    act = ACTIVITIES[activity_id]
    state = get_player_state()
    tgm = get_current_game_minutes()
    ti = time_info_from_minutes(tgm)

    if not _in_range(ti["hour"], act.hour_range):
        return {"success": False, "message": f"当前时间({ti['hour']}:00)无法进行此活动"}
    if act.requires_unlock and not get_unlock_flag(act.requires_unlock):
        return {"success": False, "message": "尚未解锁此活动"}
    if get_today_activity_count(activity_id, tgm) >= act.daily_limit:
        return {"success": False, "message": "今日此活动已达上限"}
    if state.get("energy", 0) < act.min_energy:
        return {"success": False, "message": f"精力不足，需要至少{act.min_energy}点精力"}

    # 记录变更前的精力/健康（用于边沿检测）
    energy_prev = state.get("energy", 60)
    health_prev = state.get("health", 60)

    pause_game()

    if act.time_jump_type == "next_day":
        delta = calc_time_jump_next_day(tgm)
    elif act.time_jump_type == "jump_to":
        delta = calc_time_jump_to(tgm, act.jump_target_hour, act.jump_target_minute)
    else:
        delta = act.time_cost_minutes
    add_game_minutes(delta)

    # 社工加成
    sorg = state.get("social_org")
    bonus_social = 1.15 if sorg == "youth_league" else 1.0

    changes = {}
    eff = act.effect
    if eff.energy: changes["energy"] = eff.energy
    if eff.health: changes["health"] = eff.health
    if eff.research_ability: changes["research_ability"] = eff.research_ability
    if eff.social_ability: changes["social_ability"] = int(eff.social_ability * bonus_social)

    # ★ v2.2: 如果有宵禁晚归标记，在活动结算后追加惩罚
    if flags and "curfew_penalty" in flags:
        day_block = get_day_block(tgm)
        if record_penalty(day_block, "curfew"):
            changes["energy"] = changes.get("energy", 0) + CURFEW_PENALTY["energy"]
            changes["health"] = changes.get("health", 0) + CURFEW_PENALTY["health"]

    new_state = update_player_state(changes) if changes else get_player_state()

    # ★ v2.2: eat_canteen 自动标记当前餐段已吃
    if activity_id == "eat_canteen":
        meal = get_current_meal_type(tgm)
        if meal:
            mark_meal_eaten(get_day_block(tgm), meal, tgm)

    log_activity(activity_id, act.npc_id, "success", tgm)

    # ★ v2.2: 精力/健康归零检测（边沿检测）
    events = _check_health_energy_events(energy_prev, health_prev, new_state)

    resume_game()

    newly_unlocked = check_auto_unlock() if act.npc_id else []

    # 重新读取最新状态（归零事件可能修改了精健和时间）
    new_state = get_player_state()
    new_tgm = get_current_game_minutes()
    new_ti = time_info_from_minutes(new_tgm)

    # ★ v2.2: 学期切换 → 调用 semester_transition
    sem_result = None
    if new_ti["semester_index"] > ti["semester_index"]:
        cum_gpa, detail = semester_transition(ti["semester_index"])
        sem_result = {"semester": ti["semester_name"], "gpa": cum_gpa, "detail": detail}
        new_state = get_player_state()  # 重读（gpa_committed 已更新）

    return {
        "success": True, "activity_name": act.name, "npc_id": act.npc_id,
        "effect_applied": changes,
        "time_advanced_minutes": int(new_tgm - tgm) if new_tgm > tgm else delta,
        "new_time": {"date_display": new_ti["date_display"], "time_display": new_ti["time_display"],
                     "hour": new_ti["hour"], "minute": new_ti["minute"]},
        "new_state": new_state, "newly_unlocked": newly_unlocked,
        "events": events, "semester_gpa_result": sem_result,
    }


def attend_class(course_id: str) -> Dict[str, Any]:
    tgm = get_current_game_minutes()
    ti = time_info_from_minutes(tgm)
    today = get_today_courses(ti["weekday"])
    matching = [c for c in today if c["course_id"] == course_id]
    if not matching:
        return {"success": False, "message": "今天没有这门课"}
    course = matching[0]
    pi = COURSE_PERIODS.get(course["period"])
    if not pi: return {"success": False, "message": "课程时段异常"}

    mid = int(tgm % MINUTES_PER_DAY)
    if mid > pi["end_offset"]:
        status, md = "absent", -10
    elif mid > pi["start_offset"] + 15:
        status, md = "late", 2
    else:
        status, md = "on_time", 5

    state = get_player_state()
    if state.get("social_org") == "science_assoc": md = int(md * 1.1)

    update_course_mastery(course_id, md)
    record_attendance(course_id, status != "absent")

    energy_prev = state.get("energy", 60)
    health_prev = state.get("health", 60)

    pause_game()
    jump = max(0, pi["end_offset"] - mid)
    add_game_minutes(jump)
    new_state = update_player_state({"energy": -10 if status != "absent" else 0})

    events = _check_health_energy_events(energy_prev, health_prev, new_state)
    resume_game()

    new_state = get_player_state()
    nti = time_info_from_minutes(get_current_game_minutes())
    return {"success": True, "course_name": course["course_name"],
            "attendance_status": status, "mastery_delta": md,
            "time_advanced_minutes": jump,
            "new_time": {"date_display": nti["date_display"], "time_display": nti["time_display"]},
            "events": events}


def study_course(activity_id: str, course_id: str) -> Dict[str, Any]:
    result = execute_activity(activity_id)
    if not result.get("success"): return result
    state = get_player_state()
    d = 3
    if state.get("social_org") == "science_assoc": d = int(d * 1.1)
    update_course_mastery(course_id, d)
    result["course_mastery_bonus"] = {"course_id": course_id, "delta": d}
    return result


# ═══════════════════════════════════════════
# ★ v2.2: 精力/健康归零检测（边沿检测 + 健康优先 + N*1110跳跃）
# ═══════════════════════════════════════════

def _check_health_energy_events(energy_prev: int, health_prev: int, state: dict) -> List[dict]:
    """
    检测精力/健康从 >0 变为 ==0 的边沿事件。
    健康优先：同时归零只执行生病分支（+3330），不叠加疲惫（+1110）。
    """
    events = []
    en = state.get("energy", 60)
    hp = state.get("health", 60)

    health_zeroed = health_prev > 0 and hp <= 0
    energy_zeroed = energy_prev > 0 and en <= 0

    if health_zeroed:
        # ★ 生病：跳过3个游戏日 + 恢复
        pause_game()
        add_game_minutes(HEALTH_ZERO_SKIP_DAYS * MINUTES_PER_DAY)
        update_player_state_absolute({
            "energy": RECOVERY_ENERGY_AFTER_SKIP,
            "health": RECOVERY_HEALTH_AFTER_SKIP,
        })
        resume_game()
        events.append({"type": "hospital", "message": MESSAGE_HOSPITAL})

    elif energy_zeroed:
        # ★ 疲惫：跳过1个游戏日 + 恢复
        pause_game()
        add_game_minutes(ENERGY_ZERO_SKIP_DAYS * MINUTES_PER_DAY)
        update_player_state_absolute({
            "energy": RECOVERY_ENERGY_AFTER_SKIP,
            "health": RECOVERY_HEALTH_AFTER_SKIP,
        })
        resume_game()
        events.append({"type": "faint", "message": MESSAGE_FAINT})

    else:
        # 警告（非归零）
        if 0 < en <= 30:
            events.append({"type": "energy_warning", "message": "你的精力很低，建议尽快休息或吃饭。"})
        if 0 < hp <= 30:
            events.append({"type": "health_warning", "message": "你的健康状况堪忧，请注意规律饮食和休息。"})

    return events


# ═══════════════════════════════════════════
# ★ v2.2: 晚归惩罚（幂等）
# ═══════════════════════════════════════════

def apply_curfew_penalty() -> Dict[str, Any]:
    """
    晚归惩罚：在当前玩家状态上扣除 energy/health，幂等（同 day_block 不重复扣）。
    返回 {success, applied, player, events}
    """
    tgm = get_current_game_minutes()
    day_block = get_day_block(tgm)

    if is_penalty_applied(day_block, "curfew"):
        return {"success": True, "applied": False,
                "message": "本日宵禁惩罚已处理", "player": get_player_state(), "events": []}

    state = get_player_state()
    energy_prev = state.get("energy", 60)
    health_prev = state.get("health", 60)

    record_penalty(day_block, "curfew", "late_return")

    new_state = update_player_state({
        "energy": CURFEW_PENALTY["energy"],
        "health": CURFEW_PENALTY["health"],
    })

    events = _check_health_energy_events(energy_prev, health_prev, new_state)
    new_state = get_player_state()  # 重读（归零事件可能修改了）

    return {"success": True, "applied": True, "player": new_state, "events": events}


# ═══════════════════════════════════════════
# ★ v2.2: 缺餐惩罚（幂等）
# ═══════════════════════════════════════════

def apply_missed_meals_penalty() -> Dict[str, Any]:
    """
    根据当前服务端时间判定本日已错过且未吃的餐段，扣罚精力/健康。
    按 (day_block, meal_type) 幂等。
    返回 {success, applied, missed_meals, energy_delta, health_delta, player, events}
    """
    tgm = get_current_game_minutes()
    day_block = get_day_block(tgm)
    offset = get_day_offset(tgm)

    missed = []
    for meal, deadline in MEAL_DEADLINES.items():
        if offset >= deadline and not is_meal_eaten(day_block, meal):
            # 检查是否已经扣过这一餐
            penalty_key = f"meal_{meal}"
            if not is_penalty_applied(day_block, penalty_key):
                record_penalty(day_block, penalty_key, f"missed_{meal}")
                missed.append(meal)

    if not missed:
        return {"success": True, "applied": False, "missed_meals": [],
                "energy_delta": 0, "health_delta": 0,
                "player": get_player_state(), "events": []}

    state = get_player_state()
    energy_prev = state.get("energy", 60)
    health_prev = state.get("health", 60)

    total_energy = MEAL_MISS_PENALTY["energy"] * len(missed)
    total_health = MEAL_MISS_PENALTY["health"] * len(missed)

    new_state = update_player_state({
        "energy": total_energy,
        "health": total_health,
    })

    events = _check_health_energy_events(energy_prev, health_prev, new_state)
    new_state = get_player_state()

    return {
        "success": True, "applied": True,
        "missed_meals": missed,
        "energy_delta": total_energy,
        "health_delta": total_health,
        "player": new_state, "events": events,
    }


# ═══════════════════════════════════════════
# 自动解锁 / 时间信息 / 社工系统
# ═══════════════════════════════════════════

def check_auto_unlock() -> List[str]:
    newly = []
    fs = get_all_npc_friendships(); state = get_player_state()
    if not get_unlock_flag("boyfriend_unlocked") and fs.get("shen_xingci", 0) >= 60:
        set_unlock_flag("boyfriend_unlocked"); newly.append("boyfriend_unlocked")
    if not get_unlock_flag("mentor_close") and fs.get("lin_wanqing", 0) >= 80:
        set_unlock_flag("mentor_close"); newly.append("mentor_close")
    if not get_unlock_flag("lab_access") and state.get("research_ability", 0) >= 30 and fs.get("lin_wanqing", 0) >= 40:
        set_unlock_flag("lab_access"); newly.append("lab_access")
    if not get_unlock_flag("club_joined") and state.get("social_ability", 0) >= 20 and fs.get("zhang_kunlin", 0) >= 30:
        set_unlock_flag("club_joined"); newly.append("club_joined")
    if not get_unlock_flag("srt_unlocked") and state.get("gpa", 0) >= 3.0 and state.get("semester_index", 0) >= 2:
        set_unlock_flag("srt_unlocked"); newly.append("srt_unlocked")
    if not get_unlock_flag("internship_unlocked") and state.get("semester_index", 0) >= 4:
        set_unlock_flag("internship_unlocked"); newly.append("internship_unlocked")
    return newly


def get_time_info() -> Dict[str, Any]:
    return time_info_from_minutes(get_current_game_minutes())


SOCIAL_ORGS = {
    "student_union": {"name": "学生会", "bonus": "NPC好感度上升加快10%"},
    "youth_league":  {"name": "团委",   "bonus": "社工能力上升加快15%"},
    "science_assoc": {"name": "科协",   "bonus": "课程掌握度上升加快10%"},
}
SOCIAL_RANKS = ["member", "leader", "minister", "president"]
SOCIAL_RANK_NAMES = {"member": "组员", "leader": "组长", "minister": "部长", "president": "主席"}

def join_social_org(org_type: str) -> Dict[str, Any]:
    if org_type not in SOCIAL_ORGS: return {"success": False, "message": "无效的组织类型"}
    state = get_player_state()
    if state.get("social_org"): return {"success": False, "message": "你已经加入了一个组织"}
    update_player_state({"social_org": org_type, "social_rank": "member"})
    set_unlock_flag("social_org_joined")
    return {"success": True, "org": SOCIAL_ORGS[org_type]["name"], "rank": "组员", "bonus": SOCIAL_ORGS[org_type]["bonus"]}

def try_promote_social_rank() -> Dict[str, Any]:
    state = get_player_state()
    org, rank = state.get("social_org"), state.get("social_rank")
    if not org or not rank: return {"success": False, "message": "未加入任何组织"}
    social = state.get("social_ability", 0)
    ci = SOCIAL_RANKS.index(rank) if rank in SOCIAL_RANKS else 0
    if ci >= len(SOCIAL_RANKS) - 1: return {"success": False, "message": "已是最高级别"}
    thresholds = [0, 30, 60, 85]
    if social >= thresholds[ci + 1]:
        nr = SOCIAL_RANKS[ci + 1]; update_player_state({"social_rank": nr})
        return {"success": True, "new_rank": SOCIAL_RANK_NAMES[nr], "message": f"恭喜晋升为{SOCIAL_RANK_NAMES[nr]}！"}
    return {"success": False, "message": f"社工能力不足，需要{thresholds[ci+1]}（当前{social}）"}
