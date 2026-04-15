using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 던전 HUD — 플레이어 HP바, 경험치, 골드, 전투 정보를 표시한다.
/// Dungeon 씬의 Canvas 하위에 배치하고 DungeonManager가 이벤트를 통해 업데이트한다.
/// </summary>
public class DungeonHUD : MonoBehaviour
{
    public static DungeonHUD Instance { get; private set; }

    [Header("HP")]
    public Image hpFillImage;
    public TextMeshProUGUI hpText;

    [Header("정보")]
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI expText;
    public TextMeshProUGUI goldText;

    [Header("전투 로그")]
    public TextMeshProUGUI combatLogText;

    [Header("사망 UI")]
    public GameObject deathPanel;
    public Button returnToTownButton;

    [Header("클리어 UI")]
    public GameObject clearPanel;
    public TextMeshProUGUI clearTimeText;

    private int _currentHp;
    private int _maxHp;
    private float _combatLogTimer;
    private const float CombatLogDuration = 3f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (deathPanel != null)
            deathPanel.SetActive(false);
        if (clearPanel != null)
            clearPanel.SetActive(false);

        // 마을 복귀 버튼 이벤트 연결
        if (returnToTownButton != null)
            returnToTownButton.onClick.AddListener(OnReturnToTownClicked);
    }

    void Update()
    {
        // 전투 로그 자동 숨김
        if (_combatLogTimer > 0f)
        {
            _combatLogTimer -= Time.deltaTime;
            if (_combatLogTimer <= 0f && combatLogText != null)
                combatLogText.text = string.Empty;
        }
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _currentHp = currentHp;
        _maxHp = maxHp;

        if (hpFillImage != null)
            hpFillImage.fillAmount = maxHp > 0 ? (float)currentHp / maxHp : 0f;

        if (hpText != null)
            hpText.text = $"{currentHp} / {maxHp}";
    }

    public void SetLevel(int level)
    {
        if (levelText != null)
            levelText.text = $"Lv.{level}";
    }

    public void SetExp(int currentExp, int totalExp)
    {
        if (expText != null)
            expText.text = $"EXP: {currentExp}";
    }

    public void SetGold(int gold)
    {
        if (goldText != null)
            goldText.text = $"Gold: {gold}";
    }

    public void ShowCombatLog(string message)
    {
        if (combatLogText != null)
        {
            combatLogText.text = message;
            _combatLogTimer = CombatLogDuration;
        }
    }

    public void ShowDamage(int damage)
    {
        ShowCombatLog($"-{damage} HP");
    }

    public void ShowReward(int expReward, int goldReward)
    {
        ShowCombatLog($"+{expReward} EXP  +{goldReward} Gold");
    }

    public void ShowLevelUp(int newLevel)
    {
        ShowCombatLog($"LEVEL UP! Lv.{newLevel}");
    }

    public void ShowPlayerDeath()
    {
        ShowCombatLog("YOU DIED");
        _combatLogTimer = float.MaxValue;

        // 사망 패널 표시
        if (deathPanel != null)
            deathPanel.SetActive(true);
    }

    public void ShowDungeonClear(int clearTimeSeconds)
    {
        // 사망 패널이 열려 있어도 클리어 패널 우선
        if (deathPanel != null)
            deathPanel.SetActive(false);

        if (clearPanel != null)
            clearPanel.SetActive(true);

        int minutes = clearTimeSeconds / 60;
        int seconds = clearTimeSeconds % 60;
        if (clearTimeText != null)
            clearTimeText.text = $"던전 클리어!\n클리어 시간: {minutes:D2}:{seconds:D2}\n잠시 후 마을로 돌아갑니다.";

        ShowCombatLog("DUNGEON CLEAR!");
        _combatLogTimer = float.MaxValue;
    }

    private void OnReturnToTownClicked()
    {
        if (DungeonManager.Instance != null)
            DungeonManager.Instance.LeaveDungeon();
    }
}
