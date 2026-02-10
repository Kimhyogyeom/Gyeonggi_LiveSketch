using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스캔 이미지에서 검은 테두리만 제거하고 컬러는 그대로 유지
/// </summary>
public static class ColorExtractor
{
    // 검은색 판별 기준 (밝기)
    private const float BLACK_V_THRESHOLD = 0.30f;  // V < 30% = 검은색

    /// <summary>
    /// 검은 테두리만 흰색으로 변환, 나머지 컬러는 원본 그대로
    /// </summary>
    public static Texture2D ExtractColorsWithBorderMask(Texture2D source, bool removeOutside = true)
    {
        return RemoveBlackLines(source);
    }

    /// <summary>
    /// 검은 테두리만 흰색으로 변환
    /// </summary>
    public static Texture2D RemoveBlackLines(Texture2D source)
    {
        if (source == null) return null;

        int width = source.width;
        int height = source.height;
        var pixels = source.GetPixels32();

        for (int i = 0; i < pixels.Length; i++)
        {
            Color color = pixels[i];
            Color.RGBToHSV(color, out float h, out float s, out float v);

            // 어두운 픽셀(테두리)만 흰색으로 변환
            if (v < BLACK_V_THRESHOLD)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            // 나머지는 원본 그대로 유지
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(pixels);
        result.Apply(false, false);
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    /// <summary>
    /// 컬러 추출 없이 원본 그대로 반환 (테스트용)
    /// </summary>
    public static Texture2D ExtractColorsOnly(Texture2D source)
    {
        return RemoveBlackLines(source);
    }
}
