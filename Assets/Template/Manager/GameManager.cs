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
    public GameObject loadingPanel;    // 5번: 번역중 대기 화면 (CameraPanel의 자식, 오버레이)
    public GameObject resultPanel;     // 6번: 번역 완료 화면 (영상 재생)
    public GameObject resultQRPanel;   // 7번: 결과 QR 팝업
    public GameObject headsetPanel;    // (구) 임시 — 사용 안 함
    public GameObject genrePanel;      // (구) 무비디렉터 잔재 — 사용 안 함

    [Header("Language Selection")]
    public LanguageButton[] targetLanguageButtons;  // 변환할 언어 6개 (ko / ja / zh / en / de / ru)
    public Button languageNextButton;               // "다음으로" 버튼

    // 입력 언어는 한국어 고정 — 기획상 별도 선택 UI 없음
    const string SOURCE_LANG = "ko";
    string selectedTargetLang;

    [Header("Camera Countdown")]
    public Button cameraOkButton;            // CameraPanel의 OK 버튼 (카운트다운 시작 후 비활성화)
    public TMP_Text cameraCountdownText;     // 카메라 영상 위에 표시되는 카운트다운 숫자
    [Tooltip("OK 버튼 클릭 후 카운트다운 시작 숫자 (초)")]
    public int cameraCountdownStart = 5;

    [Tooltip("높이 조절 안내 그룹 (텍스트/이미지). 다음으로 클릭 전까지 활성.")]
    public GameObject cameraPhaseAGroup;
    [Tooltip("녹음 시작 안내 그룹 (텍스트/이미지). 다음으로 클릭 후 5초 카운트다운 동안 활성.")]
    public GameObject cameraPhaseBGroup;
    [Tooltip("녹음 중 그룹 (\"자유롭게 이야기해 보세요\" 텍스트 + 게이지바). 30초 녹음 동안 활성.")]
    public GameObject cameraPhaseCGroup;
    [Tooltip("녹음 진행 게이지바. Image (Filled, Horizontal). 0 → 1 채워짐.")]
    public Image recordingProgressFill;
    [Tooltip("녹음 남은 시간 텍스트 (\"00:30\" → \"00:00\" 형식).")]
    public TMP_Text recordingRemainingTimeText;

    [Tooltip("말하기 예시 안내 — Phase B/C 둘 다에서 표시. 사용자 X로 한 번 닫으면 Phase C에서도 다시 안 뜸. 리셋 시 원복.\n주의: 이 오브젝트는 PhaseB/C의 자식이 아니라 CameraPanel 직속 자식으로 둬야 함 (그래야 phase 전환 시에도 표시 유지).")]
    public GameObject speakingExamplesPanel;

    // 사용자가 X 버튼으로 닫았는지 — Phase B에서 닫으면 Phase C에서도 안 뜸. Reset 시 false로 원복.
    bool speakingExamplesClosed;

    [Header("Camera Countdown Sprite Animation")]
    [Tooltip("5초 카운트다운 동안 표시할 스프라이트 Image. 4프레임 애니메이션으로 순환 재생.")]
    public Image cameraCountdownSpriteImage;
    [Tooltip("순서대로 재생할 스프라이트 (4개 권장). 1초에 한 번 전체 순환.")]
    public Sprite[] cameraCountdownSprites;
    [Tooltip("스프라이트 한 프레임 지속 시간 (초). 4프레임 × 0.25 = 1초 권장.")]
    public float cameraCountdownFrameDuration = 0.25f;

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
    // 무비디렉터 잔재 — 사용 안 함, 점진 정리 예정
    public GameObject confirmPanel;
    public GameObject scenarioPanel;
    public GameObject examplePanel;
    public GameObject scenarioConfirmPanel;

    [Header("Scenario Input")]
    public TMP_InputField scenarioInput;   // 시나리오 입력 필드
    public TMP_Text charCountText;         // 글자수 표시 (0/1000)
    public int maxCharCount = 1000;

    [Header("QR")]
    public RawImage qrImage;           // QR 패널 안의 RawImage (부스 QR)
    public RawImage resultQRImage;     // 결과 QR 패널의 RawImage

    [Header("Loading Bar")]
    public Image loadingBarFill;       // (선택) Image (Filled, Horizontal) — 미연결 시 비표시
    [Tooltip("이 시간 동안 선형으로 0 → 0.99 채워짐 (초). 게이지바 미사용이면 무의미.")]
    public float loadingBarTargetSeconds = 120f;

    [Header("Loading Panel — Rotating Indicator")]
    [Tooltip("로딩 패널의 회전 이미지 (Z축 천천히 회전). 미연결이면 회전 안 함.")]
    public RectTransform loadingRotatingImage;
    [Tooltip("회전 속도 (deg/sec). 음수면 반대 방향. 기본 30 = 12초에 한 바퀴.")]
    public float loadingRotationSpeed = 30f;

    [Header("Loading Panel — Target Language Display")]
    [Tooltip("선택한 변환 언어의 이미지 표시 위치 (Image).")]
    public Image loadingTargetLanguageImage;
    [Tooltip("선택한 변환 언어명 텍스트 (\"영어\", \"일본어\" 등).")]
    public TMP_Text loadingTargetLanguageText;
    [Tooltip("언어 코드별 표시 매핑. langCode가 selectedTargetLang과 같은 항목의 sprite/displayName이 표시됨.")]
    public TargetLanguageDisplay[] targetLanguageDisplays;

    [System.Serializable]
    public class TargetLanguageDisplay
    {
        [Tooltip("ISO 639-1 코드: ko / en / ja / zh / de / ru")]
        public string langCode;
        [Tooltip("로딩 패널에 표시할 이미지 (국기/심볼 등)")]
        public Sprite sprite;
        [Tooltip("표시 텍스트 (\"영어\", \"일본어\" 등)")]
        public string displayName;
    }

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
        videoPlayer.isLooping = true;   // 결과 영상 반복 재생

        if (videoRT == null)
        {
            videoRT = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            videoRT.Create();
        }
        videoPlayer.targetTexture = videoRT;

        if (videoDisplayImage != null)
        {
            videoDisplayImage.texture = videoRT;
            videoDisplayImage.uvRect = new Rect(1f, 0f, -1f, 1f);  // 좌우 반전 (셀카 효과 — 카메라 프리뷰와 일관성)
        }
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

        // 보이스 시프트: 결과 패널 진입 즉시 자동 재생 (isLooping=true로 반복)
        vp.Play();
        // StartCoroutine(PauseAfterFirstFrame());  // 무비디렉터식 첫 프레임 정지 — 보이스 시프트엔 불필요

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
        UpdateLoadingRotation();

#if UNITY_EDITOR
        // 에디터 전용: QR 패널에서 숫자 1 누르면 LanguagePanel로 스킵.
        // 실제 세션이 없어서 마지막 제출 단계는 동작 안 함 (UI 흐름 테스트용).
        if (qrPanel != null && qrPanel.activeSelf && !isTransitioning
            && Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            Debug.LogWarning("[GameManager] QR 임시 스킵 (개발용). 실제 세션 없음 → 결과 제출 불가, UI 흐름만 확인 가능.");
            StartCoroutine(TransitionTo(() =>
            {
                if (titlePanel != null) titlePanel.SetActive(false);
                if (qrPanel != null) qrPanel.SetActive(false);
                if (languagePanel != null) languagePanel.SetActive(true);
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

    // 항상 두 자리 분/초 (예: 30초 → "00:30", 9초 → "00:09")
    string FormatMMSS(float seconds)
    {
        int totalSec = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        int min = totalSec / 60;
        int sec = totalSec % 60;
        return $"{min:D2}:{sec:D2}";
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

        // 언어 선택 상태 리셋 (Target만 선택, Source는 ko 고정)
        selectedTargetLang = null;
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

        // Phase A(높이 조절 안내) 활성, Phase B/C 비활성으로 원복
        if (cameraPhaseAGroup != null) cameraPhaseAGroup.SetActive(true);
        if (cameraPhaseBGroup != null) cameraPhaseBGroup.SetActive(false);
        if (cameraPhaseCGroup != null) cameraPhaseCGroup.SetActive(false);
        if (recordingProgressFill != null) recordingProgressFill.fillAmount = 0f;
        if (recordingRemainingTimeText != null) recordingRemainingTimeText.text = FormatMMSS(recordingDuration);

        // 말하기 예시 안내 — Phase A에선 안 보이고 B 진입 시 켜짐. 닫힘 플래그만 원복.
        speakingExamplesClosed = false;
        if (speakingExamplesPanel != null) speakingExamplesPanel.SetActive(false);

        // 카운트다운 스프라이트 초기화
        if (cameraCountdownSpriteImage != null) cameraCountdownSpriteImage.gameObject.SetActive(false);

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
        if (WebSocketClient.Instance == null)
        {
            Debug.LogError("[GameManager] WebSocketClient.Instance 없음 — Hierarchy에 WebSocketClient GameObject 있는지/활성인지 확인");
            return;
        }
        if (APIManager.Instance == null)
        {
            Debug.LogError("[GameManager] APIManager.Instance 없음 — Hierarchy에 APIManager GameObject 있는지/활성인지 확인");
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

    // LanguageButton에서 호출. Target row 단일 선택.
    public void OnLanguageButtonClicked(LanguageButton btn)
    {
        if (isTransitioning || btn == null) return;

        selectedTargetLang = btn.langCode;
        if (targetLanguageButtons != null)
            foreach (var b in targetLanguageButtons) if (b != null) b.SetSelected(b == btn);

        Debug.Log($"[GameManager] 언어 선택 source={SOURCE_LANG}(고정) target={selectedTargetLang}");
        UpdateLanguageNextButton();
    }

    void UpdateLanguageNextButton()
    {
        if (languageNextButton == null) return;
        languageNextButton.interactable = !string.IsNullOrEmpty(selectedTargetLang);
    }

    // LanguagePanel의 "다음으로" 버튼 OnClick
    public void OnLanguageNext()
    {
        if (isTransitioning) return;
        if (string.IsNullOrEmpty(selectedTargetLang)) return;

        Debug.Log($"[GameManager] 언어 확정 → 카메라 조정 화면 source={SOURCE_LANG}(고정) target={selectedTargetLang}");

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

        // Phase A → B 스왑 (높이 조절 안내 끄고, 녹음 안내 켜기)
        if (cameraPhaseAGroup != null) cameraPhaseAGroup.SetActive(false);
        if (cameraPhaseBGroup != null) cameraPhaseBGroup.SetActive(true);

        // 말하기 예시 안내 — 사용자가 닫지 않았으면 활성
        if (speakingExamplesPanel != null) speakingExamplesPanel.SetActive(!speakingExamplesClosed);

        cameraCountdownCoroutine = StartCoroutine(CameraCountdown());
    }

    IEnumerator CameraCountdown()
    {
        // 부모 Image는 스프라이트 4프레임을 0.25s 간격으로 순환 (1초당 한 바퀴)
        // 자식 cameraCountdownText는 현재 남은 초(5→4→3→2→1) 표시
        if (cameraCountdownSpriteImage != null) cameraCountdownSpriteImage.gameObject.SetActive(true);

        float total = Mathf.Max(0.01f, cameraCountdownStart);
        float frameDur = Mathf.Max(0.01f, cameraCountdownFrameDuration);
        bool hasSprites = cameraCountdownSprites != null && cameraCountdownSprites.Length > 0;

        float elapsed = 0f;
        int lastFrame = -1;
        int lastSecond = -1;
        while (elapsed < total)
        {
            if (hasSprites && cameraCountdownSpriteImage != null)
            {
                int frame = Mathf.FloorToInt(elapsed / frameDur) % cameraCountdownSprites.Length;
                if (frame != lastFrame)
                {
                    cameraCountdownSpriteImage.sprite = cameraCountdownSprites[frame];
                    lastFrame = frame;
                }
            }

            if (cameraCountdownText != null)
            {
                int remaining = cameraCountdownStart - Mathf.FloorToInt(elapsed);
                if (remaining != lastSecond)
                {
                    cameraCountdownText.text = remaining.ToString();
                    lastSecond = remaining;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (cameraCountdownSpriteImage != null) cameraCountdownSpriteImage.gameObject.SetActive(false);
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

        // Phase A/B 모두 끄고, Phase C(녹음 중) 켜기 + 게이지바 0부터 시작
        if (cameraPhaseAGroup != null) cameraPhaseAGroup.SetActive(false);
        if (cameraPhaseBGroup != null) cameraPhaseBGroup.SetActive(false);
        if (cameraPhaseCGroup != null) cameraPhaseCGroup.SetActive(true);
        if (recordingProgressFill != null) recordingProgressFill.fillAmount = 0f;

        // 말하기 예시 안내 — B에서 닫혔으면 C에서도 계속 비활성, 안 닫혔으면 계속 표시
        if (speakingExamplesPanel != null) speakingExamplesPanel.SetActive(!speakingExamplesClosed);

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
        // 선택한 변환 언어 이미지/텍스트를 LoadingPanel에 반영
        ApplyTargetLanguageDisplay();

        // 말하기 예시 안내는 번역중 단계에서 숨김 (B/C에서만 노출).
        // 플래그(speakingExamplesClosed)는 그대로 두고 비활성만 — 홈 복귀 시 ResetAllPanelsImmediate가 일괄 원복.
        if (speakingExamplesPanel != null) speakingExamplesPanel.SetActive(false);

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

    // API 응답(200 동기 또는 WS RESULT_READY) 도착 시 호출. 게이지바 100% + ResultPanel 전환 + 영상 재생.
    void CompleteTranslation()
    {
        if (translationCoroutine != null)
        {
            StopCoroutine(translationCoroutine);
            translationCoroutine = null;
        }
        if (loadingBarFill != null) loadingBarFill.fillAmount = 1f;

        Debug.Log("[번역 완료]");

        // LoadingPanel이 활성 상태이고 영상 URL이 있으면 결과 패널로 전환
        if (loadingPanel != null && loadingPanel.activeSelf && !string.IsNullOrEmpty(currentVideoUrl))
            StartCoroutine(CompleteAndTransitionToResult());
    }

    // === VideoRecorder 이벤트 핸들러 ===

    // 30초 진행률 (0~1). 게이지바 + 시간 텍스트 갱신.
    void HandleRecorderProgress(float progress01)
    {
        float remaining = recordingDuration * (1f - progress01);

        // 왼쪽 텍스트: "00:30" → "00:00" 형식 카운트다운
        if (recordingRemainingTimeText != null)
            recordingRemainingTimeText.text = FormatMMSS(remaining);

        // (legacy) 기존 숫자 타이머 텍스트도 유지 — 미연결이면 무시
        if (recordingTimerText != null)
            recordingTimerText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();

        // 게이지바 (왼쪽에서 오른쪽으로 채워짐)
        if (recordingProgressFill != null)
            recordingProgressFill.fillAmount = Mathf.Clamp01(progress01);
    }

    // 말하기 예시 패널의 X(닫기) 버튼 OnClick — 안내만 끄고 진행은 계속.
    // 한 번 닫으면 Phase B → C 전환 시에도 다시 안 뜸. ResetAllPanelsImmediate에서 원복.
    public void OnCloseSpeakingExamples()
    {
        speakingExamplesClosed = true;
        if (speakingExamplesPanel != null) speakingExamplesPanel.SetActive(false);
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
        if (WebSocketClient.Instance == null)
        {
            Debug.LogError("[GameManager] WebSocketClient.Instance 없음 — Hierarchy에 WebSocketClient GameObject 있는지/활성인지 확인");
            return;
        }
        if (APIManager.Instance == null)
        {
            Debug.LogError("[GameManager] APIManager.Instance 없음 — Hierarchy에 APIManager GameObject 있는지/활성인지 확인");
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

    // LoadingPanel 활성 중에만 회전 이미지를 Z축으로 천천히 돌림
    void UpdateLoadingRotation()
    {
        if (loadingRotatingImage == null) return;
        if (loadingPanel == null || !loadingPanel.activeSelf) return;
        loadingRotatingImage.Rotate(0f, 0f, -loadingRotationSpeed * Time.deltaTime);
    }

    // 선택한 변환 언어(selectedTargetLang)에 맞는 이미지/텍스트를 로딩 패널에 적용
    void ApplyTargetLanguageDisplay()
    {
        if (targetLanguageDisplays == null) return;
        if (string.IsNullOrEmpty(selectedTargetLang)) return;

        foreach (var d in targetLanguageDisplays)
        {
            if (d == null) continue;
            if (d.langCode != selectedTargetLang) continue;

            if (loadingTargetLanguageImage != null)
            {
                loadingTargetLanguageImage.sprite = d.sprite;
                loadingTargetLanguageImage.enabled = d.sprite != null;
            }
            if (loadingTargetLanguageText != null)
                loadingTargetLanguageText.text = d.displayName ?? "";
            return;
        }

        Debug.LogWarning($"[GameManager] targetLanguageDisplays에 '{selectedTargetLang}' 매핑 없음 — Inspector 확인.");
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
            if (cameraPanel != null) cameraPanel.SetActive(false);  // 보이스 시프트: LoadingPanel의 부모 CameraPanel도 같이 끔
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

        // QR 팝업 동안 영상 일시정지 (오디오 정지)
        if (videoPlayer != null && videoPlayer.isPlaying) videoPlayer.Pause();

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

    // 결과 QR 팝업 - "X 닫기" 버튼 OnClick → 팝업 닫고 ResultPanel로 복귀
    public void OnCloseResultQRClick()
    {
        if (isTransitioning) return;
        if (resultQRPanel == null) return;

        StartCoroutine(TransitionTo(() =>
        {
            resultQRPanel.SetActive(false);
            // QR 팝업 닫으면 영상 재개
            if (videoPlayer != null && videoPlayer.isPrepared) videoPlayer.Play();
        }));
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
