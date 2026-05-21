using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 好感度本地持久化。与时间系统绑定：退出时保存、启动时加载；按 Restart 时与时间一起清除。
/// 挂在 GameManager 或任意持久化对象上，与 PlayerManager 配合使用。
/// </summary>
public class FriendshipPersistence : MonoBehaviour
{
    [Header("设置")]
    [Tooltip("文件名，存于 Application.persistentDataPath")]
    public string fileName = "friendships.json";

    [Tooltip("好感度变化后延迟保存（秒），避免频繁写盘")]
    public float saveDelay = 0.5f;

    [Tooltip("默认关闭：好感度以服务端 GET /player 为准。仅离线缓存时可开启。")]
    public bool persistToDisk = false;

    private string FilePath => Path.Combine(Application.persistentDataPath, fileName);
    private float _saveTimer;
    private bool _dirty;

    private void OnEnable()
    {
        PlayerManager.FriendshipChanged += OnFriendshipChanged;
    }

    private void OnDisable()
    {
        PlayerManager.FriendshipChanged -= OnFriendshipChanged;
        if (_dirty && persistToDisk) SaveToFile();
    }

    private void Start()
    {
        var pm = PlayerManager.Instance;
        if (persistToDisk && (pm == null || !pm.serverFriendshipsAuthoritative))
            LoadFromFile();
    }

    private void OnApplicationQuit()
    {
        if (persistToDisk && !IsGameOver()) SaveToFile();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause && persistToDisk && !IsGameOver()) SaveToFile();
    }

    private bool IsGameOver()
    {
        var pm = PlayerManager.Instance;
        return pm != null && pm.stats != null && pm.stats.is_game_over_server;
    }

    private void Update()
    {
        if (!persistToDisk || !_dirty || saveDelay <= 0f) return;
        _saveTimer += Time.deltaTime;
        if (_saveTimer >= saveDelay)
        {
            _saveTimer = 0f;
            _dirty = false;
            SaveToFile();
        }
    }

    private void OnFriendshipChanged(string npcId, int value)
    {
        if (!persistToDisk) return;
        _dirty = true;
        if (saveDelay <= 0f)
        {
            SaveToFile();
            _dirty = false;
        }
    }

    /// <summary>从本地文件加载好感度并写入 PlayerManager</summary>
    public void LoadFromFile()
    {
        var pm = PlayerManager.Instance;
        if (pm == null) return;

        if (!File.Exists(FilePath))
        {
            Debug.Log("[FriendshipPersistence] 无本地记录，跳过加载");
            return;
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            var data = JsonUtility.FromJson<FriendshipsData>(json);
            if (data?.entries != null && data.entries.Count > 0)
            {
                var dict = new Dictionary<string, int>();
                foreach (var e in data.entries)
                {
                    if (!string.IsNullOrEmpty(e.npcId))
                        dict[e.npcId] = Mathf.Clamp(e.value, 0, 100);
                }
                pm.LoadFriendships(dict);
                Debug.Log($"[FriendshipPersistence] 已加载 {dict.Count} 条好感度记录");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FriendshipPersistence] 加载失败: " + e.Message);
        }
    }

    /// <summary>将当前好感度保存到本地</summary>
    public void SaveToFile()
    {
        var pm = PlayerManager.Instance;
        if (pm == null) return;

        var data = pm.GetAllFriendshipsForSave();
        if (data == null || data.entries == null || data.entries.Count == 0)
            return;

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"[FriendshipPersistence] 已保存 {data.entries.Count} 条好感度到 {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FriendshipPersistence] 保存失败: " + e.Message);
        }
    }

    /// <summary>立即保存（供外部调用，如场景切换前）</summary>
    public void SaveNow()
    {
        _dirty = false;
        SaveToFile();
    }

    /// <summary>清除本地存档与内存数据，供 Restart 键调用（与时间系统绑定）</summary>
    public void ClearSavedData()
    {
        _dirty = false;
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
                Debug.Log("[FriendshipPersistence] 已清除本地好感度存档");
            }
        }
        catch (Exception e) { Debug.LogWarning("[FriendshipPersistence] 清除失败: " + e.Message); }

        if (PlayerManager.Instance != null)
            PlayerManager.Instance.ClearFriendships();
    }
}

// JsonUtility 不支持 Dictionary，用可序列化结构
[Serializable]
public class FriendshipsData
{
    public List<FriendshipEntry> entries = new();
}

[Serializable]
public class FriendshipEntry
{
    public string npcId;
    public int value;
}
