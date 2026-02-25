using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameShared.Enums;
using GameShared.Proto;

/// <summary>
/// 채팅 UI: 메시지 표시 + 입력 전송
/// </summary>
public class ChatUI : MonoBehaviour
{
    [Header("UI 요소")]
    public TMP_Text chatLog;
    public TMP_InputField chatInput;
    public Button sendButton;

    [Header("최대 표시 줄 수")]
    public int maxLines = 50;

    private int _lineCount;

    void Start()
    {
        // Inspector 연결 확인
        if (chatLog == null)   { Debug.LogError("[ChatUI] chatLog is not assigned!"); return; }
        if (chatInput == null) { Debug.LogError("[ChatUI] chatInput is not assigned!"); return; }
        if (sendButton == null){ Debug.LogError("[ChatUI] sendButton is not assigned!"); return; }

        sendButton.onClick.AddListener(SendChat);
        chatInput.onSubmit.AddListener(_ => SendChat());

        NetworkManager.Instance.OnChatReceived += OnChatMessage;

        chatLog.text = string.Empty;
        Debug.Log("[ChatUI] Initialized OK");
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnChatReceived -= OnChatMessage;
    }

    private void SendChat()
    {
        string msg = chatInput.text.Trim();
        Debug.Log($"[ChatUI] SendChat called, msg='{msg}'");
        if (string.IsNullOrEmpty(msg)) return;

        NetworkManager.Instance.Send(PacketId.C2S_Chat, new C2S_Chat { Message = msg });
        chatInput.text = string.Empty;
        chatInput.ActivateInputField(); // 포커스 유지
    }

    private void OnChatMessage(S2C_Chat packet)
    {
        AppendLine($"[{packet.SenderName}] {packet.Message}");
    }

    private void AppendLine(string line)
    {
        if (_lineCount >= maxLines)
        {
            // 첫 줄 제거
            int idx = chatLog.text.IndexOf('\n');
            chatLog.text = idx >= 0 ? chatLog.text[(idx + 1)..] : string.Empty;
        }
        else
        {
            _lineCount++;
        }

        chatLog.text += (_lineCount > 1 ? "\n" : string.Empty) + line;
    }
}
