using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

public class WebSocketClient : MonoBehaviour
{
    public static WebSocketClient Instance { get; private set; }

    [Header("Connection")]
    [SerializeField] private string baseUrl = "wss://dev-api.uiseong.ai.kr";
    [SerializeField] private string boothId = "17";
    [SerializeField] private string boothSecret = "bsk_n2yTvziKEKs3xNxO";

    [Header("Reconnect")]
    [SerializeField] private float initialBackoffSeconds = 1f;
    [SerializeField] private float maxBackoffSeconds = 30f;

    [Header("ACK Timeout")]
    [SerializeField] private float ackTimeoutSeconds = 5f;

    WebSocket ws;
    float currentBackoff;
    bool reconnectDisabled;

    public int CurrentSessionId { get; private set; }
    public string CurrentStartToken { get; private set; }

    Coroutine ackWatchdog;
    int ackPendingSessionId;

    public event Action<int, string> OnSessionStarted;   // (sessionId, startToken)
    public event Action<int> OnAck;                       // sessionId
    public event Action<int, string> OnNack;              // (sessionId, reason)
    public event Action<int, string, APIManager.ResultInner> OnResultReady;  // (sessionId, qrPayload, result)
    public event Action<int, string> OnResultFailed;                          // (sessionId, reason)

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        currentBackoff = initialBackoffSeconds;
    }

    async void Start()
    {
        await Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    async Task Connect()
    {
        if (reconnectDisabled) return;

        string url = $"{baseUrl}/ws/experience";
        var headers = new Dictionary<string, string>
        {
            { "X-Booth-Id", boothId },
            { "X-Booth-Secret", boothSecret },
        };

        ws = new WebSocket(url, headers);
        ws.OnOpen += () =>
        {
            Debug.Log("[WS] Connected (handshake OK, waiting for START_SESSION)");
            currentBackoff = initialBackoffSeconds;
        };
        ws.OnError += (err) => Debug.LogError($"[WS] Error: {err}");
        ws.OnClose += OnClose;
        ws.OnMessage += OnMessage;

        Debug.Log($"[WS] Connecting to {url} (boothId={boothId})");
        await ws.Connect();
    }

    void OnClose(WebSocketCloseCode code)
    {
        // Editor Play 정지로 인스턴스가 destroyed 이후 비동기 콜백이 늦게 도착하는 경우 방어
        if (this == null || reconnectDisabled) return;

        Debug.LogWarning($"[WS] Closed: {code} ({(int)code})");

        if ((int)code == 4001)
        {
            reconnectDisabled = true;
            Debug.LogError("[WS] SUPERSEDED — another connection took over. Reconnect disabled.");
            return;
        }

        Debug.Log($"[WS] Reconnecting in {currentBackoff:F1}s");
        Invoke(nameof(Reconnect), currentBackoff);
        currentBackoff = Mathf.Min(currentBackoff * 2f, maxBackoffSeconds);
    }

    void OnDestroy()
    {
        // 인스턴스 파괴 후 OnClose 등 비동기 콜백이 Invoke/Reconnect 시도하는 걸 막음
        reconnectDisabled = true;
    }

    async void Reconnect()
    {
        await Connect();
    }

    void OnMessage(byte[] bytes)
    {
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        Debug.Log($"[WS] Recv: {json}");

        try
        {
            var msg = JsonUtility.FromJson<ServerMessage>(json);
            switch (msg.type)
            {
                case "START_SESSION":
                    CurrentSessionId = msg.sessionId;
                    CurrentStartToken = msg.startToken;
                    Debug.Log($"[WS] START_SESSION sessionId={msg.sessionId} startToken={msg.startToken}");
                    OnSessionStarted?.Invoke(msg.sessionId, msg.startToken);
                    break;
                case "ACK":
                    Debug.Log($"[WS] ACK sessionId={msg.sessionId}");
                    ClearAckWatchdog(msg.sessionId);
                    OnAck?.Invoke(msg.sessionId);
                    break;
                case "NACK":
                    Debug.LogWarning($"[WS] NACK sessionId={msg.sessionId} reason={msg.reason}");
                    ClearAckWatchdog(msg.sessionId);
                    OnNack?.Invoke(msg.sessionId, msg.reason);
                    break;
                case "RESULT_READY":
                    string videoUrl = msg.result != null && msg.result.contents != null
                        ? msg.result.contents.GENERATED_VIDEO : null;
                    Debug.Log($"[WS] RESULT_READY sessionId={msg.sessionId} qrPayload={msg.qrPayload} video={videoUrl}");
                    OnResultReady?.Invoke(msg.sessionId, msg.qrPayload, msg.result);
                    break;
                case "RESULT_FAILED":
                    Debug.LogError($"[WS] RESULT_FAILED sessionId={msg.sessionId} reason={msg.reason}");
                    OnResultFailed?.Invoke(msg.sessionId, msg.reason);
                    break;
                default:
                    Debug.LogWarning($"[WS] Unknown type: {msg.type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Parse error: {ex.Message}\nraw: {json}");
        }
    }

    public async Task SendSessionStarted()
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogError("[WS] Cannot send SESSION_STARTED: socket not open");
            return;
        }
        if (CurrentSessionId == 0 || string.IsNullOrEmpty(CurrentStartToken))
        {
            Debug.LogError("[WS] Cannot send SESSION_STARTED: no pending session");
            return;
        }

        var payload = new SessionStartedMessage
        {
            type = "SESSION_STARTED",
            sessionId = CurrentSessionId,
            startToken = CurrentStartToken,
            startedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
        };
        string json = JsonUtility.ToJson(payload);
        Debug.Log($"[WS] Send: {json}");
        await ws.SendText(json);

        ackPendingSessionId = CurrentSessionId;
        if (ackWatchdog != null) StopCoroutine(ackWatchdog);
        ackWatchdog = StartCoroutine(AckTimeoutWatchdog(CurrentSessionId));
    }

    // 진행 중 세션을 서버에 중단 통보. IN_PROGRESS 상태에서만 유효 (§3.4).
    // 성공 시 서버가 ABORTED로 전이 + ACK 응답. fire-and-forget.
    public async Task SendSessionAbort()
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WS] Cannot send SESSION_ABORT: socket not open");
            return;
        }
        if (CurrentSessionId == 0 || string.IsNullOrEmpty(CurrentStartToken))
        {
            Debug.Log("[WS] No active session to abort");
            return;
        }

        var payload = new SessionAbortMessage
        {
            type = "SESSION_ABORT",
            sessionId = CurrentSessionId,
            startToken = CurrentStartToken,
        };
        string json = JsonUtility.ToJson(payload);
        Debug.Log($"[WS] Send: {json}");
        await ws.SendText(json);
    }

    public void ClearCurrentSession()
    {
        CurrentSessionId = 0;
        CurrentStartToken = null;
    }

    IEnumerator AckTimeoutWatchdog(int sessionId)
    {
        yield return new WaitForSeconds(ackTimeoutSeconds);
        if (ackPendingSessionId == sessionId)
        {
            Debug.LogWarning($"[WS] ACK timeout for sessionId={sessionId}, resending SESSION_STARTED");
            ackWatchdog = null;
            _ = SendSessionStarted();
        }
    }

    void ClearAckWatchdog(int sessionId)
    {
        if (ackPendingSessionId != sessionId) return;
        ackPendingSessionId = 0;
        if (ackWatchdog != null)
        {
            StopCoroutine(ackWatchdog);
            ackWatchdog = null;
        }
    }

    async void OnApplicationQuit()
    {
        if (ws == null) return;

        // 진행 중 세션이 있으면 ABORT 시도 후 닫기.
        // Editor에서 Play를 즉시 정지하면 메시지가 서버에 도달 못 할 수 있음 (best effort).
        if (ws.State == WebSocketState.Open
            && CurrentSessionId != 0
            && !string.IsNullOrEmpty(CurrentStartToken))
        {
            try
            {
                var payload = new SessionAbortMessage
                {
                    type = "SESSION_ABORT",
                    sessionId = CurrentSessionId,
                    startToken = CurrentStartToken,
                };
                string json = JsonUtility.ToJson(payload);
                Debug.Log($"[WS] Send (on quit): {json}");
                await ws.SendText(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WS] SESSION_ABORT on quit failed: {e.Message}");
            }
        }

        await ws.Close();
    }

    [Serializable]
    class ServerMessage
    {
        public string type;
        public int sessionId;
        public string startToken;
        public string reason;
        public string qrPayload;
        public APIManager.ResultInner result;
    }

    [Serializable]
    class SessionStartedMessage
    {
        public string type;
        public int sessionId;
        public string startToken;
        public string startedAt;
    }

    [Serializable]
    class SessionAbortMessage
    {
        public string type;
        public int sessionId;
        public string startToken;
    }
}
