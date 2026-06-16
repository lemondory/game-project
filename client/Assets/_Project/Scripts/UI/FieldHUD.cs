using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 시간제 사냥터 HUD — DungeonHUD와 동일한 항목 구조.
/// 타이머/킬카운트 대신 일간/주간 남은 입장 시간을 표시한다.
/// 클리어 개념이 없으므로 결과 패널 없음.
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
    public Button respawnButton;       // 입구에서 부활
    public Button returnToTownButton;  // 마을로 이동

    private float _combatLogTimer;
    private const float CombatLogDuration = 3f;

    // 클라이언트 로컬 카운트다운 (서버 60초 브로드캐스트 사이 실시간 갱신용)
    private float _dailyRemainingSeconds;
    private float _weeklyRemainingSeconds;
    private bool  _quotaActive;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (deathPanel != null) deathPanel.SetActive(false);

        if (respawnButton != null)
            respawnButton.onClick.AddListener(OnRespawnClicked);
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

        if (_quotaActive)
        {
            _dailyRemainingSeconds  = Mathf.Max(0f, _dailyRemainingSeconds  - Time.deltaTime);
            _weeklyRemainingSeconds = Mathf.Max(0f, _weeklyRemainingSeconds - Time.deltaTime);
            RefreshQuotaText();
        }
    }

    // ── HP ──────────────────────────────────────────────────────

    public void SetHealth(int currentHp, int maxHp)
    {
        if (hpFillImage != null)
            hpFillImage.fillAmount = maxHp > 0 ? (float)currentHp / maxHp : 0f;
        if (hpText != null)
            hpText.text = $"{currentHp} / {maxHp}";
    }

    // ── 정보 ─────────────────────────────────────────────────────

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

    // ── 쿼터 ─────────────────────────────────────────────────────

    /// <summary>서버에서 수신한 값으로 동기화 (입장 시 초기값 + 60초마다 서버 보정).</summary>
    public void UpdateQuota(int dailyRemainingSeconds, int weeklyRemainingSeconds)
    {
        _dailyRemainingSeconds  = dailyRemainingSeconds;
        _weeklyRemainingSeconds = weeklyRemainingSeconds;
        _quotaActive = true;
        RefreshQuotaText();
    }

    public void StopQuota()
    {
        _quotaActive = false;
    }

    private void RefreshQuotaText()
    {
        if (dailyQuotaText != null)
        {
            int m = (int)_dailyRemainingSeconds / 60;
            int s = (int)_dailyRemainingSeconds % 60;
            dailyQuotaText.text  = $"오늘 {m:D2}:{s:D2}";
            dailyQuotaText.color = _dailyRemainingSeconds <= 300f ? Color.red : Color.white;
        }

        if (weeklyQuotaText != null)
        {
            int m = (int)_weeklyRemainingSeconds / 60;
            int s = (int)_weeklyRemainingSeconds % 60;
            weeklyQuotaText.text  = $"이번 주 {m:D2}:{s:D2}";
            weeklyQuotaText.color = _weeklyRemainingSeconds <= 600f ? Color.yellow : Color.white;
        }
    }

    // ── 전투 로그 ────────────────────────────────────────────────

    public void ShowCombatLog(string message)
    {
        if (combatLogText != null)
        {
            combatLogText.text = message;
            _combatLogTimer = CombatLogDuration;
        }
    }

    public void ShowDamage(int damage)            => ShowCombatLog($"-{damage} HP");
    public void ShowReward(int exp, int gold)     => ShowCombatLog($"+{exp} EXP  +{gold} Gold");
    public void ShowLevelUp(int level)            => ShowCombatLog($"LEVEL UP! Lv.{level}");

    // ── 사망 ─────────────────────────────────────────────────────

    public void ShowPlayerDeath()
    {
        ShowCombatLog("YOU DIED");
        _combatLogTimer = float.MaxValue;
        if (deathPanel != null) deathPanel.SetActive(true);
    }

    public void HideDeathPanel()
    {
        if (combatLogText != null) combatLogText.text = string.Empty;
        _combatLogTimer = 0f;
        if (deathPanel != null) deathPanel.SetActive(false);
    }

    // ── 버튼 ─────────────────────────────────────────────────────

    private void OnRespawnClicked()
    {
        HideDeathPanel();
        if (FieldManager.Instance != null)
            FieldManager.Instance.RequestRespawn();
    }

    private void OnReturnToTownClicked()
    {
        HideDeathPanel();
        if (FieldManager.Instance != null)
            FieldManager.Instance.LeaveField();
    }
}
