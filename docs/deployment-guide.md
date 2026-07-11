# 后端部署说明 v2.2

> [!WARNING]
> 当前后端是单人、单 SQLite 存档设计。一个服务实例不应供多个互不信任的玩家共享。公网部署必须使用 HTTPS、更换默认令牌并限制跨域来源。

## 1. 环境要求

- Python 3.10+
- Linux 或 Windows Server；Linux 推荐使用 systemd
- SQLite，无需额外数据库服务
- 反向代理与 HTTPS，例如 Nginx + Let's Encrypt

生产环境建议让 FastAPI 只监听 `127.0.0.1:8000`，由反向代理公开 443 端口。不要直接在云安全组中开放 8000。

## 2. 部署文件

将以下内容复制到服务器，例如 `/opt/thustory`：

```text
database.py
activity_system.py
main.py
npc_engine.py
requirements.txt
.env.example
```

创建环境并安装依赖：

```bash
cd /opt/thustory
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

Windows PowerShell 使用：

```powershell
cd C:\thustory_backend
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env
```

## 3. 环境变量

生产环境至少应修改：

```dotenv
# 可选；留空时 NPC 使用离线降级回复
DEEPSEEK_API_KEY=

# 必须替换为随机长令牌，并与客户端配置一致
API_TOKEN=replace-with-a-long-random-token

HOST=127.0.0.1
PORT=8000
DEBUG=false
QINGHUA_DB_PATH=/opt/thustory/data/qinghua_story.db

# 改成实际前端来源；多个来源以逗号分隔
ALLOWED_ORIGINS=https://game.example.com
```

可用 Python 生成令牌：

```bash
python -c "import secrets; print(secrets.token_urlsafe(32))"
```

不要把实际 `.env`、DeepSeek 密钥或生产令牌提交到 Git。

## 4. 首次启动与验证

```bash
mkdir -p /opt/thustory/data
python main.py
```

本机验证：

```bash
curl http://127.0.0.1:8000/health
```

`/health` 不需要令牌；其他接口需要 `X-Token` 请求头。

## 5. systemd 服务

```ini
# /etc/systemd/system/thustory.service
[Unit]
Description=ThuStory Backend
After=network.target

[Service]
Type=simple
User=thustory
Group=thustory
WorkingDirectory=/opt/thustory
EnvironmentFile=/opt/thustory/.env
Environment=PYTHONIOENCODING=utf-8
ExecStart=/opt/thustory/venv/bin/python /opt/thustory/main.py
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now thustory
sudo systemctl status thustory
```

## 6. HTTPS 反向代理

下面是最小 Nginx 代理示例；证书可通过 Certbot 配置：

```nginx
server {
    listen 443 ssl http2;
    server_name api.example.com;

    ssl_certificate /etc/letsencrypt/live/api.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}
```

客户端应配置 `https://api.example.com`，不要在生产环境启用 Unity 的全局明文 HTTP 许可。

## 7. SQLite 备份与升级

升级前先停止服务并备份数据库：

```bash
sudo systemctl stop thustory
cp /opt/thustory/data/qinghua_story.db /opt/thustory/data/qinghua_story.db.bak
sudo systemctl start thustory
```

从早期 v2.1 数据库升级时，代码会补充部分字段和索引，但课程表结构变化较大。比赛存档不需要保留时，最稳妥的方式仍是备份后重建数据库；需要保留存档时，应先在数据库副本上验证迁移。

## 8. 常见问题

### API 返回 401

确认客户端的 `X-Token` 与服务端 `.env` 中的 `API_TOKEN` 完全一致。

### NPC 只返回离线回复

检查 `DEEPSEEK_API_KEY` 是否配置，并查看服务日志。没有密钥不会影响其他游戏功能。

### SQLite 出现锁等待

当前代码启用了 WAL、外键和 5 秒忙等待，但仍只适合单实例、低并发运行。不要同时启动多个写入同一数据库的后端进程。

### Windows 控制台编码错误

启动前设置：

```powershell
$env:PYTHONIOENCODING = "utf-8"
```
