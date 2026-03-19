using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameShared.Generated.Data;
using GameShared.Proto;

/// <summary>
/// 던전 입장 팝업 UI.
/// - [E] 프롬프트 (포탈 근처에 있을 때)
/// - 던전 정보 (이름, 최소레벨, 최대인원, 설명)
/// - 입장 / 취소 버튼
/// </summary>
public class DungeonEntryPopup : MonoBehaviour
{
    public static DungeonEntryPopup Instance { get; private set; }

    [Header("프롬프트")]
    public GameObject promptPanel;          // "[E] 던전 입장" 패널
    public TMP_Text promptText;

    [Header("팝업")]
    public GameObject popupPanel;
    public TMP_Text dungeonNameText;
    public TMP_Text dungeonInfoText;        // 최소레벨, 최대인원, 설명
    public Button enterButton;
    public Button cancelButton;

    private DungeonData _currentDungeon;

    void Awake()
    {
        Instance = this;
        popupPanel.SetActive(false);
        if (promptPanel != null)
            promptPanel.SetActive(false);
    }

    void Start()
    {
        enterButton.onClick.AddListener(OnEnterClicked);
        cancelButton.onClick.AddListener(OnCancelClicked);
    }

    // ── 프롬프트 ─────────────────────────────────────────────────────────────

    public void ShowPrompt(bool show, string dungeonName)
    {
        if (promptPanel == null) return;
        promptPanel.SetActive(show);
        if (show && promptText != null)
            promptText.text = $"[E] {dungeonName} 입장";
    }

    // ── 팝업 ─────────────────────────────────────────────────────────────────

    public void Open(DungeonData data)
    {
        if (data == null) return;
        _currentDungeon = data;

        dungeonNameText.text = data.Name;
        dungeonInfoText.text =
            $"최소 레벨: {data.MinLevel}\n" +
            $"최대 인원: {data.MaxPlayers}인\n\n" +
            data.Description;

        popupPanel.SetActive(true);
    }

    private void OnEnterClicked()
    {
        if (_currentDungeon == null) return;
        popupPanel.SetActive(false);
        ShowPrompt(false, string.Empty);

        NetworkManager.Instance.SendEnterDungeon(_currentDungeon.DungeonId);
        Debug.Log($"[DungeonEntryPopup] Entering dungeon {_currentDungeon.DungeonId}: {_currentDungeon.Name}");
    }

    private void OnCancelClicked()
    {
        popupPanel.SetActive(false);
    }
}
