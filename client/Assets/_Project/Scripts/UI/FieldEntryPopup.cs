using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameShared.Generated.Data;

/// <summary>
/// 시간제 사냥터 입장 팝업 UI.
/// - [E] 프롬프트 (포탈 근처에 있을 때)
/// - 필드 정보 (이름, 일일/주간 제한 시간, 설명)
/// - 입장 / 취소 버튼
/// </summary>
public class FieldEntryPopup : MonoBehaviour
{
    public static FieldEntryPopup Instance { get; private set; }

    [Header("프롬프트")]
    public GameObject promptPanel;
    public TMP_Text promptText;

    [Header("팝업")]
    public GameObject popupPanel;
    public TMP_Text fieldNameText;
    public TMP_Text fieldInfoText;    // 설명 + 일일/주간 제한
    public Button enterButton;
    public Button cancelButton;

    private TimeLimitedFieldData _currentField;

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

    public void ShowPrompt(bool show, string fieldName)
    {
        if (promptPanel == null) return;
        promptPanel.SetActive(show);
        if (show && promptText != null)
            promptText.text = $"[E] {fieldName} 입장";
        if (!show)
            popupPanel.SetActive(false);
    }

    // ── 팝업 ─────────────────────────────────────────────────────────────────

    public void Open(TimeLimitedFieldData data)
    {
        if (data == null) return;
        _currentField = data;

        fieldNameText.text = data.Name;
        fieldInfoText.text =
            $"{data.Description}\n\n" +
            $"일일 제한: {data.DailyLimitMinutes}분\n" +
            $"주간 제한: {data.WeeklyLimitMinutes}분";

        popupPanel.SetActive(true);
    }

    private void OnEnterClicked()
    {
        if (_currentField == null) return;
        popupPanel.SetActive(false);
        ShowPrompt(false, string.Empty);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);

        NetworkManager.Instance.SendEnterField(_currentField.FieldId);
        Debug.Log($"[FieldEntryPopup] Entering field {_currentField.FieldId}: {_currentField.Name}");
    }

    private void OnCancelClicked()
    {
        popupPanel.SetActive(false);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }
}
