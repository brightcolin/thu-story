using System;
using System.IO;

/// <summary>Debug mode：NDJSON 追加到 .cursor/debug-9bc6ed.log（会话 9bc6ed）。</summary>
internal static class DebugSessionNdjson
{
    private const string Path = "/Users/thuee25/Downloads/0408thustory/.cursor/debug-9bc6ed.log";
    private const string SessionId = "9bc6ed";

    // #region agent log
    public static void CurfewDecision(
        string hypothesisId,
        string message,
        int hour,
        int minute,
        float totalGameMinutes,
        int deltaNext,
        bool totalUsable,
        bool deltaHit,
        bool hour24Branch,
        bool hourOnlyHit,
        bool finalResult,
        string phase,
        string phaseName)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string ph = (phase ?? "").Replace("\\", "\\\\").Replace("\"", "'");
            string pn = (phaseName ?? "").Replace("\\", "\\\\").Replace("\"", "'");
            string tot = totalGameMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string line =
                "{\"sessionId\":\"" + SessionId + "\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"CurfewTimeV21.IsPastOneAmCurfew\",\"message\":\"" + message +
                "\",\"timestamp\":" + ts +
                ",\"data\":{\"hour\":" + hour + ",\"minute\":" + minute +
                ",\"total_game_minutes\":" + tot +
                ",\"deltaNext\":" + deltaNext +
                ",\"totalUsable\":" + (totalUsable ? "true" : "false") +
                ",\"deltaHit\":" + (deltaHit ? "true" : "false") +
                ",\"hour24Branch\":" + (hour24Branch ? "true" : "false") +
                ",\"hourOnlyHit\":" + (hourOnlyHit ? "true" : "false") +
                ",\"finalResult\":" + (finalResult ? "true" : "false") +
                ",\"phase\":\"" + ph + "\",\"phase_name\":\"" + pn + "\"}}\n";
            File.AppendAllText(Path, line);
        }
        catch
        {
            // ignore
        }
    }

    public static void CurfewUiForced(string hypothesisId, string message, int scene, int block)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line =
                "{\"sessionId\":\"" + SessionId + "\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"playercontrol.ForcedDormSleepFromCurfewRoutine\",\"message\":\"" + message +
                "\",\"timestamp\":" + ts +
                ",\"data\":{\"tr_scene\":" + scene + ",\"gameDayBlock\":" + block + "}}\n";
            File.AppendAllText(Path, line);
        }
        catch
        {
            // ignore
        }
    }

    public static void CurfewMonitorEval(
        string hypothesisId,
        string timeSource,
        string outcome,
        bool inCurfew,
        int hour,
        int minute,
        int scene,
        bool busy,
        bool uiOpen,
        int block)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line =
                "{\"sessionId\":\"" + SessionId + "\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"LateNightCurfewMonitor\",\"message\":\"monitor_eval\"," +
                "\"timestamp\":" + ts +
                ",\"data\":{\"timeSource\":\"" + timeSource +
                "\",\"outcome\":\"" + outcome +
                "\",\"inCurfew\":" + (inCurfew ? "true" : "false") +
                ",\"hour\":" + hour + ",\"minute\":" + minute +
                ",\"scene\":" + scene +
                ",\"busy\":" + (busy ? "true" : "false") +
                ",\"uiOpen\":" + (uiOpen ? "true" : "false") +
                ",\"block\":" + block + "}}\n";
            File.AppendAllText(Path, line);
        }
        catch
        {
            // ignore
        }
    }

    public static void CurfewClosedNpcOverlays(bool closedAiChat, bool closedDialogueBox)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line =
                "{\"sessionId\":\"" + SessionId + "\",\"hypothesisId\":\"H7\",\"location\":\"playercontrol.CloseNpcOverlaysForCurfew\",\"message\":\"pre_curfew_close_npc\"," +
                "\"timestamp\":" + ts +
                ",\"data\":{\"closedAiChat\":" + (closedAiChat ? "true" : "false") +
                ",\"closedDialogueBox\":" + (closedDialogueBox ? "true" : "false") + "}}\n";
            File.AppendAllText(Path, line);
        }
        catch
        {
            // ignore
        }
    }

    public static void LibraryStudyCompleted(string hypothesisId, int hour, int minute, float total)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string tot = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string line =
                "{\"sessionId\":\"" + SessionId + "\",\"hypothesisId\":\"" + hypothesisId +
                "\",\"location\":\"playercontrol.RunLibrarySelfStudyFromUi\",\"message\":\"library_success_ui\"," +
                "\"timestamp\":" + ts + ",\"data\":{\"hour\":" + hour + ",\"minute\":" + minute +
                ",\"total_game_minutes\":" + tot + "}}\n";
            File.AppendAllText(Path, line);
        }
        catch
        {
            // ignore
        }
    }
    // #endregion
}
