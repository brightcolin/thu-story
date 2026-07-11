def test_time_advance_rejects_invalid_ranges(api_client):
    assert api_client.post("/time/advance?minutes=-1").status_code == 422
    assert api_client.post("/time/advance?minutes=1111").status_code == 422


def test_patch_player_uses_absolute_values(api_client):
    response = api_client.patch("/player", json={"energy": 25, "health": 40})

    assert response.status_code == 200
    assert response.json()["player"]["energy"] == 25
    assert response.json()["player"]["health"] == 40


def test_course_schedule_validates_slots_and_conflicts(api_client):
    invalid = api_client.post(
        "/courses/select",
        json={"course_id": "math_analysis_1", "schedule": [{"day_of_week": 7, "period": 1}]},
    )
    assert invalid.status_code == 422

    first = api_client.post(
        "/courses/select",
        json={"course_id": "math_analysis_1", "schedule": [{"day_of_week": 0, "period": 1}]},
    )
    conflict = api_client.post(
        "/courses/select",
        json={"course_id": "linear_algebra", "schedule": [{"day_of_week": 0, "period": 1}]},
    )

    assert first.status_code == 200
    assert conflict.status_code == 400


def test_course_must_belong_to_current_semester(api_client):
    response = api_client.post(
        "/courses/select",
        json={"course_id": "math_analysis_2", "schedule": [{"day_of_week": 1, "period": 2}]},
    )

    assert response.status_code == 400


def test_manual_semester_transition_only_settles_finished_semester(api_client, fresh_db):
    selected = api_client.post(
        "/courses/select",
        json={"course_id": "math_analysis_1", "schedule": [{"day_of_week": 0, "period": 1}]},
    )
    assert selected.status_code == 200
    assert api_client.post("/semester/transition").status_code == 409

    fresh_db.set_game_minutes(fresh_db.MINUTES_PER_DAY * fresh_db.DAYS_PER_SEMESTER)
    settled = api_client.post("/semester/transition")

    assert settled.status_code == 200
    assert settled.json()["semester_settled"] == 0
    assert api_client.post("/semester/transition").status_code == 409
