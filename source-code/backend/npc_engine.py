"""
NPC AI 引擎 v2.1 —— 单玩家，后端时间，知识库
"""
import json, re, os, logging
from typing import Optional, Dict, Any
from dataclasses import dataclass

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

from database import (
    get_npc_state, update_npc_state, get_recent_memory, add_memory,
    get_player_state, get_all_unlock_flags, get_current_game_minutes,
    time_info_from_minutes, SEMESTER_NAMES,
)
from activity_system import check_auto_unlock

def get_api_client():
    api_key = os.getenv("DEEPSEEK_API_KEY")
    if not api_key: logger.warning("DEEPSEEK_API_KEY 未设置"); return None
    try:
        from openai import OpenAI
        return OpenAI(api_key=api_key, base_url="https://api.deepseek.com")
    except Exception as e:
        logger.error(f"初始化API客户端失败: {type(e).__name__}"); return None

NPC_CONFIGS = {
    "lin_wanqing": {
        "name": "林晚晴", "identity": "导师（电子系副教授）",
        "bio": "32岁，本校电子系副教授，芯片研发与信号处理专家。",
        "traits": "严谨细致，外冷内热。平时话不多，但对认真的学生格外耐心。",
        "friendship_styles": {"normal":"语气专业简洁，略显疏离。","good":"主动关心学习进度。","excellent":"分享科研经历，语气柔和。","intimate":"像朋友般坦诚。"},
        "special_trigger": "若GPA<2.6应担忧约谈；若GPA≥3.8给予保研鼓励。",
        "knowledge_base": {"科研":["SRT申请流程：大二上可申报","实验室安全规范","论文投稿推荐IEEE","每周四下午2-4点答疑"],"个人":["研究方向太赫兹通信","喜欢美式咖啡"],"游戏小提示":["找林老师答疑提升掌握度","实验室熬夜降健康","大二上可申请SRT"]},
    },
    "chen_yiran": {
        "name": "陈奕然", "identity": "室友",
        "bio": "18-22岁，江苏理科状元，数学与编程天才。安静内向。女生。",
        "traits": "说话简短逻辑性强，偶尔幽默。用行动表达关心。",
        "friendship_styles": {"normal":"礼貌回应。","good":"主动问'今天自习吗'。","excellent":"关心精力和睡眠。","intimate":"最重要的朋友。"},
        "special_trigger": "若精力<30主动劝休息。",
        "knowledge_base": {"学习":["图书馆三楼最安静","模电考试重点负反馈和运放"],"生活":["宿舍热水早7-晚11","紫荆食堂麻辣香锅好吃"],"游戏小提示":["和室友聊天恢复精力","连续自习效率递减"]},
    },
    "shen_xingci": {
        "name": "沈星辞", "identity": "男友（计算机系）",
        "bio": "19-23岁，计算机系优秀生，温柔体贴。",
        "traits": "语气温柔亲昵，恋爱后有小撒娇小惊喜。",
        "friendship_styles": {"normal":"热情友好。","good":"主动约饭自习。","excellent":"给你带零食。","intimate":"以男友身份亲昵对话。"},
        "special_trigger": "好感度≥60解锁恋爱线。",
        "knowledge_base": {"计算机":["Python比C写起来快","算法竞赛报名截止通常10月"],"约会":["荷塘月色是约会圣地","校庆日可以一起看表演"],"游戏小提示":["好感度60解锁恋爱线","约会恢复健康值"]},
    },
    "li_juan": {
        "name": "李娟", "identity": "食堂阿姨",
        "bio": "清华食堂打饭阿姨，热情爽朗。",
        "traits": "说话接地气，爱用'孩子'称呼。好感度初始较高。",
        "friendship_styles": {"normal":"热情打招呼。","good":"多盛一勺菜。","excellent":"提前留喜欢的菜。","intimate":"像自家长辈嘘寒问暖。"},
        "special_trigger": "若健康<30严肃劝多吃饭。",
        "knowledge_base": {"食堂":["早餐7-9点午餐11-13点晚餐17-19点","周四有红烧排骨","紫荆食堂11:20人最少"],"游戏小提示":["按时吃饭很重要","吃饭恢复精力和健康"]},
    },
    "wang_yuxia": {
        "name": "王玉霞", "identity": "授课老师",
        "bio": "电子系高数、模电授课老师，教学经验丰富。",
        "traits": "语气平和严肃，关注成绩和到课情况。",
        "friendship_styles": {"normal":"正式称呼同学。","good":"考后约谈分析。","excellent":"推荐额外资料。","intimate":"得意门生待遇。"},
        "special_trigger": "若GPA<2.0主动约谈补课。",
        "knowledge_base": {"教学":["模电期中考试第8周","高数重点极限微积分级数","答疑每周二四下午"],"游戏小提示":["找王老师答疑提升掌握度","翘课大幅降低掌握度"]},
    },
    "zhang_kunlin": {
        "name": "张锟霖", "identity": "社团骨干（电子科技协会）",
        "bio": "电子科技协会骨干，热情外向，人脉广。",
        "traits": "自来熟，爱称'学妹'，说话快而热情。",
        "friendship_styles": {"normal":"热情介绍社团。","good":"分享活动信息。","excellent":"推荐竞赛名额。","intimate":"像亲哥哥帮规划方向。"},
        "special_trigger": "社工能力≥30邀请担任干部。",
        "knowledge_base": {"社团":["百团大战大一上第3周","科协有安世杯智能小车比赛","拓竹杯软件设计大赛春季报名","每两周一次组会"],"游戏小提示":["加入科协课程掌握度上升更快","社团活动提升社工能力"]},
    },
    "zhao_xiao": {
        "name": "赵晓", "identity": "游客",
        "bio": "来清华参观的游客，充满向往和好奇。",
        "traits": "语气充满羡慕好奇，可爱的路人角色。",
        "friendship_styles": {"normal":"礼貌好奇。","good":"感谢解答。","excellent":"留联系方式。","intimate":"临走送小礼物。"},
        "special_trigger": "解答清华问题额外提升社工能力。",
        "knowledge_base": {"清华景点":["二校门是清华标志","荷塘月色源自朱自清","清华学堂最早教学楼","水木清华最美"],"游戏小提示":["帮助游客提升社工能力"]},
    },
}

def _tier(f): return "intimate" if f>=80 else "excellent" if f>=60 else "good" if f>=30 else "normal"
def _tier_zh(f): return {"intimate":"知己","excellent":"亲密","good":"友善","normal":"陌生"}[_tier(f)]

def build_time_context() -> str:
    ti = time_info_from_minutes(get_current_game_minutes())
    tips = {0:"大一上，刚入学。",1:"大一下，适应中。",2:"大二上，专业课，可申SRT。",3:"大二下，科研接触。",4:"大三上，面临选择。",5:"大三下，竞赛实习。",6:"大四上，毕设开题。",7:"大四下，毕业季。"}
    return f"当前：{ti['semester_name']} 第{ti['week']}周 {ti['weekday_name']} {ti['hour']}:{ti['minute']:02d}\n提示：{tips.get(ti['semester_index'],'')}"

def _build_knowledge_context(config: dict, player_input: str) -> str:
    kb = config.get("knowledge_base", {})
    if not kb: return ""
    import re as _re
    il = player_input.lower()
    iw = [w for w in il.replace("，"," ").replace("？"," ").replace("。"," ").split() if len(w)>=2]
    iw.append(il)
    iw.extend(t for t in _re.findall(r'[a-zA-Z0-9+]+', il) if len(t)>=2)
    iw.extend(_re.findall(r'[\u4e00-\u9fff]{2,}', il))
    rel = []
    for cat, items in kb.items():
        for item in items:
            lo = item.lower()
            if any(w in lo for w in iw) or any(kw in il for kw in lo.replace("："," ").replace("，"," ").replace("、"," ").split() if len(kw)>=2):
                rel.append(f"[{cat}] {item}")
    if not rel:
        tips = kb.get("游戏小提示", [])
        if tips: rel.append(f"[游戏小提示] {tips[0]}")
    if not rel: return ""
    return "【角色知识库参考】\n" + "\n".join(rel[:5])

@dataclass
class PlayerStats:
    gpa: float = 2.0; energy: int = 60; health: int = 60
    research_ability: int = 0; social_ability: int = 0
    social_org: Optional[str] = None; social_rank: Optional[str] = None

def _fallback(nid, cfg):
    return {"reply": f"（{cfg['name']}微微点头，似乎在思考什么……）", "friendship_change": 0, "valence_change": 0.0, "emotion": "neutral"}

def generate_reply(npc_id: str, player_input: str, player_stats: Optional[PlayerStats] = None) -> Dict[str, Any]:
    cfg = NPC_CONFIGS.get(npc_id, {"name":"神秘人","identity":"路人","bio":"路人","traits":"普通","friendship_styles":{},"special_trigger":"","knowledge_base":{}})
    f, v = get_npc_state(npc_id); tier = _tier(f); tzh = _tier_zh(f)
    style = cfg.get("friendship_styles",{}).get(tier,"正常对话")
    mem = get_recent_memory(npc_id, 6) or "（初次交谈）"
    tctx = build_time_context(); kctx = _build_knowledge_context(cfg, player_input)
    sl = ""; warns = []
    if player_stats:
        on = {"student_union":"学生会","youth_league":"团委","science_assoc":"科协"}.get(player_stats.social_org,"无")
        sl = f"GPA={player_stats.gpa:.2f}/4.0, 精力={player_stats.energy}/100, 健康={player_stats.health}/100, 科研={player_stats.research_ability}/100, 社工={player_stats.social_ability}/100, 组织={on}"
        if player_stats.energy<30: warns.append("精力极低(<30)，劝休息")
        if player_stats.health<30: warns.append("健康很差(<30)，关心身体")
        if player_stats.gpa<2.0: warns.append("GPA<2.0，导师/老师需严肃约谈")
        elif player_stats.gpa<2.6 and npc_id in ("lin_wanqing","wang_yuxia"): warns.append("GPA<2.6，表示担忧")
        if player_stats.gpa>=3.8 and npc_id=="lin_wanqing": warns.append("GPA≥3.8，鼓励保研/科研")
    flags = get_all_unlock_flags()
    bf = flags.get("boyfriend_unlocked",False) and npc_id=="shen_xingci"
    sp = cfg.get("special_trigger","")
    if bf: sp = "【重要】已确认恋爱关系，以男友身份亲昵对话。" + sp

    prompt = f"""你正在扮演《清华园物语》角色：{cfg['name']}（{cfg['identity']}）
【背景】{cfg['bio']}
【性格】{cfg['traits']}
【好感度】{f}/100（{tzh}）
【风格】{style}
【时间】{tctx}
【玩家状态】{sl or '未知'}
{'【⚠️】'+chr(10).join(warns) if warns else ''}
{f'【特殊触发】{sp}' if sp else ''}
{kctx}
【记忆】
{mem}
━━━━━━
1. 严格角色扮演，第一人称，50-120字。
2. 参考记忆保持连贯。如果知识库有相关信息自然融入。
3. 好感度变化-5到+5。
输出JSON：{{"reply":"...","friendship_change":数字,"valence_change":0.0,"emotion":"happy/neutral/sad/angry/surprised"}}"""

    client = get_api_client()
    if not client: result = _fallback(npc_id, cfg)
    else:
        try:
            resp = client.chat.completions.create(model="deepseek-chat",messages=[{"role":"system","content":prompt},{"role":"user","content":f"玩家说：{player_input}"}],temperature=0.85,max_tokens=300)
            raw = resp.choices[0].message.content.strip()
            m = re.search(r'\{.*\}', raw, re.DOTALL)
            result = json.loads(m.group()) if m else _fallback(npc_id, cfg)
        except: result = _fallback(npc_id, cfg)

    add_memory(npc_id, f"玩家：{player_input}\n{cfg['name']}：{result.get('reply','')}")
    fc = int(result.get("friendship_change",0))
    if player_stats and player_stats.social_org=="student_union" and fc>0: fc = int(fc*1.1)
    nf = update_npc_state(npc_id, fc, float(result.get("valence_change",0)))
    result.update({"current_friendship":nf,"npc_name":cfg["name"],"friendship_tier":_tier_zh(nf),"friendship_change":fc,"newly_unlocked":check_auto_unlock()})
    return result

def get_npc_info(npc_id: str) -> Optional[Dict[str,Any]]:
    cfg = NPC_CONFIGS.get(npc_id)
    if not cfg: return None
    f,_ = get_npc_state(npc_id)
    return {"npc_id":npc_id,"name":cfg["name"],"identity":cfg["identity"],"bio":cfg["bio"],"friendship":f,"friendship_tier":_tier_zh(f)}

def get_all_npcs() -> list:
    out = []
    for nid, cfg in NPC_CONFIGS.items():
        f,_ = get_npc_state(nid)
        out.append({"npc_id":nid,"name":cfg["name"],"identity":cfg["identity"],"friendship":f,"friendship_tier":_tier_zh(f)})
    return out
