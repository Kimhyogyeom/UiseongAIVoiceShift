using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class BouncingText : MonoBehaviour
{
    [Tooltip("글자가 튕기는 최대 높이 (Unity 단위). 값이 클수록 높이 튐.")]
    public float bounceHeight = 10f;

    [Tooltip("한 글자가 한 번 튕기는 데 걸리는 시간 (초)")]
    public float bounceDuration = 0.4f;

    [Tooltip("인접 글자 사이의 시작 간격 (초). 작을수록 파도처럼 빠르게 이어짐.")]
    public float perCharDelay = 0.07f;

    [Tooltip("한 사이클 종료 후 다음 사이클까지 대기 시간 (초)")]
    public float cycleGap = 1.5f;

    [Tooltip("반복 여부")]
    public bool loop = true;

    TMP_Text tmp;

    void OnEnable()
    {
        tmp = GetComponent<TMP_Text>();
        StartCoroutine(AnimateLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator AnimateLoop()
    {
        while (true)
        {
            yield return StartCoroutine(AnimateOnce());
            if (!loop) yield break;
            yield return new WaitForSeconds(cycleGap);
        }
    }

    IEnumerator AnimateOnce()
    {
        tmp.ForceMeshUpdate();
        TMP_TextInfo textInfo = tmp.textInfo;
        int charCount = textInfo.characterCount;
        if (charCount == 0) yield break;

        // 원본 vertex 백업
        Vector3[][] origVerts = new Vector3[textInfo.meshInfo.Length][];
        for (int m = 0; m < textInfo.meshInfo.Length; m++)
        {
            origVerts[m] = (Vector3[])textInfo.meshInfo[m].vertices.Clone();
        }

        float total = (charCount - 1) * perCharDelay + bounceDuration;
        float t = 0f;

        while (t < total)
        {
            t += Time.deltaTime;

            for (int ci = 0; ci < charCount; ci++)
            {
                TMP_CharacterInfo ch = textInfo.characterInfo[ci];
                if (!ch.isVisible) continue;

                int matIdx = ch.materialReferenceIndex;
                int vi = ch.vertexIndex;
                Vector3[] verts = textInfo.meshInfo[matIdx].vertices;
                Vector3[] orig = origVerts[matIdx];

                float charT = t - ci * perCharDelay;
                float offsetY = 0f;
                if (charT > 0f && charT < bounceDuration)
                {
                    float phase = charT / bounceDuration;
                    offsetY = Mathf.Sin(phase * Mathf.PI) * bounceHeight;
                }

                Vector3 offset = new Vector3(0, offsetY, 0);
                verts[vi + 0] = orig[vi + 0] + offset;
                verts[vi + 1] = orig[vi + 1] + offset;
                verts[vi + 2] = orig[vi + 2] + offset;
                verts[vi + 3] = orig[vi + 3] + offset;
            }

            for (int m = 0; m < textInfo.meshInfo.Length; m++)
            {
                var meshInfo = textInfo.meshInfo[m];
                meshInfo.mesh.vertices = meshInfo.vertices;
                tmp.UpdateGeometry(meshInfo.mesh, m);
            }

            yield return null;
        }

        // 원래 자리로 복구
        for (int m = 0; m < textInfo.meshInfo.Length; m++)
        {
            System.Array.Copy(origVerts[m], textInfo.meshInfo[m].vertices, origVerts[m].Length);
            var meshInfo = textInfo.meshInfo[m];
            meshInfo.mesh.vertices = meshInfo.vertices;
            tmp.UpdateGeometry(meshInfo.mesh, m);
        }
    }
}
