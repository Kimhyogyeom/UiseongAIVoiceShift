using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// CJK(일본어/중국어) 글리프 fallback 자동 설정용 Editor 스크립트.
// 사용법:
//   1) Noto Sans CJK KR (또는 Source Han Sans) .otf/.ttf 파일을 Assets/Template/Font/ 에 드롭
//   2) Unity 메뉴 Tools → VoiceShift → Setup CJK Fallback 클릭
// 동작:
//   - 폰트 폴더에서 "Noto"/"SourceHan"/"CJK" 이름 포함된 폰트 파일 찾아서
//   - Dynamic SDF 모드 TMP_FontAsset 생성 ("<원본이름> SDF.asset")
//   - Pretendard-Bold SDF / Pretendard-Light SDF 의 fallbackFontAssetTable에 등록
public static class CjkFallbackSetup
{
    const string FontDir = "Assets/Template/Font";

    [MenuItem("Tools/VoiceShift/Setup CJK Fallback")]
    public static void Run()
    {
        Font sourceFont = FindCjkSourceFont();
        if (sourceFont == null)
        {
            EditorUtility.DisplayDialog(
                "CJK Fallback Setup",
                $"CJK 폰트 파일을 {FontDir}/ 에 먼저 넣어주세요.\n" +
                "파일명에 'Noto', 'SourceHan', 'CJK' 중 하나가 포함돼야 자동 인식됩니다.\n\n" +
                "추천: Noto Sans CJK KR Regular.otf",
                "확인");
            return;
        }

        string sourceAssetPath = AssetDatabase.GetAssetPath(sourceFont);
        string sourceName = Path.GetFileNameWithoutExtension(sourceAssetPath);
        string sdfAssetPath = $"{FontDir}/{sourceName} SDF.asset";

        TMP_FontAsset cjkSdf = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfAssetPath);
        if (cjkSdf == null)
        {
            cjkSdf = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                samplingPointSize: 90,
                atlasPadding: 9,
                GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            AssetDatabase.CreateAsset(cjkSdf, sdfAssetPath);
            Debug.Log($"[CjkFallbackSetup] SDF 생성: {sdfAssetPath}");
        }
        else
        {
            Debug.Log($"[CjkFallbackSetup] 기존 SDF 재사용: {sdfAssetPath}");
        }

        int wired = 0;
        wired += AddFallback($"{FontDir}/Pretendard-Bold SDF.asset", cjkSdf);
        wired += AddFallback($"{FontDir}/Pretendard-Light SDF.asset", cjkSdf);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "CJK Fallback Setup",
            $"완료.\n- CJK SDF: {sdfAssetPath}\n- Pretendard에 fallback 등록: {wired}개",
            "확인");
    }

    static Font FindCjkSourceFont()
    {
        if (!Directory.Exists(FontDir)) return null;

        string[] guids = AssetDatabase.FindAssets("t:Font", new[] { FontDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name.Contains("noto") || name.Contains("sourcehan") || name.Contains("source han") || name.Contains("cjk"))
            {
                return AssetDatabase.LoadAssetAtPath<Font>(path);
            }
        }
        return null;
    }

    static int AddFallback(string targetSdfPath, TMP_FontAsset fallback)
    {
        var target = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetSdfPath);
        if (target == null)
        {
            Debug.LogWarning($"[CjkFallbackSetup] 대상 SDF 없음: {targetSdfPath}");
            return 0;
        }

        if (target.fallbackFontAssetTable == null)
            target.fallbackFontAssetTable = new List<TMP_FontAsset>();

        if (target.fallbackFontAssetTable.Contains(fallback))
        {
            Debug.Log($"[CjkFallbackSetup] 이미 등록됨: {targetSdfPath}");
            return 1;
        }

        target.fallbackFontAssetTable.Add(fallback);
        EditorUtility.SetDirty(target);
        Debug.Log($"[CjkFallbackSetup] fallback 등록: {targetSdfPath} ← {fallback.name}");
        return 1;
    }
}
