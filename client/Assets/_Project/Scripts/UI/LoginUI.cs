using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameShared.Enums;
using GameShared.Proto;

public class LoginUI : MonoBehaviour
{
    [Header("입력 필드")]
    public TMP_InputField usernameInputField;
    public TMP_InputField passwordInputField;

    [Header("버튼")]
    public Button loginButton;

    [Header("상태 텍스트")]
    public TMP_Text statusText;

    [Header("서버 설정")]
    public string serverHost = "127.0.0.1";
    public int serverPort = 7777;

    void Start()
    {

        #if DEBUG
        usernameInputField.text = "testuser1";
        passwordInputField.text = "test";
        #endif
        loginButton.onClick.AddListener(OnLoginButtonClicked);

        NetworkManager.Instance.OnConnectedToServer += OnConnectedToServer;
        NetworkManager.Instance.OnDisconnectedFromServer += OnDisconnectedFromServer;

        SetStatus("", false);
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnConnectedToServer -= OnConnectedToServer;
            NetworkManager.Instance.OnDisconnectedFromServer -= OnDisconnectedFromServer;
        }
    }

    private void OnLoginButtonClicked()
    {
        string username = usernameInputField.text.Trim();
        string password = passwordInputField.text;

        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Enter username.", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter password.", true);
            return;
        }

        SetStatus("Connecting...", false);
        SetInputEnabled(false);

        NetworkManager.Instance.Connect(serverHost, serverPort);
    }

    private void OnConnectedToServer()
    {
        SetStatus("Logging in...", false);

        NetworkManager.Instance.Send(PacketId.C2S_Login, new C2S_Login
        {
            Username = usernameInputField.text.Trim(),
            Password = passwordInputField.text
        });
    }

    private void OnDisconnectedFromServer()
    {
        SetStatus("Disconnected from server.", true);
        SetInputEnabled(true);
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = isError ? Color.red : Color.white;
    }

    private void SetInputEnabled(bool enabled)
    {
        usernameInputField.interactable = enabled;
        passwordInputField.interactable = enabled;
        loginButton.interactable = enabled;
    }
}
