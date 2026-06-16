using UnityEngine;
using UnityEngine.UI;

// RawImage에 둥근 코너 적용. RawImage 옆에 붙이면 자동으로 머티리얼 생성 + 사이즈 추적.
// Inspector의 Corner Radius 슬라이더로 코너 반경(픽셀) 조절.
[RequireComponent(typeof(RawImage))]
[ExecuteAlways]
[DisallowMultipleComponent]
public class RoundedRawImage : MonoBehaviour
{
    [Tooltip("코너 반경 (픽셀). 0 = 사각형, 클수록 둥글어짐.")]
    [Range(0f, 300f)]
    public float cornerRadius = 30f;

    RawImage rawImage;
    RectTransform rt;
    Material runtimeMat;
    static Shader cachedShader;

    void OnEnable()
    {
        rawImage = GetComponent<RawImage>();
        rt = GetComponent<RectTransform>();
        EnsureMaterial();
        Apply();
    }

    void OnDisable()
    {
        if (rawImage != null && runtimeMat != null && rawImage.material == runtimeMat)
            rawImage.material = null;
        if (runtimeMat != null)
        {
            if (Application.isPlaying) Destroy(runtimeMat);
            else DestroyImmediate(runtimeMat);
            runtimeMat = null;
        }
    }

    void OnRectTransformDimensionsChange() => Apply();
    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        EnsureMaterial();
        Apply();
    }

    void EnsureMaterial()
    {
        if (rawImage == null) rawImage = GetComponent<RawImage>();
        if (cachedShader == null) cachedShader = Shader.Find("UI/RoundedRawImage");
        if (cachedShader == null)
        {
            Debug.LogWarning("[RoundedRawImage] 'UI/RoundedRawImage' 셰이더를 찾을 수 없음. Assets/Template/Shaders/UIRoundedRawImage.shader가 있는지 확인.");
            return;
        }

        if (runtimeMat == null || runtimeMat.shader != cachedShader)
        {
            runtimeMat = new Material(cachedShader) { name = "RoundedRawImage (Runtime)" };
            rawImage.material = runtimeMat;
        }
    }

    void Apply()
    {
        if (runtimeMat == null || rt == null) return;
        Vector2 size = rt.rect.size;
        runtimeMat.SetVector("_RectSize", new Vector4(size.x, size.y, 0, 0));
        runtimeMat.SetFloat("_CornerRadius", cornerRadius);
    }
}
