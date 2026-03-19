using UnityEngine;
using GameShared.Data;

/// <summary>
/// StreamingAssets/Data/*.bytes 파일을 읽어 GameDataManager를 초기화한다.
/// 씬 전환 후에도 한 번만 실행된다 (DontDestroyOnLoad + IsLoaded 체크).
/// </summary>
public class GameDataInitializer : MonoBehaviour
{
    public static GameDataInitializer Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!GameDataManager.Instance.IsLoaded)
        {
            var dataPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Data");
            try
            {
                GameDataManager.Instance.Initialize(dataPath);
                Debug.Log("[GameDataInitializer] GameDataManager loaded successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataInitializer] Failed to load game data: {ex.Message}");
            }
        }
    }
}
