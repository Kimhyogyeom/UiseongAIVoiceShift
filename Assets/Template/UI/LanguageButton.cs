using UnityEngine;
using UnityEngine.UI;

// LanguagePanel의 언어 버튼. 변환할 언어(Target) 한 row만, 언어 코드.
// 선택 시 스프라이트 교체(우선) 또는 색상 변경(fallback). 입력 언어는 한국어 고정이라 Source UI 없음.
public class LanguageButton : MonoBehaviour
{
    [Header("Language")]
    [Tooltip("ISO 639-1 코드. ko / ja / zh / en / de / ru")]
    public string langCode;

    [Header("Visual — Sprite Swap (우선)")]
    [Tooltip("교체할 Image. 비워두면 이 GameObject의 Image 자동 사용.")]
    public Image targetImage;
    [Tooltip("선택 전 기본 스프라이트. 미리 준비된 이미지 등록.")]
    public Sprite normalSprite;
    [Tooltip("선택됐을 때 스프라이트. 미리 준비된 이미지 등록.")]
    public Sprite selectedSprite;

    [Header("Visual — Color Fallback (스프라이트 미연결 시)")]
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(0.4f, 0.7f, 1f);

    void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        SetSelected(false);

        var btn = GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(HandleClick);
    }

    void HandleClick()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("[LanguageButton] GameManager.Instance 없음");
            return;
        }
        GameManager.Instance.OnLanguageButtonClicked(this);
    }

    public void SetSelected(bool selected)
    {
        if (targetImage == null) return;

        // normalSprite/selectedSprite 둘 다 등록돼 있으면 스프라이트 교체 모드
        if (normalSprite != null && selectedSprite != null)
        {
            targetImage.sprite = selected ? selectedSprite : normalSprite;
            // 컬러 간섭 없게 흰색 유지 (스프라이트 본연의 색상 사용)
            targetImage.color = Color.white;
        }
        else
        {
            // 스프라이트 미연결 시 기존 컬러 변경으로 동작 (하위호환)
            targetImage.color = selected ? selectedColor : normalColor;
        }
    }
}
