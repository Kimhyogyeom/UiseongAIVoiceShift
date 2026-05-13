using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class APIManager : MonoBehaviour
{
    public static APIManager Instance { get; private set; }

    [Header("Server")]
    [SerializeField] private string baseUrl = "https://dev-api.uiseong.ai.kr";

    [Header("Timeout")]
    [Tooltip("AI 영상 생성이 수 분 걸릴 수 있음. 넉넉하게.")]
    [SerializeField] private int timeoutSeconds = 600;

    public event Action<ResultData> OnResultSuccess;          // HTTP 200 동기 응답 (즉시 결과)
    public event Action<int> OnResultAccepted;                 // HTTP 202 비동기 접수 (WebSocket RESULT_READY 대기)
    public event Action<string, string> OnResultFailure;       // (code, message) — 409/503/네트워크 등 확정 실패

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SubmitMovieResult(int sessionId, string startToken, string director, string genre, string prompt)
    {
        StartCoroutine(SubmitResultCoroutine(sessionId, startToken, director, genre, prompt));
    }

    IEnumerator SubmitResultCoroutine(int sessionId, string startToken, string director, string genre, string prompt)
    {
        string url = $"{baseUrl}/api/v1/experience/sessions/{sessionId}/result";

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("director", director),
            new MultipartFormDataSection("genre", genre),
            new MultipartFormDataSection("prompt", prompt),
        };

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.SetRequestHeader("X-Start-Token", startToken);
            req.timeout = timeoutSeconds;

            Debug.Log($"[API] POST {url} director={director} genre={genre} promptLen={prompt.Length}");

            yield return req.SendWebRequest();

            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            long status = req.responseCode;

            // UnityWebRequest.Result는 4xx/5xx도 ProtocolError로 분류함 → status code 기반 분기
            bool transportFailure = req.result == UnityWebRequest.Result.ConnectionError
                                 || req.result == UnityWebRequest.Result.DataProcessingError;
            if (transportFailure)
            {
                Debug.LogError($"[API] Transport error: {req.error}\n{body}");
                OnResultFailure?.Invoke("NETWORK", req.error);
                yield break;
            }

            Debug.Log($"[API] HTTP {status} response: {body}");

            ApiResponse res = null;
            if (!string.IsNullOrEmpty(body))
            {
                try { res = JsonUtility.FromJson<ApiResponse>(body); }
                catch (Exception ex)
                {
                    Debug.LogError($"[API] Parse error: {ex.Message}");
                    OnResultFailure?.Invoke("PARSE_ERROR", ex.Message);
                    yield break;
                }
            }

            switch (status)
            {
                case 200:
                    if (res != null && res.isSuccess && res.data != null)
                    {
                        string videoUrl = res.data.result != null && res.data.result.contents != null
                            ? res.data.result.contents.GENERATED_VIDEO : null;
                        int score = res.data.result != null ? res.data.result.score : 0;
                        Debug.Log($"[API] 200 SUCCESS qrPayload={res.data.qrPayload} score={score} video={videoUrl}");
                        OnResultSuccess?.Invoke(res.data);
                    }
                    else
                    {
                        string code = res != null ? res.code : "UNKNOWN";
                        string msg = res != null ? res.message : "";
                        Debug.LogError($"[API] 200 but body invalid code={code} message={msg}");
                        OnResultFailure?.Invoke(code ?? "UNKNOWN", msg ?? "");
                    }
                    break;

                case 202:
                    int accepted = res != null && res.data != null ? res.data.sessionId : sessionId;
                    Debug.Log($"[API] 202 ACCEPTED sessionId={accepted} — WebSocket RESULT_READY 대기");
                    OnResultAccepted?.Invoke(accepted);
                    break;

                case 409:
                    Debug.LogError($"[API] 409 CONFLICT — 이미 제출된 세션");
                    OnResultFailure?.Invoke("ALREADY_SUBMITTED", res != null ? res.message : "");
                    break;

                case 503:
                    Debug.LogError($"[API] 503 SERVICE_UNAVAILABLE — 서버 과부하");
                    OnResultFailure?.Invoke("SERVER_BUSY", res != null ? res.message : "");
                    break;

                default:
                    string fcode = res != null ? res.code : ("HTTP_" + status);
                    string fmsg = res != null ? res.message : req.error;
                    Debug.LogError($"[API] FAILURE status={status} code={fcode} message={fmsg}");
                    OnResultFailure?.Invoke(fcode ?? ("HTTP_" + status), fmsg ?? "");
                    break;
            }
        }
    }

    [Serializable]
    public class ApiResponse
    {
        public bool isSuccess;
        public string code;
        public string message;
        public string trackingId;
        public ResultData data;
    }

    [Serializable]
    public class ResultData
    {
        public int sessionId;       // 202 응답에서만 채워짐
        public string qrPayload;
        public ResultInner result;
    }

    [Serializable]
    public class ResultInner
    {
        public int score;
        public ResultContents contents;
        public string analysis;
    }

    [Serializable]
    public class ResultContents
    {
        public string GENERATED_VIDEO;
    }
}
