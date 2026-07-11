import json
import sqlite3

import pytest


def _history_rows(database):
    conn = database.get_connection()
    conn.row_factory = __import__("sqlite3").Row
    rows = conn.execute(
        "SELECT * FROM gpa_history ORDER BY semester_index, id"
    ).fetchall()
    conn.close()
    return [dict(row) for row in rows]


def test_semester_transition_is_idempotent(fresh_db):
    database = fresh_db
    assert database.select_course(
        "math_analysis_1",
        [{"day_of_week": 0, "period": 1}],
        semester_index=0,
    )

    first_gpa, first_detail = database.semester_transition(0)
    first_state = database.get_player_state()

    second_gpa, second_detail = database.semester_transition(0)
    second_state = database.get_player_state()

    assert second_gpa == first_gpa
    assert second_detail == first_detail
    assert second_state["failed_credits"] == first_state["failed_credits"]
    assert len(_history_rows(database)) == 1


def test_semester_history_detail_only_contains_settled_semester(fresh_db):
    database = fresh_db
    assert database.select_course(
        "math_analysis_1",
        [{"day_of_week": 0, "period": 1}],
        semester_index=0,
    )
    assert database.select_course(
        "math_analysis_2",
        [{"day_of_week": 1, "period": 2}],
        semester_index=1,
    )
    conn = database.get_connection()
    conn.execute("UPDATE player_courses SET mastery=80 WHERE semester_index=0")
    conn.execute("UPDATE player_courses SET mastery=100 WHERE semester_index=1")
    conn.commit(); conn.close()

    cumulative_gpa, detail = database.semester_transition(0)
    history = _history_rows(database)

    assert cumulative_gpa == 3.0
    assert set(detail) == {"数学分析(1)"}
    assert set(json.loads(history[0]["detail"])) == {"数学分析(1)"}


def test_pending_transition_selects_finished_semester(fresh_db):
    database = fresh_db
    assert database.select_course(
        "math_analysis_1",
        [{"day_of_week": 0, "period": 1}],
        semester_index=0,
    )
    database.set_game_minutes(database.MINUTES_PER_DAY * database.DAYS_PER_SEMESTER)

    assert database.get_pending_semester_transition() == 0
    database.semester_transition(0)
    assert database.get_pending_semester_transition() is None


def test_init_migrates_duplicate_gpa_history(tmp_path, monkeypatch):
    import database

    db_path = tmp_path / "legacy.db"
    conn = sqlite3.connect(db_path)
    conn.execute(
        """CREATE TABLE gpa_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            semester_index INTEGER NOT NULL,
            semester_gpa REAL NOT NULL,
            cumulative_gpa REAL NOT NULL,
            detail TEXT DEFAULT '{}'
        )"""
    )
    conn.executemany(
        """INSERT INTO gpa_history
           (semester_index, semester_gpa, cumulative_gpa, detail)
           VALUES (0, ?, ?, '{}')""",
        [(2.0, 2.0), (3.0, 3.0)],
    )
    conn.commit(); conn.close()

    monkeypatch.setattr(database, "DB_NAME", str(db_path))
    database.init_db()

    conn = database.get_connection()
    rows = conn.execute(
        "SELECT semester_gpa FROM gpa_history WHERE semester_index=0"
    ).fetchall()
    assert rows == [(3.0,)]
    with pytest.raises(sqlite3.IntegrityError):
        conn.execute(
            """INSERT INTO gpa_history
               (semester_index, semester_gpa, cumulative_gpa, detail)
               VALUES (0, 4.0, 4.0, '{}')"""
        )
    conn.close()
