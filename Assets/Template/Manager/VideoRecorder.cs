using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

// 30초 영상 녹화 — WebCamTexture 프레임(JPG 시퀀스) + Microphone 오디오(WAV) →
//   RecordingDuration 만료 시 ffmpeg로 mp4 합성 후 OnRecordingComplete(mp4Path, errorMsg) 호출.
//
// WebCamTexture는 가능하면 CameraPreview에서 쓰던 인스턴스를 재사용 (재초기화 비용 회피).
public class VideoRecorder : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] CameraPreview cameraPreviewRef;

    [Header("녹화 설정")]
    [SerializeField] float recordingDuration = 30f;
    [SerializeField] int captureFrameRate = 15;
    [SerializeField] int jpegQuality = 80;

    [Header("마이크 설정")]
    [SerializeField] string preferredMicDevice = "";
    [SerializeField] int micSampleRate = 44100;

    /// 30초 녹화 시간이 끝난 "직후" 호출 (ffmpeg 합성은 아직 진행 중).
    /// UI를 LoadingPanel로 즉시 전환하는 용도.
    public event Action OnRecordingStopped;

    /// 녹화 완료 시 호출 (성공 시 mp4Path, 실패 시 errorMsg). 실패 시 mp4Path=null.
    /// ffmpeg 합성까지 끝난 뒤 발사 → 서버 제출 트리거로 사용.
    public event Action<string, string> OnRecordingComplete;

    /// 진행률 0~1
    public event Action<float> OnProgress;

    bool recording;
    string sessionDir;
    string framesDir;
    string wavPath;
    string mp4Path;
    WebCamTexture webcam;
    string micDevice;
    AudioClip micClip;
    RenderTexture rt;
    Texture2D readbackTex;

    public bool IsRecording => recording;
    public AudioClip MicClip => micClip;
    public string MicDevice => micDevice;
    public bool IsMicActive => !string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice);

    public void StartRecording()
    {
        if (recording)
        {
            Debug.LogWarning("[VideoRecorder] 이미 녹화 중");
            return;
        }

        if (!TryAcquireWebcam())
        {
            OnRecordingComplete?.Invoke(null, "NO_WEBCAM");
            return;
        }

        if (!TryStartMicrophone())
        {
            OnRecordingComplete?.Invoke(null, "NO_MICROPHONE");
            return;
        }

        PrepareSessionDir();

        int w = webcam.width;
        int h = webcam.height;
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        readbackTex = new Texture2D(w, h, TextureFormat.RGB24, false);

        recording = true;
        StartCoroutine(RecordLoop(w, h));
    }

    public void CancelRecording()
    {
        if (!recording) return;
        recording = false;

        if (!string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice))
            Microphone.End(micDevice);

        Cleanup(keepFiles: false);
    }

    bool TryAcquireWebcam()
    {
        // 1) CameraPreview가 이미 쓰던 웹캠 재사용
        if (cameraPreviewRef != null && cameraPreviewRef.CurrentWebCamTexture != null)
        {
            webcam = cameraPreviewRef.CurrentWebCamTexture;
            if (!webcam.isPlaying) webcam.Play();
            return true;
        }

        // 2) 없으면 새로 생성
        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            Debug.LogError("[VideoRecorder] 사용 가능한 웹캠 없음");
            return false;
        }
        webcam = new WebCamTexture(devices[0].name, 1280, 720, captureFrameRate);
        webcam.Play();
        return true;
    }

    bool TryStartMicrophone()
    {
        var mics = Microphone.devices;
        if (mics == null || mics.Length == 0)
        {
            Debug.LogError("[VideoRecorder] 마이크 없음");
            return false;
        }

        // 디바이스 목록 전부 로깅 — preferredMicDevice 설정 시 참고용
        Debug.Log($"[VideoRecorder] 마이크 디바이스 목록 ({mics.Length}개):");
        for (int i = 0; i < mics.Length; i++)
            Debug.Log($"  [{i}] {mics[i]}");

        micDevice = !string.IsNullOrEmpty(preferredMicDevice) ? preferredMicDevice : mics[0];
        int lengthSec = Mathf.CeilToInt(recordingDuration) + 1;
        // loop=true: 일부 USB 마이크에서 loop=false면 초기 buffer가 0으로만 잡히는 이슈 회피.
        // recordingDuration < lengthSec이라 30초 안엔 loop가 일어나지 않음 → WAV 저장에도 영향 없음.
        micClip = Microphone.Start(micDevice, true, lengthSec, micSampleRate);

        // 즉시 시작 안 되는 경우 대비 short wait
        float t0 = Time.realtimeSinceStartup;
        while (Microphone.GetPosition(micDevice) <= 0 && Time.realtimeSinceStartup - t0 < 1f) { }

        Debug.Log($"[VideoRecorder] mic start device='{micDevice}' rate={micSampleRate}");
        return true;
    }

    void PrepareSessionDir()
    {
        string root = Path.Combine(Application.temporaryCachePath, "voiceshift");
        sessionDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        framesDir = Path.Combine(sessionDir, "frames");
        wavPath = Path.Combine(sessionDir, "audio.wav");
        mp4Path = Path.Combine(sessionDir, "visitor.mp4");
        Directory.CreateDirectory(framesDir);
    }

    IEnumerator RecordLoop(int w, int h)
    {
        int frameIdx = 0;
        float elapsed = 0f;
        float interval = 1f / Mathf.Max(1, captureFrameRate);
        float nextCapture = 0f;

        while (recording && elapsed < recordingDuration)
        {
            elapsed += Time.deltaTime;
            OnProgress?.Invoke(Mathf.Clamp01(elapsed / recordingDuration));

            if (elapsed >= nextCapture)
            {
                nextCapture += interval;
                CaptureFrame(frameIdx, w, h);
                frameIdx++;
            }
            yield return null;
        }

        yield return FinishRecording(frameIdx);
    }

    void CaptureFrame(int idx, int w, int h)
    {
        try
        {
            Graphics.Blit(webcam, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readbackTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            readbackTex.Apply(false);
            RenderTexture.active = prev;

            byte[] jpg = readbackTex.EncodeToJPG(jpegQuality);
            string framePath = Path.Combine(framesDir, $"frame_{idx:D5}.jpg");
            File.WriteAllBytes(framePath, jpg);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VideoRecorder] frame {idx} 캡처 실패: {e.Message}");
        }
    }

    IEnumerator FinishRecording(int framesCaptured)
    {
        recording = false;

        // 1) 녹화 타이머 종료 즉시 UI 전환 신호 — ffmpeg 합성 기다리지 않음
        OnRecordingStopped?.Invoke();

        // 2) 마이크 정지 & WAV 저장
        if (Microphone.IsRecording(micDevice))
            Microphone.End(micDevice);

        if (micClip != null)
        {
            try { WavEncoder.Save(micClip, wavPath); }
            catch (Exception e) { Debug.LogError($"[VideoRecorder] wav 저장 실패: {e.Message}"); }
        }

        // 웹캠은 이후 패널에서 더 안 씀 → 멈춤
        if (cameraPreviewRef != null)
            cameraPreviewRef.ReleaseWebCam();
        else if (webcam != null && webcam.isPlaying)
            webcam.Stop();

        // ffmpeg 합성 (async). 코루틴에서 Task 완료 대기
        Task<bool> encodeTask = FfmpegEncoder.EncodeAsync(framesDir, wavPath, mp4Path, captureFrameRate);
        while (!encodeTask.IsCompleted)
            yield return null;

        if (encodeTask.Result)
        {
            Debug.Log($"[VideoRecorder] encode 완료: {mp4Path}");
            OnRecordingComplete?.Invoke(mp4Path, null);
        }
        else
        {
            Debug.LogError("[VideoRecorder] mp4 합성 실패");
            OnRecordingComplete?.Invoke(null, "ENCODE_FAILED");
        }
    }

    void Cleanup(bool keepFiles)
    {
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        if (readbackTex != null) { Destroy(readbackTex); readbackTex = null; }
        if (!keepFiles && !string.IsNullOrEmpty(sessionDir) && Directory.Exists(sessionDir))
        {
            try { Directory.Delete(sessionDir, true); } catch { /* ignore */ }
        }
    }

    void OnDestroy()
    {
        Cleanup(keepFiles: true);
    }
}
