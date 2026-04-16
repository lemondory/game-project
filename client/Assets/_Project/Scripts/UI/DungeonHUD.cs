using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 던전 HUD — 플레이어 HP바, 타이머, 킬카운트, 전투 로그를 표시한다.
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

    [Header("던전 상태")]
    public TextMeshProUGUI timerText;     // 남은 시간 카운트다운
    public TextMeshProUGUI killCountText; // 처치 수 (Timed 던전)

    [Header("전투 로그")]
    public TextMeshProUGUI combatLogText;

    [Header("사망 UI")]
    public GameObject deathPanel;
    public Button returnToTownButton;

    [Header("결과 UI")]
    public GameObject resultPanel;
    public TextMeshProUGUI resultTitleText; // DUNGEON CLEAR! / TIME OVER
    public TextMeshProUGUI resultDetailText;

    private float _combatLogTimer;
    private const float CombatLogDuration = 3f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (deathPanel != null)   deathPanel.SetActive(false);
        if (resultPanel != null)  resultPanel.SetActive(false);

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

    // ── 타이머 / 킬카운트 ────────────────────────────────────────

    /// <summary>서버에서 1초마다 수신되는 타이머 업데이트.</summary>
    public void UpdateTimer(int remainingSeconds, int killCount)
    {
        if (timerText != null)
        {
            int m = remainingSeconds / 60;
            int s = remainingSeconds % 60;
            timerText.text = $"{m:D2}:{s:D2}";

            // 30초 이하이면 빨간색으로 강조
            timerText.color = remainingSeconds <= 30 ? Color.red : Color.white;
        }

        if (killCountText != null)
            killCountText.text = $"Kill: {killCount}";
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

    public void ShowDamage(int damage)    => ShowCombatLog($"-{damage} HP");
    public void ShowReward(int exp, int gold) => ShowCombatLog($"+{exp} EXP  +{gold} Gold");
    public void ShowLevelUp(int level)    => ShowCombatLog($"LEVEL UP! Lv.{level}");

    // ── 사망 ─────────────────────────────────────────────────────

    public void ShowPlayerDeath()
    {
        ShowCombatLog("YOU DIED");
        _combatLogTimer = float.MaxValue;
        if (deathPanel != null) deathPanel.SetActive(true);
    }

    // ── 던전 결과 ────────────────────────────────────────────────

    /// <summary>
    /// 던전 종료 결과 패널 표시.
    /// isCleared=true  → 클리어 (KillAll 전멸 or Timed 시간 종료)
    /// isCleared=false → 실패 (KillAll 시간 초과)
    /// </summary>
    public void ShowDungeonResult(bool isCleared, int timeSeconds, int killCount)
    {
        if (deathPanel != null) deathPanel.SetActive(false);

        if (resultPanel != null) resultPanel.SetActive(true);

        int m = timeSeconds / 60;
        int s = timeSeconds % 60;

        if (resultTitleText != null)
        {
            resultTitleText.text  = isCleared ? "DUNGEON CLEAR!" : "TIME OVER";
            resultTitleText.color = isCleared ? Color.yellow : Color.red;
        }

        if (resultDetailText != null)
        {
            if (isCleared)
                resultDetailText.text = $"클리어 시간: {m:D2}:{s:D2}\n처치: {killCount}\n잠시 후 마을로 돌아갑니다.";
            else
                resultDetailText.text = $"제한 시간 초과\n처치: {killCount}\n잠시 후 마을로 돌아갑니다.";
        }

        ShowCombatLog(isCleared ? "DUNGEON CLEAR!" : "TIME OVER...");
        _combatLogTimer = float.MaxValue;
    }

    // ── 버튼 ─────────────────────────────────────────────────────

    private void OnReturnToTownClicked()
    {
        if (DungeonManager.Instance != null)
            DungeonManager.Instance.LeaveDungeon();
    }
}
