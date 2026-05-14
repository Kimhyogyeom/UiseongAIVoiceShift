using UnityEngine;
using UnityEngine.UI;

// CameraPanel에 부착. 패널이 활성화되면 카메라를 켜고, 비활성화되면 자동으로 끈다.
// WebCamTexture는 권한이 필요하므로 첫 실행 시 OS 권한 팝업이 뜰 수 있음.
public class CameraPreview : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("카메라 영상이 표시될 RawImage")]
    public RawImage previewImage;

    [Header("Settings")]
    [Tooltip("기본 카메라 인덱스. 여러 카메라가 있을 때 0이 보통 내장.")]
    public int cameraIndex = 0;

    [Tooltip("WebCamTexture가 좌우 반전되어 보일 때 켜기 (셀카 효과)")]
    public bool mirrorHorizontal = false;

    WebCamTexture webcamTexture;

    /// VideoRecorder가 녹화에 재사용할 수 있게 현재 WebCamTexture 노출.
    public WebCamTexture CurrentWebCamTexture => webcamTexture;

    void OnEnable()
    {
        StartCamera();
    }

    void OnDisable()
    {
        StopCamera();
    }

    /// 녹화 종료 후 명시적으로 웹캠 정지하고 싶을 때 (다음 패널에서 재사용 안 할 때).
    public void ReleaseWebCam()
    {
        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying) webcamTexture.Stop();
            webcamTexture = null;
        }
        if (previewImage != null) previewImage.texture = null;
    }

    void StartCamera()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("[CameraPreview] 사용 가능한 카메라 없음");
            return;
        }

        int idx = Mathf.Clamp(cameraIndex, 0, devices.Length - 1);

        if (webcamTexture == null || webcamTexture.deviceName != devices[idx].name)
        {
            webcamTexture = new WebCamTexture(devices[idx].name);
        }

        if (previewImage != null)
        {
            previewImage.texture = webcamTexture;

            // 좌우 반전 (셀카 느낌)
            var rt = previewImage.rectTransform;
            var scale = rt.localScale;
            scale.x = Mathf.Abs(scale.x) * (mirrorHorizontal ? -1f : 1f);
            rt.localScale = scale;
        }
        else
        {
            Debug.LogWarning("[CameraPreview] previewImage 미연결");
        }

        if (!webcamTexture.isPlaying)
            webcamTexture.Play();

        Debug.Log($"[CameraPreview] 카메라 시작: {devices[idx].name} ({webcamTexture.width}x{webcamTexture.height})");
    }

    void StopCamera()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
            Debug.Log("[CameraPreview] 카메라 정지");
        }
    }
}
