using UnityEngine;
using UnityEngine.UI;

// LanguagePanel의 언어 버튼. 위 row(Source) / 아래 row(Target) 구분, 언어 코드, 선택 시 색상 변화.
public class LanguageButton : MonoBehaviour
{
    public enum LangSlot { Source, Target }

    [Header("Language")]
    public LangSlot slot;
    [Tooltip("ISO 639-1 코드. ko / en / ja / zh / vi / es")]
    public string langCode;

    [Header("Visual")]
    [Tooltip("색상을 바꿀 Image. 비워두면 이 GameObject의 Image 자동 사용.")]
    public Image targetImage;
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
        if (targetImage != null)
            targetImage.color = selected ? selectedColor : normalColor;
    }
}
