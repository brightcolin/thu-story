# 后端部署说明 v2.2

## 1. 服务器要求

- **推荐配置**：阿里云 ECS 2核4G（或更高），Ubuntu 22.04 / Windows Server
- **Python**：3.10+
- **端口**：8000（需在安全组/防火墙放行 TCP 8000）
- **数据库**：SQLite（内嵌，无需额外安装）

## 2. 部署步骤

### 2.1 上传代码

将以下 6 个文件上传到服务器目录（如 `/opt/thustory` 或 `C:\thustory_backend`）：

```
database.py
activity_system.py
main.py
npc_engine.py
requirements.txt
.env.example
```

### 2.2 创建虚拟环境并安装依赖

```bash
cd /opt/thustory
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

Windows：
```powershell
cd C:\thustory_backend
python -m venv venv
.\venv\Scripts\activate
pip install -r requirements.txt
```

### 2.3 配置环境变量

```bash
cp .env.example .env
# 编辑 .env，填入实际值：
# DEEPSEEK_API_KEY=sk-xxx
# API_TOKEN=thustory
```

### 2.4 首次启动（初始化数据库）

```bash
# 先删除旧数据库（如果从 v2.1 升级，表结构不兼容）
rm -f qinghua_story.db

# 启动
python main.py
```

### 2.5 生产部署（使用 pm2 或 systemd）

**pm2（推荐，Windows/Linux 通用）：**
```bash
pm2 start main.py --name thustory-api --interpreter ./venv/bin/python
pm2 save
```

**systemd（Linux）：**
```ini
# /etc/systemd/system/thustory.service
[Unit]
Description=ThuStory Backend
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/opt/thustory
ExecStart=/opt/thustory/venv/bin/python main.py
Environment=PYTHONIOENCODING=utf-8
Restart=always

[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl enable thustory && sudo systemctl start thustory
```

## 3. 启动命令

```bash
python main.py
```

默认监听 `0.0.0.0:8000`，可通过 `.env` 中 `HOST`/`PORT` 修改。

启动后访问 `http://服务器IP:8000/docs` 可查看 Swagger API 文档。

## 4. 从 v2.1 升级注意事项

v2.2 数据库新增了 `meal_log`、`penalty_log` 表，`player_state` 增加 `gpa_committed` 列，`player_courses` 和 `course_schedule` 增加 `semester_index` 列。

**建议**：
1. 备份旧数据库 `cp qinghua_story.db qinghua_story.db.bak`
2. 删除旧数据库 `rm qinghua_story.db`
3. 重启服务（自动创建新库结构）

如果需要保留数据，代码中 `_migrate_add_gpa_committed()` 会自动为旧库添加缺失列，但 `player_courses` 表结构变化较大，建议重建。

## 5. 常见问题

### Q1: Windows 上 pm2 启动出现编码错误
```
UnicodeEncodeError: 'gbk' codec can't encode character
```
**解决**：启动前 `$env:PYTHONIOENCODING = "utf-8"`

### Q2: 端口 8000 被占用
```
OSError: [Errno 98] Address already in use
```
**解决**：`lsof -i:8000` 找到进程并 kill，或在 `.env` 中改 `PORT=8001`

### Q3: API 返回 401
检查请求头是否包含 `X-Token: thustory`（与 `.env` 中 `API_TOKEN` 一致）

### Q4: 连不上服务器
检查：1) pm2 状态 2) 安全组/防火墙是否放行 8000 端口 3) Windows 防火墙
