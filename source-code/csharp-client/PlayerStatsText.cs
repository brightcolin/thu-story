using UnityEngine;
using TMPro;

/// <summary>
/// 属性面板：t1–t9 依次为绩点、精力、健康、科研能力、社工能力、挂课学分、社工、实验室、SRT。
/// 未在 Inspector 拖引用时，会按子物体名称 t1…t9 自动绑定 TextMeshProUGUI。
/// </summary>
public class text : MonoBehaviour
{
    private PlayerManager pm;

    public TextMeshProUGUI t1;
    public TextMeshProUGUI t2;
    public TextMeshProUGUI t3;
    public TextMeshProUGUI t4;
    public TextMeshProUGUI t5;
    public TextMeshProUGUI t6;
    public TextMeshProUGUI t7;
    public TextMeshProUGUI t8;
    public TextMeshProUGUI t9;

    private void Awake()
    {
        pm = FindObjectOfType<PlayerManager>();
        BindByObjectName();
    }

    private void BindByObjectName()
    {
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            switch (tmp.gameObject.name)
            {
                case "t1": if (t1 == null) t1 = tmp; break;
                case "t2": if (t2 == null) t2 = tmp; break;
                case "t3": if (t3 == null) t3 = tmp; break;
                case "t4": if (t4 == null) t4 = tmp; break;
                case "t5": if (t5 == null) t5 = tmp; break;
                case "t6": if (t6 == null) t6 = tmp; break;
                case "t7": if (t7 == null) t7 = tmp; break;
                case "t8": if (t8 == null) t8 = tmp; break;
                case "t9": if (t9 == null) t9 = tmp; break;
            }
        }
    }

    private void Update()
    {
        if (pm == null) return;

        var s = pm.stats;
        string creditWarn = s.failed_credits >= 20 ? " ⚠退学风险" : "";

        if (t1) t1.text = s.FormatGpaPanelLine();
        if (t2) t2.text = $"精力: {s.energy}/100";
        if (t3) t3.text = $"健康: {s.health}/100";
        if (t4) t4.text = $"科研能力: {s.research_ability_100}/100";
        if (t5) t5.text = $"社工能力: {s.social_ability_100}/100";
        if (t6) t6.text = $"挂课学分: {s.failed_credits}{creditWarn}";
        if (t7) t7.text = FormatSocialOrgLine(s.social_org, s.social_rank);
        if (t8) t8.text = FormatLaboratoryLine(s.lab_status);
        if (t9) t9.text = FormatSrtLine(s.srt_project);
    }

    private static string FormatSocialOrgLine(string org, string rank)
    {
        string o = org switch
        {
            "student_union" => "学生会",
            "youth_league" => "团委",
            "science_assoc" => "科协",
            _ => null
        };
        string r = rank switch
        {
            "member" => "部员",
            "leader" => "骨干",
            "minister" => "部长",
            "president" => "主席",
            _ => null
        };
        if (string.IsNullOrEmpty(o))
            return "社工: 未加入组织";
        if (string.IsNullOrEmpty(r))
            return $"社工: {o}";
        return $"社工: {o} · {r}";
    }

    private static string FormatLaboratoryLine(string labStatus)
    {
        string inner = labStatus switch
        {
            "joined" => "已进组",
            "published" => "已发表",
            "none" => "未进组",
            _ => string.IsNullOrEmpty(labStatus) ? "未进组" : labStatus
        };
        return $"实验室: {inner}";
    }

    private static string FormatSrtLine(int srtProject)
    {
        return srtProject != 0 ? "SRT: 进行中" : "SRT: 无";
    }
}
