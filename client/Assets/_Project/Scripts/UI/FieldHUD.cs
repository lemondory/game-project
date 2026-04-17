using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Field 씬 HUD — 쿼터(일간/주간 남은 시간), HP, 전투 정보를 표시한다.
/// </summary>
public class FieldHUD : MonoBehaviour
{
    public static FieldHUD Instance { get; private set; }

    [Header("HP")]
    public Image hpFillImage;
    public TextMeshProUGUI hpText;

    [Header("정보")]
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI expText;
    public TextMeshProUGUI goldText;

    [Header("쿼터 (입장 가능 시간)")]
    public TextMeshProUGUI dailyQuotaText;   // 오늘 남은 시간
    public TextMeshProUGUI weeklyQuotaText;  // 이번 주 남은 시간

    [Header("전투 로그")]
    public TextMeshProUGUI combatLogText;

    [Header("사망 UI")]
    public GameObject deathPanel;
    public Button returnToTownButton;

    private float _combatLogTimer;
    private const float CombatLogDuration = 3f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (deathPanel != null) deathPanel.SetActive(false);
        if (returnToTownButton != null)
            returnToTownButton.onClick.AddListener(OnReturnToTownClicked);
    }

    void Update()
    {
        if (_combatLogTimer > 0f)
        {
            _combatLogTimer -= Time.deltaTime;
            if (_combatLogTimer <= 0f && combatLogText != null)
                combatLogText.text = string.Empty;
        }
    }

    // ── HP ──────────────────────────────────────────────────────────────────

    public void SetHealth(int currentHp, int maxHp)
    {
        if (hpFillImage != null)
            hpFillImage.fillAmount = maxHp > 0 ? (float)currentHp / maxHp : 0f;
        if (hpText != null)
            hpText.text = $"{currentHp} / {maxHp}";
    }

    // ── 정보 ────────────────────────────────────────────────────────────────

    public void SetLevel(int level)
    {
        if (levelText != null) levelText.text = $"Lv.{level}";
    }

    public void SetExp(int currentExp, int totalExp)
    {
        if (expText != null) expText.text = $"EXP: {currentExp}";
    }

    public void SetGold(int gold)
    {
        if (goldText != null) goldText.text = $"Gold: {gold}";
    }

    // ── 쿼터 ────────────────────────────────────────────────────────────────

    /// <summary>서버에서 수신한 일간/주간 남은 시간(초)으로 UI를 갱신한다.</summary>
    public void UpdateQuota(int dailyRemainingSeconds, int weeklyRemainingSeconds)
    {
        if (dailyQuotaText != null)
        {
            int m = dailyRemainingSeconds / 60;
            int s = dailyRemainingSeconds % 60;
            dailyQuotaText.text = $"오늘 {m:D2}:{s:D2}";

            // 5분 이하 경고색
            dailyQuotaText.color = dailyRemainingSeconds <= 300 ? Color.red : Color.white;
        }

        if (weeklyQuotaText != null)
        {
            int m = weeklyRemainingSeconds / 60;
            int s = weeklyRemainingSeconds % 60;
            weeklyQuotaText.text = $"이번 주 {m:D2}:{s:D2}";
            weeklyQuotaText.color = weeklyRemainingSeconds <= 600 ? Color.yellow : Color.white;
        }
    }

    // ── 전투 로그 ────────────────────────────────────────────────────────────

    public void ShowCombatLog(string message)
    {
        if (combatLogText != null)
        {
            combatLogText.text = message;
            _combatLogTimer = CombatLogDuration;
        }
    }

    public void ShowDamage(int damage)         => ShowCombatLog($"-{damage} HP");
    public void ShowReward(int exp, int gold)  => ShowCombatLog($"+{exp} EXP  +{gold} Gold");
    public void ShowLevelUp(int level)         => ShowCombatLog($"LEVEL UP! Lv.{level}");

    public void ShowPlayerDeath()
    {
        ShowCombatLog("YOU DIED");
        _combatLogTimer = float.MaxValue;
        if (deathPanel != null) deathPanel.SetActive(true);
    }

    // ── 버튼 ────────────────────────────────────────────────────────────────

    private void OnReturnToTownClicked()
    {
        if (FieldManager.Instance != null)
            FieldManager.Instance.LeaveField();
    }
}
