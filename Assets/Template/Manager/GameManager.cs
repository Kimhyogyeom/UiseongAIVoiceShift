using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Panels")]
    public GameObject titlePanel;      // 1번: 타이틀 화면 (QR 패널을 자식으로 포함)
    public GameObject qrPanel;         // 2번: QR 화면 (TitlePanel의 자식)
    public GameObject languagePanel;   // 3번: 언어 선택 화면
    public GameObject cameraPanel;     // 4번: 카메라 조정 화면
    public GameObject headsetPanel;    // 5번: 헤드셋 준비 화면 (5초 타이머, 구성은 다음 단계)
    public GameObject genrePanel;      // (구) 무비디렉터 잔재 — 사용 안 함, 점진 정리 예정

    [Header("Language Selection")]
    public LanguageButton[] sourceLanguageButtons;  // 위 row (지금 쓰는 언어) 6개
    public LanguageButton[] targetLanguageButtons;  // 아래 row (변환할 언어) 6개
    public Button languageNextButton;               // "다음으로" 버튼

    string selectedSourceLang;
    string selectedTargetLang;

    [Header("Camera Countdown")]
    public Button cameraOkButton;            // CameraPanel의 OK 버튼 (카운트다운 시작 후 비활성화)
    public TMP_Text cameraCountdownText;     // 카메라 영상 위에 표시되는 카운트다운 숫자
    [Tooltip("OK 버튼 클릭 후 카운트다운 시작 숫자 (초)")]
    public int cameraCountdownStart = 5;

    Coroutine cameraCountdownCoroutine;

    [Header("Voice Recording")]
    public Image voicePulseImage;            // (선택) 단일 동그라미 파동 — 안 쓰면 비워둠
    public VoiceWaveform voiceWaveform;      // (선택) 막대 N개 파형 시각화 — 사용자 이미지 스타일
    public TMP_Text recordingTimerText;      // 파동 아래 30초 타이머
    [Tooltip("녹음 시간 (초)")]
    public int recordingDuration = 30;
    [Tooltip("음량 → 시각화 강도 민감도 (단일 동그라미 + 파형 막대 공통)")]
    public float pulseSensitivity = 10f;
    public float pulseMinScale = 1f;
    public float pulseMaxScale = 1.5f;

    Coroutine recordingTimerCoroutine;
    AudioClip micClip;
    string micDeviceName;
    bool isRecording;

    [Header("Video Recorder (영상 + 마이크 통합 녹화 → mp4)")]
    public VideoRecorder videoRecorder;

    [Header("Translation Loading")]
    [Tooltip("번역 게이지바 진행 시간 (초). 실제 API 응답 도착하면 단축됨.")]
    public float translationDuration = 30f;

    Coroutine translationCoroutine;
    public GameObject confirmPanel;    // 4번: "이 주제로 영상을 만들어볼까?" 확인 팝업 (장르)
    public GameObject scenarioPanel;   // 5번: "시나리오를 적어주세요!" 화면
    public GameObject examplePanel;    // 6번: 예시 선택 팝업
    public GameObject scenarioConfirmPanel; // 7번: "이 내용으로 영상을 만들어볼까요?" 확인 팝업 (시나리오)
    public GameObject loadingPanel;    // 8번: AI 영상 생성 중 로딩 화면
    public GameObject resultPanel;     // 9번: 결과 영상 화면
    public GameObject resultQRPanel;   // 10번: 결과 QR 팝업

    [Header("Scenario Input")]
    public TMP_InputField scenarioInput;   // 시나리오 입력 필드
    public TMP_Text charCountText;         // 글자수 표시 (0/1000)
    public int maxCharCount = 1000;

    [Header("QR")]
    public RawImage qrImage;           // QR 패널 안의 RawImage (부스 QR)
    public RawImage resultQRImage;     // 결과 QR 패널의 RawImage

    [Header("Loading Bar")]
    public Image loadingBarFill;       // Image (Filled, Horizontal) — fillAmount 애니메이션
    [Tooltip("이 시간 동안 선형으로 0 → 0.99 채워짐 (초). AI 영상 생성 평균 소요시간 참고. 99%에서 정지하고 응답 오면 100%로 채워짐")]
    public float loadingBarTargetSeconds = 120f;

    [Header("Result Video")]
    public VideoPlayer videoPlayer;    // 결과 영상 재생용
    public RawImage videoDisplayImage; // 결과 패널 안의 영상 표시 RawImage

    [Header("Video Progress")]
    public Image videoProgressFill;    // Image (Filled, Horizontal) — 재생 진행률 표시
    public TMP_Text videoTimeCurrent;  // "0:00" 현재 재생 시간
    public TMP_Text videoTimeTotal;    // "0:20" 총 길이

    [Header("Result Title")]
    public TMP_Text resultTitleText;   // 결과 패널의 영화 제목 표시
    [Tooltip("직접 입력 시 제목 최대 글자 수 (넘으면 말줄임표)")]
    public int titleMaxChars = 20;

    RenderTexture videoRT;
    string currentResultTitle;

    [Header("Fade")]
    public CanvasGroup fadeOverlay;    // 화면 전체를 덮는 검정 CanvasGroup
    public float fadeDuration = 0.4f;

    [Header("State")]
    public string selectedGenre;       // 선택된 장르

    [Header("API")]
    [Tooltip("director 필드에 전송할 값. 백엔드에서 의미 확정되면 조정.")]
    public string directorValue = "AI";

    bool isTransitioning;
    string currentQrPayload;
    string currentVideoUrl;
    Coroutine loadingBarCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ResetAllPanelsImmediate();
        UpdateLanguageNextButton();

        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
        }

        if (scenarioInput != null)
        {
            scenarioInput.characterLimit = maxCharCount;
            scenarioInput.onValueChanged.AddListener(OnScenarioTextChanged);
        }
        UpdateCharCount();

        if (WebSocketClient.Instance != null)
        {
            WebSocketClient.Instance.OnSessionStarted += HandleSessionStarted;
            WebSocketClient.Instance.OnResultReady += HandleResultReady;
            WebSocketClient.Instance.OnResultFailed += HandleResultFailedWs;
        }

        if (APIManager.Instance != null)
        {
            APIManager.Instance.OnResultSuccess += HandleResultSuccess;
            APIManager.Instance.OnResultAccepted += HandleResultAccepted;
            APIManager.Instance.OnResultFailure += HandleResultFailure;
        }

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.errorReceived += OnVideoError;
        }

        if (videoRecorder != null)
        {
            videoRecorder.OnRecordingStopped += HandleRecorderStopped;
            videoRecorder.OnRecordingComplete += HandleRecorderComplete;
            videoRecorder.OnProgress += HandleRecorderProgress;
        }
    }

    void SetupAndPlayVideo(string url)
    {
        print(url);
        if (videoPlayer == null)
        {
            Debug.LogError("[GameManager] VideoPlayer 미연결");
            return;
        }
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("[GameManager] video URL 비어있음");
            return;
        }

        videoPlayer.Stop();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;

        if (videoRT == null)
        {
            videoRT = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            videoRT.Create();
        }
        videoPlayer.targetTexture = videoRT;

        if (videoDisplayImage != null)
            videoDisplayImage.texture = videoRT;
        else
            Debug.LogWarning("[GameManager] videoDisplayImage 미연결 — 영상이 화면에 안 보일 수 있음");

        Debug.Log($"[GameManager] VideoPlayer preparing: {url}");
        videoPlayer.Prepare();
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log($"[GameManager] VideoPlayer prepared ({vp.width}x{vp.height}, len={vp.length:F1}s). Paused at frame 0.");

        // 실제 영상 해상도에 맞춰 RenderTexture 재생성
        if (vp.width > 0 && vp.height > 0 &&
            (videoRT == null || videoRT.width != (int)vp.width || videoRT.height != (int)vp.height))
        {
            if (videoRT != null) videoRT.Release();
            videoRT = new RenderTexture((int)vp.width, (int)vp.height, 0, RenderTextureFormat.ARGB32);
            videoRT.Create();
            vp.targetTexture = videoRT;
            if (videoDisplayImage != null) videoDisplayImage.texture = videoRT;
            Debug.Log($"[GameManager] RenderTexture resized to {vp.width}x{vp.height}");
        }

        // 첫 프레임만 렌더링 후 일시정지 (사용자가 "시작" 버튼 눌러야 재생)
        vp.Play();
        StartCoroutine(PauseAfterFirstFrame());

        // 총 재생 시간 텍스트 초기화
        if (videoTimeTotal != null) videoTimeTotal.text = FormatTime(vp.length);
        if (videoTimeCurrent != null) videoTimeCurrent.text = FormatTime(0);
        if (videoProgressFill != null) videoProgressFill.fillAmount = 0f;
    }

    IEnumerator PauseAfterFirstFrame()
    {
        yield return null;
        if (videoPlayer != null)
        {
            videoPlayer.Pause();
            videoPlayer.time = 0;
        }
    }

    void Update()
    {
        UpdateVideoProgress();
        UpdatePulseScale();

#if UNITY_EDITOR
        if (qrPanel != null && qrPanel.activeSelf && !isTransitioning
            && Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame)
        {
            Debug.LogWarning("[GameManager] QR 임시 스킵 (개발용). 실제 세션 없음 → 이후 결과 제출 불가.");
            StartCoroutine(TransitionTo(() =>
            {
                titlePanel.SetActive(false);
                qrPanel.SetActive(false);
                genrePanel.SetActive(true);
            }));
        }
#endif
    }

    void UpdateVideoProgress()
    {
        if (videoPlayer == null || !videoPlayer.isPrepared) return;

        double total = videoPlayer.length;
        double current = videoPlayer.time;

        if (videoProgressFill != null && total > 0)
            videoProgressFill.fillAmount = (float)(current / total);

        if (videoTimeCurrent != null)
            videoTimeCurrent.text = FormatTime(current);
    }

    string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        int totalSec = Mathf.FloorToInt((float)seconds);
        int min = totalSec / 60;
        int sec = totalSec % 60;
        return $"{min}:{sec:D2}";
    }

    void OnVideoError(VideoPlayer vp, string error)
    {
        Debug.LogError($"[GameManager] VideoPlayer error: {error}  url={vp.url}");
    }

    void OnDestroy()
    {
        if (WebSocketClient.Instance != null)
        {
            WebSocketClient.Instance.OnSessionStarted -= HandleSessionStarted;
            WebSocketClient.Instance.OnResultReady -= HandleResultReady;
            WebSocketClient.Instance.OnResultFailed -= HandleResultFailedWs;
        }

        if (APIManager.Instance != null)
        {
            APIManager.Instance.OnResultSuccess -= HandleResultSuccess;
            APIManager.Instance.OnResultAccepted -= HandleResultAccepted;
            APIManager.Instance.OnResultFailure -= HandleResultFailure;
        }

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }

        if (videoRecorder != null)
        {
            videoRecorder.OnRecordingStopped -= HandleRecorderStopped;
            videoRecorder.OnRecordingComplete -= HandleRecorderComplete;
            videoRecorder.OnProgress -= HandleRecorderProgress;
        }
    }

    void ResetAllPanelsImmediate()
    {
        if (titlePanel != null) titlePanel.SetActive(true);
        if (qrPanel != null) qrPanel.SetActive(false);
        if (languagePanel != null) languagePanel.SetActive(false);
        if (cameraPanel != null) cameraPanel.SetActive(false);
        if (headsetPanel != null) headsetPanel.SetActive(false);
        if (genrePanel != null) genrePanel.SetActive(false);

        // 언어 선택 상태 리셋
        selectedSourceLang = null;
        selectedTargetLang = null;
        if (sourceLanguageButtons != null)
            foreach (var b in sourceLanguageButtons) if (b != null) b.SetSelected(false);
        if (targetLanguageButtons != null)
            foreach (var b in targetLanguageButtons) if (b != null) b.SetSelected(false);
        UpdateLanguageNextButton();

        // 카메라 카운트다운 상태 리셋
        if (cameraCountdownCoroutine != null)
        {
            StopCoroutine(cameraCountdownCoroutine);
            cameraCountdownCoroutine = null;
        }
        if (cameraCountdownText != null) cameraCountdownText.text = "";
        if (cameraOkButton != null)
        {
            cameraOkButton.gameObject.SetActive(true);
            cameraOkButton.interactable = true;
        }

        // 녹음 상태 리셋
        if (recordingTimerCoroutine != null)
        {
            StopCoroutine(recordingTimerCoroutine);
            recordingTimerCoroutine = null;
        }
        // VideoRecorder가 진행 중이면 즉시 중단 (마이크 + 프레임 캡처 정리)
        if (videoRecorder != null && videoRecorder.IsRecording)
            videoRecorder.CancelRecording();

        StopRecording();
        if (recordingTimerText != null) recordingTimerText.text = "";
        if (voicePulseImage != null) voicePulseImage.gameObject.SetActive(false);
        if (voiceWaveform != null) voiceWaveform.gameObject.SetActive(false);

        // 번역 진행 상태 리셋
        if (translationCoroutine != null)
        {
            StopCoroutine(translationCoroutine);
            translationCoroutine = null;
        }
        if (loadingBarFill != null) loadingBarFill.fillAmount = 0f;
        if (confirmPanel != null) confirmPanel.SetActive(false);
        if (scenarioPanel != null) scenarioPanel.SetActive(false);
        if (examplePanel != null) examplePanel.SetActive(false);
        if (scenarioConfirmPanel != null) scenarioConfirmPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (resultQRPanel != null) resultQRPanel.SetActive(false);
    }

    void SubmitToServer(string genre, string prompt)
    {
        if (WebSocketClient.Instance == null || APIManager.Instance == null)
        {
            Debug.LogError("[GameManager] WebSocketClient/APIManager Instance 없음");
            return;
        }

        int sessionId = WebSocketClient.Instance.CurrentSessionId;
        string startToken = WebSocketClient.Instance.CurrentStartToken;

        if (sessionId == 0 || string.IsNullOrEmpty(startToken))
        {
            Debug.LogError("[GameManager] sessionId/startToken 없음. 세션이 시작되지 않았을 수 있음.");
            return;
        }

        string genreCode = MapGenreToEnum(genre);
        string finalPrompt = string.IsNullOrWhiteSpace(prompt) ? "자유롭게 만들어주세요" : prompt;

        // 결과 화면에 보여줄 제목 미리 결정
        currentResultTitle = BuildResultTitle(genre, prompt);

        Debug.Log($"[GameManager] (무비디렉터 잔재) SubmitToServer 호출됨 — 보이스 시프트는 SubmitToVoiceShift 사용");
        // 무비디렉터 잔재 — 보이스 시프트는 VideoRecorder.OnRecordingComplete → SubmitToVoiceShift 흐름
    }

    // 결과 패널에 표시할 영화 제목 구성
    string BuildResultTitle(string genre, string prompt)
    {
        // 직접 입력: 사용자가 쓴 시나리오 앞부분을 제목으로
        if (genre == "직접입력")
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "내 이야기";
            string trimmed = prompt.Trim();
            if (trimmed.Length <= titleMaxChars) return trimmed;
            return trimmed.Substring(0, titleMaxChars) + "…";
        }
        // 장르 버튼 선택: 장르명 그대로
        return string.IsNullOrEmpty(genre) ? "AI 영화" : genre;
    }

    // === 언어 선택 ===

    // LanguageButton에서 호출. 같은 row 안에서 단일 선택으로 전환.
    public void OnLanguageButtonClicked(LanguageButton btn)
    {
        if (isTransitioning || btn == null) return;

        if (btn.slot == LanguageButton.LangSlot.Source)
        {
            selectedSourceLang = btn.langCode;
            if (sourceLanguageButtons != null)
                foreach (var b in sourceLanguageButtons) if (b != null) b.SetSelected(b == btn);
        }
        else
        {
            selectedTargetLang = btn.langCode;
            if (targetLanguageButtons != null)
                foreach (var b in targetLanguageButtons) if (b != null) b.SetSelected(b == btn);
        }

        Debug.Log($"[GameManager] 언어 선택 source={selectedSourceLang} target={selectedTargetLang}");
        UpdateLanguageNextButton();
    }

    void UpdateLanguageNextButton()
    {
        if (languageNextButton == null) return;
        languageNextButton.interactable = !string.IsNullOrEmpty(selectedSourceLang)
                                       && !string.IsNullOrEmpty(selectedTargetLang);
    }

    // LanguagePanel의 "다음으로" 버튼 OnClick
    public void OnLanguageNext()
    {
        if (isTransitioning) return;
        if (string.IsNullOrEmpty(selectedSourceLang) || string.IsNullOrEmpty(selectedTargetLang)) return;

        Debug.Log($"[GameManager] 언어 확정 → 카메라 조정 화면 source={selectedSourceLang} target={selectedTargetLang}");

        StartCoroutine(TransitionTo(() =>
        {
            if (languagePanel != null) languagePanel.SetActive(false);
            if (cameraPanel != null) cameraPanel.SetActive(true);
        }));
    }

    // CameraPanel의 "OK" 버튼 OnClick → 카운트다운 시작 (카메라 영상은 그대로 유지)
    public void OnCameraOkClick()
    {
        if (isTransitioning) return;
        if (cameraCountdownCoroutine != null) return;  // 중복 방지

        Debug.Log("[GameManager] 카메라 OK → 카운트다운 시작");

        if (cameraOkButton != null) cameraOkButton.interactable = false;
        cameraCountdownCoroutine = StartCoroutine(CameraCountdown());
    }

    IEnumerator CameraCountdown()
    {
        for (int i = cameraCountdownStart; i >= 1; i--)
        {
            if (cameraCountdownText != null) cameraCountdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        if (cameraCountdownText != null) cameraCountdownText.text = "";
        cameraCountdownCoroutine = null;

        Debug.Log("[GameManager] 5초 카운트다운 종료 → 녹음 시작");
        StartRecording();
    }

    // === 녹음 (말하는 화면) ===

    void StartRecording()
    {
        // OK 버튼 숨김
        if (cameraOkButton != null) cameraOkButton.gameObject.SetActive(false);

        // 파동/파형 활성화
        if (voicePulseImage != null)
        {
            voicePulseImage.gameObject.SetActive(true);
            voicePulseImage.rectTransform.localScale = Vector3.one * pulseMinScale;
        }
        if (voiceWaveform != null) voiceWaveform.gameObject.SetActive(true);

        // 영상 + 마이크 통합 녹화를 VideoRecorder에 위임
        // VideoRecorder가 30초 타이머, 마이크, 프레임 캡처, ffmpeg mp4 합성 전체 처리
        if (videoRecorder != null)
        {
            videoRecorder.StartRecording();
        }
        else
        {
            Debug.LogError("[GameManager] VideoRecorder 미연결 — Inspector에서 연결 필요");
        }
    }

    IEnumerator RecordingTimer()
    {
        for (int i = recordingDuration; i >= 1; i--)
        {
            if (recordingTimerText != null) recordingTimerText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        if (recordingTimerText != null) recordingTimerText.text = "";
        recordingTimerCoroutine = null;

        StopRecording();

        Debug.Log("[GameManager] 30초 녹음 종료 → 번역 시작");
        StartTranslation();
    }

    // === 번역 진행 (LoadingPanel) ===

    void StartTranslation()
    {
        // CameraPanel은 그대로 두고 (카메라 영상 유지), 자식 LoadingPanel만 오버레이로 활성화
        StartCoroutine(TransitionTo(() =>
        {
            if (loadingPanel != null) loadingPanel.SetActive(true);
        }));

        // 게이지바 진행 (시간 기반) — 실제 API 호출은 다음 단계에서 추가
        if (translationCoroutine != null) StopCoroutine(translationCoroutine);
        translationCoroutine = StartCoroutine(TranslationProgress());

        // TODO: 영상 녹화 mp4 파일 + outputLanguage(selectedTargetLang) → APIManager_VoiceShift.SubmitResult 호출
        // TODO: WebSocket RESULT_READY 수신 시 게이지바 100% + ResultPanel 전환
    }

    IEnumerator TranslationProgress()
    {
        if (loadingBarFill != null) loadingBarFill.fillAmount = 0f;

        float duration = Mathf.Max(0.01f, translationDuration);
        float t = 0f;
        // 95%까지만 시간 기반 진행. 100%는 API 응답 도착 시 CompleteTranslation에서 처리.
        const float maxFillBeforeDone = 0.95f;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (loadingBarFill != null)
                loadingBarFill.fillAmount = Mathf.Min(maxFillBeforeDone, t / duration);
            yield return null;
        }

        // 시간 만료해도 95%에서 대기. 응답 안 오면 계속 95% 유지.
        if (loadingBarFill != null) loadingBarFill.fillAmount = maxFillBeforeDone;
        translationCoroutine = null;
    }

    // API 응답(200 동기 또는 WS RESULT_READY) 도착 시 호출. 게이지바 100% + 로그.
    void CompleteTranslation()
    {
        if (translationCoroutine != null)
        {
            StopCoroutine(translationCoroutine);
            translationCoroutine = null;
        }
        if (loadingBarFill != null) loadingBarFill.fillAmount = 1f;

        Debug.Log("[번역 완료]");
        // TODO: 다음 단계 — ResultPanel 전환 + GENERATED_VIDEO 재생
    }

    // === VideoRecorder 이벤트 핸들러 ===

    // 30초 진행률 (0~1). 30초 카운트다운 텍스트 갱신.
    void HandleRecorderProgress(float progress01)
    {
        if (recordingTimerText != null)
        {
            float remaining = recordingDuration * (1f - progress01);
            recordingTimerText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
        }
    }

    // 30초 녹화 타이머 종료 즉시 호출 (ffmpeg 합성은 백그라운드). LoadingPanel 즉시 활성.
    void HandleRecorderStopped()
    {
        Debug.Log("[GameManager] 30초 녹화 종료 → LoadingPanel 활성 (ffmpeg 합성 진행 중)");
        if (recordingTimerText != null) recordingTimerText.text = "";
        StartTranslation();
    }

    // ffmpeg 합성까지 끝난 뒤 호출. 성공 시 mp4Path, 실패 시 errorMsg.
    void HandleRecorderComplete(string mp4Path, string errorMsg)
    {
        if (string.IsNullOrEmpty(mp4Path))
        {
            Debug.LogError($"[GameManager] 녹화/합성 실패 reason={errorMsg}");
            // TODO: 에러 UI 안내. 일단 메인 복귀
            ResetToTitle();
            return;
        }

        Debug.Log($"[GameManager] mp4 합성 완료 → API 제출 시작 mp4={mp4Path}");
        SubmitToVoiceShift(mp4Path);
    }

    // 보이스 시프트 결과 제출 API 호출.
    void SubmitToVoiceShift(string mp4Path)
    {
        if (WebSocketClient.Instance == null || APIManager.Instance == null)
        {
            Debug.LogError("[GameManager] WebSocketClient/APIManager Instance 없음");
            return;
        }

        int sessionId = WebSocketClient.Instance.CurrentSessionId;
        string startToken = WebSocketClient.Instance.CurrentStartToken;

        if (sessionId == 0 || string.IsNullOrEmpty(startToken))
        {
            Debug.LogError("[GameManager] sessionId/startToken 없음 — 세션이 시작되지 않은 상태");
            return;
        }

        if (string.IsNullOrEmpty(selectedTargetLang))
        {
            Debug.LogError("[GameManager] selectedTargetLang 없음 — 언어 선택 안 됨");
            return;
        }

        Debug.Log($"[GameManager] 결과 제출 요청 sessionId={sessionId} outputLang={selectedTargetLang}");
        APIManager.Instance.SubmitResult(sessionId, startToken, mp4Path, selectedTargetLang);
    }

    void StopRecording()
    {
        if (videoRecorder != null && videoRecorder.IsRecording)
            videoRecorder.CancelRecording();

        if (voicePulseImage != null)
            voicePulseImage.rectTransform.localScale = Vector3.one * pulseMinScale;
    }

    void UpdatePulseScale()
    {
        // VideoRecorder가 보유한 마이크 클립을 사용해 음량 시각화
        if (videoRecorder == null || !videoRecorder.IsMicActive || videoRecorder.MicClip == null) return;

        int pos = Microphone.GetPosition(videoRecorder.MicDevice) - 128;
        if (pos < 0) return;

        float[] samples = new float[128];
        videoRecorder.MicClip.GetData(samples, pos);

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];

        float rms = Mathf.Sqrt(sum / samples.Length);
        float level = Mathf.Clamp01(rms * pulseSensitivity);

        // [DEBUG] 음량 측정 확인 — 동작 검증 후 제거
        if (Time.frameCount % 30 == 0)
            Debug.Log($"[Voice] rms={rms:F4} level={level:F2} sensitivity={pulseSensitivity}");

        // 단일 동그라미 파동 — scale 조절
        if (voicePulseImage != null)
        {
            float scale = Mathf.Lerp(pulseMinScale, pulseMaxScale, level);
            voicePulseImage.rectTransform.localScale = Vector3.one * scale;
        }

        // 막대 파형 — 각 막대 높이 조절
        if (voiceWaveform != null)
            voiceWaveform.SetLevel(level);
    }

    // 한글 장르명 → 백엔드 enum 매핑
    // 허용값: action, comedy, drama, horror, sf, romance, thriller, fantasy, animation, documentary
    string MapGenreToEnum(string korean)
    {
        if (string.IsNullOrEmpty(korean)) return "drama";
        switch (korean)
        {
            case "SF 공상과학": return "sf";
            case "액션 스릴러": return "thriller";
            case "로맨틱 코미디": return "romance";
            case "호러 미스터리": return "horror";
            case "다큐멘터리": return "documentary";
            case "뮤지컬": return "drama";       // 직접 매핑 없음
            case "직접입력": return "drama";       // 프롬프트 기반, 기본 drama
            default: return korean.ToLower();
        }
    }

    void HandleResultSuccess(APIManager.ResultData data)
    {
        string videoUrl = data.result != null && data.result.contents != null
            ? data.result.contents.GENERATED_VIDEO : null;
        Debug.Log($"[GameManager] 결과 수신 성공 (200) qrPayload={data.qrPayload} video={videoUrl}");

        currentQrPayload = data.qrPayload;
        currentVideoUrl = videoUrl;

        // LoadingPanel 활성 상태일 때만 완료 처리
        if (loadingPanel == null || !loadingPanel.activeSelf)
        {
            Debug.Log("[GameManager] 결과 수신했지만 LoadingPanel 비활성 — 결과 전환 스킵");
            return;
        }

        CompleteTranslation();
        // TODO: ResultPanel 전환 + 영상 재생 (다음 단계)
    }

    [Header("Loading → Result Transition")]
    [Tooltip("로딩바가 현재값에서 100%까지 부드럽게 채워지는 시간 (초)")]
    public float loadingBarCompleteFillSeconds = 0.5f;
    [Tooltip("100% 도달 후 결과 패널로 전환하기 전에 머무는 시간 (초)")]
    public float loadingBarHoldAt100Seconds = 1.2f;

    IEnumerator CompleteAndTransitionToResult()
    {
        // Phase 1: 영상을 로컬로 다운로드 (Unity Windows VideoPlayer의 HTTPS 이슈 회피)
        //          다운로드 진행 중에도 로딩바 UX는 유지
        string localPath = null;
        yield return StartCoroutine(DownloadVideo(currentVideoUrl, p => localPath = p));

        // Phase 2: 현재 fillAmount에서 1.0까지 부드럽게 (다운로드 끝났음을 시각화)
        if (loadingBarFill != null)
        {
            float start = loadingBarFill.fillAmount;
            float duration = Mathf.Max(0.01f, loadingBarCompleteFillSeconds);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                loadingBarFill.fillAmount = Mathf.Lerp(start, 1f, t / duration);
                yield return null;
            }
            loadingBarFill.fillAmount = 1f;
        }

        // Phase 3: 100% 상태로 잠깐 유지 (사용자가 "완료됐다"고 인지)
        yield return new WaitForSeconds(loadingBarHoldAt100Seconds);

        // Phase 4: 결과 패널로 페이드 전환 + 영상 준비 + 제목 표시 + 디스플레이 PC로 푸시
        yield return StartCoroutine(TransitionTo(() =>
        {
            if (loadingPanel != null) loadingPanel.SetActive(false);
            if (resultPanel != null) resultPanel.SetActive(true);

            if (resultTitleText != null)
                resultTitleText.text = currentResultTitle;

            // 로컬 다운로드 성공 시 로컬 경로 우선, 실패 시 원본 URL 시도
            string playSource = !string.IsNullOrEmpty(localPath) ? localPath : currentVideoUrl;
            SetupAndPlayVideo(playSource);

            // "나만의 영화가 개봉됐어요!" 순간에 디스플레이 PC로 원본 URL 푸시
            if (!string.IsNullOrEmpty(currentVideoUrl) && DisplayPushSender.Instance != null)
            {
                DisplayPushSender.Instance.Push(currentVideoUrl);
            }
            else if (DisplayPushSender.Instance == null)
            {
                Debug.LogWarning("[GameManager] DisplayPushSender가 씬에 없음 — 디스플레이 PC로 전송 불가");
            }
        }));
    }

    IEnumerator DownloadVideo(string url, System.Action<string> onComplete)
    {
        if (string.IsNullOrEmpty(url))
        {
            onComplete?.Invoke(null);
            yield break;
        }

        string fileName = $"result_video_{System.DateTime.Now:yyyyMMddHHmmss}.mp4";
        string localPath = Path.Combine(Application.temporaryCachePath, fileName);

        using (var req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerFile(localPath);
            req.timeout = 120;
            Debug.Log($"[GameManager] 영상 다운로드 시작: {url} → {localPath}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[GameManager] 영상 다운로드 완료: {localPath} ({new FileInfo(localPath).Length / 1024}KB)");
                onComplete?.Invoke(localPath);
            }
            else
            {
                Debug.LogError($"[GameManager] 영상 다운로드 실패: {req.error} → 원본 URL 재시도");
                onComplete?.Invoke(null);
            }
        }
    }

    void HandleResultFailure(string code, string message)
    {
        StopLoadingBarAnimation();
        Debug.LogError($"[GameManager] 결과 수신 실패 code={code} message={message}");
        // TODO: 에러 화면 구현 후 재시도 옵션 제공
        // 현재는 타이틀 복귀만
        ResetToTitle();
    }

    // HTTP 202 수신 — 백엔드 비동기 처리 중. LoadingPanel 그대로 유지, RESULT_READY 대기.
    void HandleResultAccepted(int sessionId)
    {
        Debug.Log($"[GameManager] 결과 비동기 접수됨 sessionId={sessionId} — RESULT_READY 대기");
    }

    // WebSocket RESULT_READY 수신 — 비동기 결과 도착. 200 처리와 동일 흐름.
    void HandleResultReady(int sessionId, string qrPayload, APIManager.ResultInner result)
    {
        var data = new APIManager.ResultData
        {
            sessionId = sessionId,
            qrPayload = qrPayload,
            result = result,
        };
        HandleResultSuccess(data);
    }

    // WebSocket RESULT_FAILED 수신 — 확정 실패.
    void HandleResultFailedWs(int sessionId, string reason)
    {
        HandleResultFailure("RESULT_FAILED", reason);
    }

    void HandleSessionStarted(int sessionId, string startToken)
    {
        Debug.Log($"[GameManager] Session begin received (sessionId={sessionId})");

        if (isTransitioning) return;
        if (qrPanel == null || !qrPanel.activeSelf) return;

        StartCoroutine(TransitionTo(() =>
        {
            // QR 패널은 TitlePanel 자식이라 부모와 함께 꺼지지만,
            // 다음 메인 복귀 시 자동으로 다시 보이는 걸 막기 위해 명시적으로 끔
            if (qrPanel != null) qrPanel.SetActive(false);
            if (titlePanel != null) titlePanel.SetActive(false);
            if (languagePanel != null) languagePanel.SetActive(true);
            _ = WebSocketClient.Instance.SendSessionStarted();
        }));
    }

    void OnScenarioTextChanged(string text)
    {
        UpdateCharCount();
    }

    void UpdateCharCount()
    {
        if (charCountText != null)
        {
            int count = scenarioInput != null ? scenarioInput.text.Length : 0;
            charCountText.text = $"{count}/{maxCharCount}";
        }
    }

    // === 로딩바 애니메이션 ===

    void StartLoadingBarAnimation()
    {
        StopLoadingBarAnimation();
        loadingBarCoroutine = StartCoroutine(LoadingBarLoop());
    }

    void StopLoadingBarAnimation()
    {
        if (loadingBarCoroutine != null)
        {
            StopCoroutine(loadingBarCoroutine);
            loadingBarCoroutine = null;
        }
    }

    IEnumerator LoadingBarLoop()
    {
        if (loadingBarFill == null) yield break;
        loadingBarFill.fillAmount = 0f;

        // 0 → 0.99 선형 채우기 (loadingBarTargetSeconds 동안)
        // 99% 도달 후 멈춤. 100%는 서버 응답 시 CompleteAndTransitionToResult가 채움.
        float t = 0f;
        while (t < loadingBarTargetSeconds)
        {
            t += Time.deltaTime;
            loadingBarFill.fillAmount = Mathf.Min(0.99f, t / loadingBarTargetSeconds * 0.99f);
            yield return null;
        }

        loadingBarFill.fillAmount = 0.99f;
    }

    void CompleteLoadingBar()
    {
        if (loadingBarFill != null) loadingBarFill.fillAmount = 1f;
    }

    // === 타이틀 전환 버튼 ===

    // 1번 타이틀 패널의 버튼 OnClick
    public void OnTitleClick()
    {
        if (isTransitioning) return;

        // QR 진입 시점에 진행 중 세션이 메모리에 남아있으면 미리 abort.
        // ResetToTitle 경로를 거치지 않고 메인으로 돌아온 경우를 위한 안전망.
        if (WebSocketClient.Instance != null && WebSocketClient.Instance.CurrentSessionId != 0)
        {
            Debug.Log($"[GameManager] QR 진입 — 이전 세션 사전 abort sessionId={WebSocketClient.Instance.CurrentSessionId}");
            _ = WebSocketClient.Instance.SendSessionAbort();
            WebSocketClient.Instance.ClearCurrentSession();
        }

        StartCoroutine(TransitionTo(() =>
        {
            qrPanel.SetActive(true);

            if (qrImage != null && QRGenerator.Instance != null)
            {
                QRGenerator.Instance.ShowQR("experience-start:17", qrImage);
            }
        }));
    }

    // 장르 선택 버튼 OnClick (GenreButton에서 호출)
    public void OnGenreSelected(string genre)
    {
        if (isTransitioning) return;

        selectedGenre = genre;
        Debug.Log($"[GameManager] 선택된 장르: {genre}");

        StartCoroutine(TransitionTo(() =>
        {
            if (confirmPanel != null) confirmPanel.SetActive(true);
        }));
    }

    // "직접 입력하기" 버튼 OnClick → 시나리오 입력 패널로 전환
    public void OnCustomInputClick()
    {
        if (isTransitioning) return;

        selectedGenre = "직접입력";
        Debug.Log("[GameManager] 직접 입력 선택");

        StartCoroutine(TransitionTo(() =>
        {
            genrePanel.SetActive(false);
            scenarioPanel.SetActive(true);
            if (scenarioInput != null) scenarioInput.text = "";
        }));
    }

    // --- 시나리오 패널 ---

    // 시나리오 패널 - "예시 가져오기" 버튼 OnClick
    public void OnExampleClick()
    {
        if (isTransitioning) return;

        StartCoroutine(TransitionTo(() =>
        {
            if (examplePanel != null) examplePanel.SetActive(true);
        }));
    }

    // 예시 버튼 선택 (예시 패널 내 버튼 4개에 각각 연결)
    public void OnExampleSelected(string exampleText)
    {
        if (isTransitioning) return;

        Debug.Log($"[GameManager] 예시 선택: {exampleText}");

        StartCoroutine(TransitionTo(() =>
        {
            if (scenarioInput != null)
                scenarioInput.text = exampleText;

            if (examplePanel != null)
                examplePanel.SetActive(false);
        }));
    }

    // 시나리오 패널 - "다음으로" 버튼 OnClick
    public void OnScenarioNext()
    {
        if (isTransitioning) return;

        if (scenarioInput == null || string.IsNullOrEmpty(scenarioInput.text))
        {
            Debug.LogWarning("[GameManager] 시나리오를 입력해주세요.");
            return;
        }

        Debug.Log($"[GameManager] 입력된 시나리오: {scenarioInput.text}");

        StartCoroutine(TransitionTo(() =>
        {
            if (scenarioConfirmPanel != null)
                scenarioConfirmPanel.SetActive(true);
        }));
    }

    // 시나리오 확인 팝업 - "다시 적어볼래요" 버튼 OnClick
    public void OnScenarioConfirmCancel()
    {
        if (isTransitioning) return;

        StartCoroutine(TransitionTo(() =>
        {
            if (scenarioConfirmPanel != null)
                scenarioConfirmPanel.SetActive(false);
        }));
    }

    // 시나리오 확인 팝업 - "네!" 버튼 OnClick
    public void OnScenarioConfirmYes()
    {
        if (isTransitioning) return;

        StartCoroutine(TransitionTo(() =>
        {
            if (scenarioConfirmPanel != null) scenarioConfirmPanel.SetActive(false);
            if (scenarioPanel != null) scenarioPanel.SetActive(false);

            ShowLoadingPanel();

            string prompt = scenarioInput != null ? scenarioInput.text : "";
            SubmitToServer("직접입력", prompt);
        }));
    }

    // 확인 팝업 - "다시선택할래요" 버튼 OnClick
    public void OnConfirmCancel()
    {
        if (isTransitioning) return;

        StartCoroutine(TransitionTo(() =>
        {
            if (confirmPanel != null)
                confirmPanel.SetActive(false);
        }));
    }

    // 확인 팝업 - "네!" 버튼 OnClick
    public void OnConfirmYes()
    {
        if (isTransitioning) return;

        StartCoroutine(TransitionTo(() =>
        {
            if (confirmPanel != null) confirmPanel.SetActive(false);
            genrePanel.SetActive(false);

            ShowLoadingPanel();

            SubmitToServer(selectedGenre, "");
        }));
    }

    // === 로딩 패널 ===

    void ShowLoadingPanel()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
            StartLoadingBarAnimation();
        }
    }

    // === 결과 패널 - 영상 컨트롤 ===

    // 결과 패널 - 영상 재생 버튼 OnClick
    public void OnVideoPlayClick()
    {
        if (videoPlayer != null) videoPlayer.Play();
    }

    // 결과 패널 - 영상 일시정지 버튼 OnClick
    public void OnVideoPauseClick()
    {
        if (videoPlayer != null) videoPlayer.Pause();
    }

    // 결과 패널 - "처음으로 돌아가기" 버튼 OnClick
    public void OnBackToTitleClick()
    {
        if (isTransitioning) return;
        ResetToTitle();
    }

    // 결과 패널 - "QR 결과 저장" 버튼 OnClick → 결과 QR 패널 팝업
    public void OnSaveResultQRClick()
    {
        if (isTransitioning) return;
        if (resultQRPanel == null) return;

        StartCoroutine(TransitionTo(() =>
        {
            resultQRPanel.SetActive(true);
            if (resultQRImage != null && QRGenerator.Instance != null && !string.IsNullOrEmpty(currentQrPayload))
            {
                QRGenerator.Instance.ShowQR(currentQrPayload, resultQRImage);
            }
        }));
    }

    // 결과 QR 팝업 - "처음으로" 버튼 OnClick
    public void OnResultQRBackClick()
    {
        if (isTransitioning) return;
        ResetToTitle();
    }

    // === 홈/초기화 ===

    // 홈 버튼 OnClick (모든 패널 공용)
    public void OnHomeClick()
    {
        if (isTransitioning) return;
        ResetToTitle();
    }

    void ResetToTitle()
    {
        // 진행 중인 세션이 서버에 남아 있으면 ABORT 송신 — 다음 QR 스캔 시 "이미 진행 중" 충돌 방지
        if (WebSocketClient.Instance != null && WebSocketClient.Instance.CurrentSessionId != 0)
        {
            Debug.Log($"[GameManager] 홈 복귀 — 세션 중단 요청 sessionId={WebSocketClient.Instance.CurrentSessionId}");
            _ = WebSocketClient.Instance.SendSessionAbort();
            WebSocketClient.Instance.ClearCurrentSession();
        }

        if (videoPlayer != null) videoPlayer.Stop();
        StopLoadingBarAnimation();

        StartCoroutine(TransitionTo(() =>
        {
            ResetAllPanelsImmediate();
        }));
    }

    // === 페이드 전환 ===

    IEnumerator TransitionTo(System.Action switchPanels)
    {
        if (fadeOverlay == null)
        {
            switchPanels();
            yield break;
        }

        isTransitioning = true;
        fadeOverlay.blocksRaycasts = true;

        // 페이드아웃 (투명 → 검정)
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = t / fadeDuration;
            yield return null;
        }
        fadeOverlay.alpha = 1f;

        // 패널 전환
        switchPanels();

        // 한 프레임 대기 (UI 갱신)
        yield return null;

        // 페이드인 (검정 → 투명)
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = 1f - (t / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = 0f;

        fadeOverlay.blocksRaycasts = false;
        isTransitioning = false;
    }
}
