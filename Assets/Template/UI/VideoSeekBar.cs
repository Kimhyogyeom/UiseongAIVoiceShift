using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

// 결과 영상 진행바를 클릭/드래그해서 탐색. 핸들(thumb) 없이 트랙 자체가 입력 영역.
// 사용:
//   1) 트랙용 Image GameObject에 이 컴포넌트 부착 (Image의 Raycast Target = true 필수)
//   2) Inspector에서 VideoPlayer, FillImage 연결
//   3) 트랙 위에 클릭/드래그 → 그 위치로 영상 시킹 + Fill 즉시 갱신
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Graphic))]
public class VideoSeekBar : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Tooltip("탐색할 VideoPlayer.")]
    public VideoPlayer videoPlayer;
    [Tooltip("진행 표시용 Image (Filled, Horizontal). 드래그 즉시 fillAmount 반영.")]
    public Image fillImage;

    RectTransform rect;
    Canvas rootCanvas;

    public bool IsDragging { get; private set; }

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) rootCanvas = canvas.rootCanvas;
    }

    public void OnPointerDown(PointerEventData e)
    {
        IsDragging = true;
        SeekToPointer(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (IsDragging) SeekToPointer(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        IsDragging = false;
    }

    void OnDisable() { IsDragging = false; }

    void SeekToPointer(PointerEventData e)
    {
        if (videoPlayer == null || !videoPlayer.isPrepared) return;
        if (rect == null) return;

        // ScreenSpaceOverlay 캔버스는 카메라 인자에 null을 넘겨야 정확함
        Camera cam = (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : e.pressEventCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, cam, out Vector2 local))
            return;

        Rect r = rect.rect;
        float normalized = Mathf.Clamp01(Mathf.InverseLerp(r.xMin, r.xMax, local.x));

        if (fillImage != null) fillImage.fillAmount = normalized;

        double total = videoPlayer.length;
        if (total > 0) videoPlayer.time = normalized * total;
    }
}
