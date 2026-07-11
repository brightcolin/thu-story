import sys
from pathlib import Path

import pytest


BACKEND_DIR = Path(__file__).resolve().parents[1]
if str(BACKEND_DIR) not in sys.path:
    sys.path.insert(0, str(BACKEND_DIR))


@pytest.fixture
def fresh_db(tmp_path, monkeypatch):
    import database

    db_path = tmp_path / "qinghua_story_test.db"
    monkeypatch.setattr(database, "DB_NAME", str(db_path))
    database.init_db()
    return database


@pytest.fixture
def api_client(fresh_db):
    from fastapi.testclient import TestClient
    import main

    with TestClient(main.app) as client:
        client.headers["X-Token"] = main.API_TOKEN
        yield client
