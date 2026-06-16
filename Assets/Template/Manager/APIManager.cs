using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// AI 보이스 시프트 결과 제출 API 매니저.
//   POST /api/v1/experience/sessions/{sessionId}/result  (multipart/form-data)
//     Header : X-Start-Token = {startToken}
//     Fields : visitorVideo (binary mp4), outputLanguage (ko|ja|zh|en|de|ru)
//     Resp   : 200(동기) data.result.contents.GENERATED_VIDEO
//              202(비동기) data.sessionId → WebSocket RESULT_READY 대기
//
// 서버 스펙 상 sourceLanguage 필드는 없음 (서버가 음성에서 자동 인식 — 기획상 입력은 한국어 고정).
public class APIManager : MonoBehaviour
{
    public static APIManager Instance { get; private set; }

    [Header("Server")]
    [SerializeField] string baseUrl = "https://dev-api.uiseong.ai.kr";

    [Header("Timeout")]
    [Tooltip("fal HeyGen Translate 처리에 수 분 걸릴 수 있음. 넉넉하게.")]
    [SerializeField] int timeoutSeconds = 600;

    public event Action<ResultData> OnResultSuccess;       // HTTP 200 동기 응답
    public event Action<int> OnResultAccepted;              // HTTP 202 비동기 접수 — WS RESULT_READY 대기
    public event Action<string, string> OnResultFailure;    // (code, message)

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

    /// <summary>
    /// visitorVideo(mp4) 파일 + outputLanguage(ISO 639-1 코드) 서버 제출.
    /// </summary>
    public void SubmitResult(int sessionId, string startToken, string videoFilePath, string outputLanguage)
    {
        StartCoroutine(SubmitCoroutine(sessionId, startToken, videoFilePath, outputLanguage));
    }

    IEnumerator SubmitCoroutine(int sessionId, string startToken, string videoFilePath, string outputLanguage)
    {
        if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath))
        {
            Debug.LogError($"[API] visitorVideo 파일 없음: {videoFilePath}");
            OnResultFailure?.Invoke("NO_VIDEO_FILE", videoFilePath ?? "");
            yield break;
        }

        byte[] videoBytes;
        try { videoBytes = File.ReadAllBytes(videoFilePath); }
        catch (Exception e)
        {
            Debug.LogError($"[API] 파일 읽기 실패: {e.Message}");
            OnResultFailure?.Invoke("FILE_READ_ERROR", e.Message);
            yield break;
        }

        string url = $"{baseUrl}/api/v1/experience/sessions/{sessionId}/result";

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("visitorVideo", videoBytes, Path.GetFileName(videoFilePath), "video/mp4"),
            new MultipartFormDataSection("outputLanguage", outputLanguage),
        };

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.SetRequestHeader("X-Start-Token", startToken);
            req.timeout = timeoutSeconds;

            Debug.Log($"[API] POST {url} outputLang={outputLanguage} bytes={videoBytes.Length}");

            yield return req.SendWebRequest();

            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            long status = req.responseCode;

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
                        Debug.Log($"[API] 200 SUCCESS qrPayload={res.data.qrPayload} video={videoUrl}");
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
        /// 서버가 생성한 번역 립싱크 영상 URL
        public string GENERATED_VIDEO;
    }
}
