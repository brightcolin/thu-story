"""
《清华园物语》后端API v2.2
v2.1 → v2.2 新增：
  - POST /player/penalties/curfew  （晚归惩罚，幂等）
  - POST /player/penalties/meals   （缺餐惩罚，幂等）
  - POST /semester/transition      （学期交割：冻结+GPA+清课表）
  - POST /activities/execute 支持 flags 字段（如 curfew_penalty）
  - GET /courses/schedule 仅返回当前学期
"""
from dotenv import load_dotenv
load_dotenv()

import os, secrets
from typing import Optional, List
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException, Depends
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field, field_validator
from fastapi.security import APIKeyHeader

api_key_header = APIKeyHeader(name="X-Token", auto_error=True)
API_TOKEN = os.getenv("API_TOKEN")
if not API_TOKEN:
    API_TOKEN = secrets.token_hex(16)
    print(f"[WARN] API_TOKEN 未设置，已自动生成: {API_TOKEN}")

async def verify_token(x_token: str = Depends(api_key_header)):
    if x_token != API_TOKEN:
        raise HTTPException(status_code=401, detail="无效的访问令牌",
                            headers={"WWW-Authenticate": "X-Token"})
    return x_token

from database import (
    init_db, reset_save, get_player_state, update_player_state,
    get_all_npc_friendships, get_all_unlock_flags,
    get_courses_for_semester, select_course, get_player_schedule,
    get_player_courses, check_endings, pause_game, resume_game,
    get_current_game_minutes, time_info_from_minutes, add_game_minutes,
    semester_transition,
)
from npc_engine import (
    generate_reply, get_npc_info, get_all_npcs, PlayerStats, NPC_CONFIGS,
)
from activity_system import (
    get_available_activities, execute_activity, get_time_info,
    attend_class, study_course, ACTIVITIES,
    join_social_org, try_promote_social_rank, SOCIAL_ORGS, SOCIAL_RANK_NAMES,
    apply_curfew_penalty, apply_missed_meals_penalty,
)

@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    print("[OK] 数据库初始化完成 (v2.2)")
    if not os.getenv("DEEPSEEK_API_KEY"): print("[WARN] DEEPSEEK_API_KEY 未设置")
    else: print("[OK] API密钥已配置")
    yield
    print("[BYE] 服务关闭")

app = FastAPI(title="清华园物语 API", version="2.2.0", lifespan=lifespan)
ALLOWED_ORIGINS = os.getenv("ALLOWED_ORIGINS", "*").split(",")
app.add_middleware(CORSMiddleware, allow_origins=ALLOWED_ORIGINS,
                   allow_credentials=True, allow_methods=["*"], allow_headers=["*"])

# ─── 请求模型 ───

class ChatRequest(BaseModel):
    npc_id: str = Field(..., description="NPC标识符")
    message: str = Field(..., min_length=1, max_length=500)
    @field_validator('npc_id')
    @classmethod
    def validate_npc_id(cls, v):
        if v not in NPC_CONFIGS: raise ValueError(f"无效的NPC ID: {v}")
        return v
    @field_validator('message')
    @classmethod
    def validate_message(cls, v): return v.strip()

class ChatResponse(BaseModel):
    npc_name: str; reply: str; emotion: str
    friendship_change: int; current_friendship: int
    friendship_tier: str; newly_unlocked: List[str] = []

class ActivityRequest(BaseModel):
    activity_id: str = Field(..., description="活动ID")
    course_id: Optional[str] = Field(default=None, description="自习时选择的科目")
    flags: Optional[List[str]] = Field(default=None, description='可选标记，如 ["curfew_penalty"]')
    @field_validator('activity_id')
    @classmethod
    def validate_activity_id(cls, v):
        if v not in ACTIVITIES: raise ValueError(f"无效的活动ID: {v}")
        return v

class CourseSelectRequest(BaseModel):
    course_id: str
    schedule: List[dict] = Field(..., description='[{"day_of_week":0,"period":1}]')

class AttendClassRequest(BaseModel):
    course_id: str

class JoinSocialOrgRequest(BaseModel):
    org_type: str

class PlayerStateUpdate(BaseModel):
    energy: Optional[int] = None
    health: Optional[int] = None
    research_ability: Optional[int] = None
    social_ability: Optional[int] = None

# ─── 系统 ───

@app.get("/", tags=["系统"])
async def root():
    return {"status": "running", "game": "清华园物语", "version": "2.2.0"}

@app.get("/health", tags=["系统"])
async def health_check():
    return {"status": "healthy", "api_configured": bool(os.getenv("DEEPSEEK_API_KEY"))}

# ─── NPC 对话 ───

@app.post("/chat", response_model=ChatResponse, tags=["NPC对话"], dependencies=[Depends(verify_token)])
async def chat_with_npc(req: ChatRequest):
    try:
        state = get_player_state()
        stats = PlayerStats(
            gpa=state.get("gpa",0.0), energy=state.get("energy",60), health=state.get("health",60),
            research_ability=state.get("research_ability",0), social_ability=state.get("social_ability",0),
            social_org=state.get("social_org"), social_rank=state.get("social_rank"))
        result = generate_reply(npc_id=req.npc_id, player_input=req.message, player_stats=stats)
        return ChatResponse(**result)
    except Exception:
        raise HTTPException(status_code=500, detail="对话生成失败")

@app.get("/npcs", tags=["NPC对话"], dependencies=[Depends(verify_token)])
async def list_npcs(): return {"npcs": get_all_npcs()}

@app.get("/npcs/{npc_id}", tags=["NPC对话"], dependencies=[Depends(verify_token)])
async def get_npc(npc_id: str):
    info = get_npc_info(npc_id)
    if not info: raise HTTPException(status_code=404, detail="NPC不存在")
    return info

# ─── 玩家状态 ───

@app.get("/player", tags=["玩家状态"], dependencies=[Depends(verify_token)])
async def get_player():
    return {"player": get_player_state(), "friendships": get_all_npc_friendships(),
            "unlocks": get_all_unlock_flags(), "courses": get_player_courses()}

@app.patch("/player", tags=["玩家状态"], dependencies=[Depends(verify_token)])
async def do_update_player(update: PlayerStateUpdate):
    changes = {k: v for k, v in update.model_dump().items() if v is not None}
    if not changes: raise HTTPException(status_code=400, detail="无有效更新字段")
    return {"player": update_player_state(changes)}

# ─── 活动系统 ───

@app.get("/activities", tags=["活动系统"], dependencies=[Depends(verify_token)])
async def list_activities():
    return {"time": get_time_info(), "activities": get_available_activities()}

@app.post("/activities/execute", tags=["活动系统"], dependencies=[Depends(verify_token)])
async def do_activity(req: ActivityRequest):
    if req.course_id and req.activity_id.startswith("study_library"):
        result = study_course(req.activity_id, req.course_id)
    else:
        result = execute_activity(req.activity_id, flags=req.flags)
    if not result.get("success"):
        raise HTTPException(status_code=400, detail=result.get("message","活动执行失败"))
    return result

# ─── 时间系统 ───

@app.get("/time", tags=["时间系统"], dependencies=[Depends(verify_token)])
async def get_current_time():
    return get_time_info()

@app.post("/time/pause", tags=["时间系统"], dependencies=[Depends(verify_token)])
async def do_pause():
    pause_game()
    return {"paused": True, "time": get_time_info()}

@app.post("/time/resume", tags=["时间系统"], dependencies=[Depends(verify_token)])
async def do_resume():
    resume_game()
    return {"paused": False, "time": get_time_info()}

@app.post("/time/advance", tags=["时间系统"], dependencies=[Depends(verify_token)])
async def do_advance(minutes: int = 10):
    add_game_minutes(minutes)
    return {"advanced": minutes, "time": get_time_info()}

@app.post("/time/nextday", tags=["时间系统"], dependencies=[Depends(verify_token)])
async def do_nextday():
    from database import calc_time_jump_next_day
    tgm = get_current_game_minutes()
    add_game_minutes(calc_time_jump_next_day(tgm))
    return {"time": get_time_info()}

# ─── 课程系统 ───

@app.get("/courses/available", tags=["课程系统"], dependencies=[Depends(verify_token)])
async def get_avail_courses():
    state = get_player_state(); si = state.get("semester_index", 0)
    return {"semester_index": si, "semester_name": state.get("semester_name",""),
            "courses": get_courses_for_semester(si)}

@app.post("/courses/select", tags=["课程系统"], dependencies=[Depends(verify_token)])
async def do_select_course(req: CourseSelectRequest):
    if not select_course(req.course_id, req.schedule):
        raise HTTPException(status_code=400, detail="选课失败")
    return {"success": True, "course_id": req.course_id}

@app.get("/courses/schedule", tags=["课程系统"], dependencies=[Depends(verify_token)])
async def get_sched():
    """★ v2.2: 仅返回当前学期课表"""
    return {"schedule": get_player_schedule()}

@app.get("/courses/mine", tags=["课程系统"], dependencies=[Depends(verify_token)])
async def get_my_courses():
    return {"courses": get_player_courses()}

@app.post("/class/attend", tags=["课程系统"], dependencies=[Depends(verify_token)])
async def do_attend(req: AttendClassRequest):
    result = attend_class(req.course_id)
    if not result.get("success"):
        raise HTTPException(status_code=400, detail=result.get("message"))
    return result

# ─── ★ v2.2: 惩罚系统 ───

@app.post("/player/penalties/curfew", tags=["惩罚系统"], dependencies=[Depends(verify_token)])
async def do_curfew_penalty():
    """
    晚归惩罚（幂等）。同一游戏日重复调用不会二次扣罚。
    返回 {success, applied, player, events}
    """
    return apply_curfew_penalty()

@app.post("/player/penalties/meals", tags=["惩罚系统"], dependencies=[Depends(verify_token)])
async def do_missed_meals_penalty():
    """
    缺餐惩罚（幂等）。服务端根据当前时间判定已过期且未吃的餐段并扣罚。
    同一 (day_block, meal) 只扣一次。
    返回 {success, applied, missed_meals, energy_delta, health_delta, player, events}
    """
    return apply_missed_meals_penalty()

# ─── ★ v2.2: 学期交割 ───

@app.post("/semester/transition", tags=["学期系统"], dependencies=[Depends(verify_token)])
async def do_semester_transition():
    """
    手动触发学期交割（冻结课程→计算GPA→清空课表→写入gpa_committed）。
    通常由时间推进自动触发，此接口用于手动/补偿调用。
    """
    state = get_player_state()
    sem = state.get("semester_index", 0)
    if sem <= 0:
        # 检查是否有课需要交割
        pass
    cum_gpa, detail = semester_transition(sem)
    return {"success": True, "semester_settled": sem,
            "cumulative_gpa": cum_gpa, "detail": detail,
            "player": get_player_state()}

# ─── 社工系统 ───

@app.get("/social/orgs", tags=["社工系统"], dependencies=[Depends(verify_token)])
async def list_orgs():
    return {"orgs": [{"id":k,"name":v["name"],"bonus":v["bonus"]} for k,v in SOCIAL_ORGS.items()]}

@app.post("/social/join", tags=["社工系统"], dependencies=[Depends(verify_token)])
async def do_join(req: JoinSocialOrgRequest):
    result = join_social_org(req.org_type)
    if not result.get("success"): raise HTTPException(status_code=400, detail=result.get("message"))
    return result

@app.post("/social/promote", tags=["社工系统"], dependencies=[Depends(verify_token)])
async def do_promote():
    result = try_promote_social_rank()
    if not result.get("success"): raise HTTPException(status_code=400, detail=result.get("message"))
    return result

@app.get("/social/status", tags=["社工系统"], dependencies=[Depends(verify_token)])
async def get_social():
    state = get_player_state(); org = state.get("social_org"); rank = state.get("social_rank")
    return {"org":org, "org_name": SOCIAL_ORGS.get(org,{}).get("name") if org else None,
            "rank":rank, "rank_name": SOCIAL_RANK_NAMES.get(rank) if rank else None,
            "social_ability": state.get("social_ability",0)}

# ─── 结局 / 存档 / 管理 ───

@app.get("/endings", tags=["结局系统"], dependencies=[Depends(verify_token)])
async def get_endings(): return {"endings": check_endings()}

@app.post("/save/reset", tags=["存档管理"], dependencies=[Depends(verify_token)])
async def reset_game():
    reset_save(); return {"message": "存档已重置", "status": "success"}

@app.get("/save/export", tags=["存档管理"], dependencies=[Depends(verify_token)])
async def export_save():
    return {"player": get_player_state(), "friendships": get_all_npc_friendships(),
            "unlocks": get_all_unlock_flags(), "courses": get_player_courses(),
            "schedule": get_player_schedule()}

@app.get("/admin/all_logs", tags=["管理"], dependencies=[Depends(verify_token)])
async def get_all_logs(limit: int = 50):
    from database import get_connection
    conn = get_connection(); conn.row_factory = __import__('sqlite3').Row; c = conn.cursor()
    c.execute("SELECT * FROM activity_log ORDER BY id DESC LIMIT ?", (limit,))
    rows = c.fetchall(); conn.close()
    return {"logs": [dict(r) for r in rows]}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host=os.getenv("HOST","0.0.0.0"),
                port=int(os.getenv("PORT","8000")),
                reload=os.getenv("DEBUG","false").lower()=="true")
