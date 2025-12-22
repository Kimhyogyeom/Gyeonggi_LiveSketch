using UnityEngine;

/// <summary>
/// 스캔 도안에서 테두리/윤곽선(검은색, 회색)을 제거하고
/// 유아가 칠한 컬러만 추출합니다.
/// </summary>
public static class ColorExtractor
{
    /// <summary>
    /// 무채색(검정/회색) 판단 임계값
    /// 채도가 이 값보다 낮으면 무채색으로 판단
    /// </summary>
    private const float SATURATION_THRESHOLD = 0.15f;

    /// <summary>
    /// 어두운 색 판단 임계값
    /// 밝기가 이 값보다 낮으면 검은색 계열로 판단
    /// </summary>
    private const float DARK_THRESHOLD = 0.4f;

    /// <summary>
    /// 밝은 색(흰색/배경) 판단 임계값
    /// 밝기가 이 값보다 높고 채도가 낮으면 흰색으로 판단
    /// </summary>
    private const float BRIGHT_THRESHOLD = 0.85f;

    /// <summary>
    /// 컬러만 추출 (테두리/윤곽선/배경 제거)
    /// - 검은색, 회색, 흰색 → 흰색으로 변환
    /// - 채도 있는 컬러만 유지
    /// </summary>
    public static Texture2D ExtractColorsOnly(Texture2D source)
    {
        if (source == null) return null;

        int width = source.width;
        int height = source.height;
        var pixels = source.GetPixels();

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];

            // HSV로 변환해서 채도 확인
            Color.RGBToHSV(pixel, out float h, out float s, out float v);

            // 무채색 판단 (검정, 회색, 흰색)
            bool isGrayscale = s < SATURATION_THRESHOLD;
            bool isDark = v < DARK_THRESHOLD;
            bool isBright = v > BRIGHT_THRESHOLD && s < SATURATION_THRESHOLD;

            // 무채색이거나 너무 어둡거나 흰색이면 → 흰색으로
            if (isGrayscale || isDark || isBright)
            {
                pixels[i] = Color.white;
            }
            // 나머지는 컬러 → 유지
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply(false, false);
        result.wrapMode = TextureWrapMode.Clamp;

        return result;
    }

    /// <summary>
    /// 커스텀 임계값으로 컬러 추출
    /// </summary>
    public static Texture2D ExtractColorsCustom(
        Texture2D source,
        float saturationThreshold = SATURATION_THRESHOLD,
        float darkThreshold = DARK_THRESHOLD,
        float brightThreshold = BRIGHT_THRESHOLD)
    {
        if (source == null) return null;

        int width = source.width;
        int height = source.height;
        var pixels = source.GetPixels();

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            Color.RGBToHSV(pixel, out float h, out float s, out float v);

            bool isGrayscale = s < saturationThreshold;
            bool isDark = v < darkThreshold;
            bool isBright = v > brightThreshold && s < saturationThreshold;

            if (isGrayscale || isDark || isBright)
            {
                pixels[i] = Color.white;
            }
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels(pixels);
        result.Apply(false, false);
        result.wrapMode = TextureWrapMode.Clamp;

        return result;
    }
}
