using UnityEngine;
using UnityEditor;
using System.IO;

public class FixMobileFVMaterials : EditorWindow
{
    [MenuItem("Tools/Fix Mobile_FV Materials (URP)")]
    public static void FixMaterials()
    {
        // URP Lit 셰이더 찾기
        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        Shader urpSimpleLitShader = Shader.Find("Universal Render Pipeline/Simple Lit");

        if (urpLitShader == null)
        {
            Debug.LogError("URP Lit 셰이더를 찾을 수 없습니다. URP 패키지가 설치되어 있는지 확인하세요.");
            return;
        }

        // Mobile_FV 폴더의 모든 머티리얼 찾기
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Mobile_FV" });

        int fixedCount = 0;

        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null) continue;

            // 현재 셰이더가 핑크색을 유발하는지 확인 (셰이더가 없거나 Hidden/InternalErrorShader인 경우)
            bool needsFix = mat.shader == null ||
                           mat.shader.name == "Hidden/InternalErrorShader" ||
                           mat.shader.name.StartsWith("MobileFV/") ||
                           mat.shader.name.StartsWith("Nature/") ||
                           mat.shader.name.Contains("SpeedTree");

            // 알파 클리핑이 안 된 URP 머티리얼도 수정
            bool needsAlphaFix = mat.shader.name.StartsWith("Universal Render Pipeline") &&
                                 mat.GetFloat("_AlphaClip") != 1.0f;

            if (needsFix || needsAlphaFix || !mat.shader.name.StartsWith("Universal Render Pipeline"))
            {
                // 기존 텍스처 저장
                Texture mainTex = null;
                Color color = Color.white;

                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                {
                    mainTex = mat.GetTexture("_BaseMap");
                }
                else if (mat.HasProperty("_MainTex"))
                {
                    mainTex = mat.GetTexture("_MainTex");
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    color = mat.GetColor("_BaseColor");
                }
                else if (mat.HasProperty("_Color"))
                {
                    color = mat.GetColor("_Color");
                }

                // URP Simple Lit으로 변경 (더 가볍고 모바일 친화적)
                mat.shader = urpSimpleLitShader != null ? urpSimpleLitShader : urpLitShader;

                // 텍스처와 색상 복원
                if (mainTex != null)
                {
                    mat.SetTexture("_BaseMap", mainTex);
                }
                mat.SetColor("_BaseColor", color);

                // 기본 설정
                mat.SetFloat("_Smoothness", 0.0f);

                // 알파 클리핑 활성화 (잎사귀 투명 처리)
                mat.SetFloat("_AlphaClip", 1.0f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = 2450; // AlphaTest queue

                EditorUtility.SetDirty(mat);
                fixedCount++;

                Debug.Log($"Fixed material: {path}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"완료! {fixedCount}개의 머티리얼을 URP 셰이더로 변경했습니다.");
        EditorUtility.DisplayDialog("Mobile_FV 머티리얼 수정 완료",
            $"{fixedCount}개의 머티리얼을 URP 셰이더로 변경했습니다.", "확인");
    }
}
