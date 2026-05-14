using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 마이크 음량(0~1)을 받아서 N개의 막대 RectTransform 높이를 동적으로 조절.
// 각 막대마다 sin 위상차를 줘서 자연스러운 파형 효과 연출.
// 좌측 / 가운데 / 우측 3색 그라데이션 자동 분배 (Awake 시 1회 적용).
public class VoiceWaveform : MonoBehaviour
{
    [Header("Bars")]
    [Tooltip("막대 RectTransform 배열. 비워두면 이 GameObject의 자식들을 자동 수집.")]
    public RectTransform[] bars;

    [Header("Height Range")]
    public float minHeight = 10f;
    public float maxHeight = 200f;

    [Header("Animation")]
    [Tooltip("막대 높이 변화 부드러움 (높을수록 즉각 반응)")]
    public float lerpSpeed = 12f;
    [Tooltip("막대 간 위상차 (자연스러운 파형 효과)")]
    public float phaseOffset = 0.4f;
    [Tooltip("위상 시간 스케일 (높을수록 빠르게 출렁임)")]
    public float waveSpeed = 4f;

    [Header("Colors (좌 → 중앙 → 우 3색 그라데이션)")]
    public bool applyColorsOnAwake = true;
    public Color leftColor   = new Color(0.50f, 0.78f, 1.00f, 1f);   // 옅은 하늘색
    public Color centerColor = new Color(0.65f, 0.30f, 1.00f, 1f);   // 보라
    public Color rightColor  = new Color(1.00f, 0.50f, 0.70f, 1f);   // 옅은 핑크

    float currentLevel;

    void Awake()
    {
        // Inspector에 비어있으면 자식들 자동 수집
        if (bars == null || bars.Length == 0)
        {
            var list = new List<RectTransform>();
            foreach (Transform child in transform)
            {
                if (child is RectTransform rt) list.Add(rt);
            }
            bars = list.ToArray();
        }

        if (applyColorsOnAwake) ApplyColors();
    }

    // 좌 → 중앙 → 우 3색 그라데이션을 막대들에 자동 분배. 알파는 강제 1.0 (투명 사고 방지).
    public void ApplyColors()
    {
        if (bars == null || bars.Length == 0) return;

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null) continue;
            var img = bars[i].GetComponent<Image>();
            if (img == null) continue;

            float t = bars.Length == 1 ? 0.5f : (float)i / (bars.Length - 1);
            Color c = (t < 0.5f)
                ? Color.Lerp(leftColor, centerColor, t * 2f)
                : Color.Lerp(centerColor, rightColor, (t - 0.5f) * 2f);
            c.a = 1f;  // 알파 강제 1.0 — 실수로 투명되는 사고 방지
            img.color = c;
        }
    }

    // GameManager가 매 프레임 호출. 0~1 범위 음량 입력.
    public void SetLevel(float level)
    {
        currentLevel = Mathf.Clamp01(level);
    }

    void Update()
    {
        if (bars == null) return;

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null) continue;

            // 인접 막대마다 위상차로 시간차 출렁임 효과
            float wave = Mathf.Sin(Time.time * waveSpeed + i * phaseOffset) * 0.3f + 0.7f;
            float target = Mathf.Lerp(minHeight, maxHeight, currentLevel * wave);

            var size = bars[i].sizeDelta;
            size.y = Mathf.Lerp(size.y, target, Time.deltaTime * lerpSpeed);
            bars[i].sizeDelta = size;
        }
    }

    void OnDisable()
    {
        // 비활성화 시 모든 막대 최소 높이로 즉시 리셋
        if (bars == null) return;
        foreach (var bar in bars)
        {
            if (bar == null) continue;
            var size = bar.sizeDelta;
            size.y = minHeight;
            bar.sizeDelta = size;
        }
        currentLevel = 0f;
    }
}
