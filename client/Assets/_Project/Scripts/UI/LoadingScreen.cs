using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

/// <summary>
/// 씬 전환 시 로딩 화면을 표시한다.
/// DontDestroyOnLoad 싱글톤으로 동작하며, LoadScene() 호출 시 로딩 UI를 보여준 후 비동기 씬 로드를 수행한다.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    [Header("UI")]
    public Canvas loadingCanvas;
    public Image progressFillImage;
    public TextMeshProUGUI loadingText;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 초기에는 로딩 화면 숨김
        if (loadingCanvas != null)
            loadingCanvas.gameObject.SetActive(false);
    }

    /// <summary>로딩 화면을 표시하며 비동기로 씬을 전환한다.</summary>
    public void LoadScene(string sceneName)
    {
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        // 로딩 UI 표시
        if (loadingCanvas != null)
            loadingCanvas.gameObject.SetActive(true);

        if (loadingText != null)
            loadingText.text = "Loading...";

        if (progressFillImage != null)
            progressFillImage.fillAmount = 0f;

        // 비동기 씬 로드
        var asyncOperation = SceneManager.LoadSceneAsync(sceneName);
        asyncOperation.allowSceneActivation = false;

        while (asyncOperation.progress < 0.9f)
        {
            if (progressFillImage != null)
                progressFillImage.fillAmount = asyncOperation.progress;

            yield return null;
        }

        // 로드 완료 — 프로그레스 바 채움
        if (progressFillImage != null)
            progressFillImage.fillAmount = 1f;

        // 짧은 대기 후 씬 활성화 (시각적 완료감)
        yield return new WaitForSeconds(0.3f);

        asyncOperation.allowSceneActivation = true;

        // 씬 활성화 대기
        while (!asyncOperation.isDone)
            yield return null;

        // 로딩 UI 숨김
        if (loadingCanvas != null)
            loadingCanvas.gameObject.SetActive(false);
    }
}
