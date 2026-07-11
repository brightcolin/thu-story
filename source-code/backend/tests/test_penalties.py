def test_curfew_penalty_is_idempotent(fresh_db):
    import activity_system

    first = activity_system.apply_curfew_penalty()
    first_state = fresh_db.get_player_state()
    second = activity_system.apply_curfew_penalty()
    second_state = fresh_db.get_player_state()

    assert first["applied"] is True
    assert second["applied"] is False
    assert second_state["energy"] == first_state["energy"]
    assert second_state["health"] == first_state["health"]


def test_meal_penalty_is_idempotent(fresh_db):
    import activity_system

    # 当日 22:00，三餐截止时间均已过去。
    fresh_db.set_game_minutes(15 * 60 + 30)
    first = activity_system.apply_missed_meals_penalty()
    first_state = fresh_db.get_player_state()
    second = activity_system.apply_missed_meals_penalty()
    second_state = fresh_db.get_player_state()

    assert first["applied"] is True
    assert second["applied"] is False
    assert second_state["energy"] == first_state["energy"]
    assert second_state["health"] == first_state["health"]
