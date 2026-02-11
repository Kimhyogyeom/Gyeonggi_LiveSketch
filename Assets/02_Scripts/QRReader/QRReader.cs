using UnityEngine;

/// <summary>
/// 스캔 이미지에서 QR 코드를 인식하고 텍스트 추출.
/// ZXing 라이브러리 사용.
/// </summary>
public static class QRReader
{
    // QR 인식에 적합한 최대 크기
    private const int MAX_QR_SCAN_SIZE = 2000;

    /// <summary>
    /// 텍스처에서 QR 코드 텍스트 추출
    /// </summary>
    public static string ReadQRCode(Texture2D texture)
    {
        if (texture == null) return null;

#if ZXING_AVAILABLE
        // 이미지가 너무 크면 축소해서 시도
        Texture2D scanTexture = texture;
        bool needsDestroy = false;

        if (texture.width > MAX_QR_SCAN_SIZE || texture.height > MAX_QR_SCAN_SIZE)
        {
            float scale = Mathf.Min((float)MAX_QR_SCAN_SIZE / texture.width, (float)MAX_QR_SCAN_SIZE / texture.height);
            int newWidth = Mathf.RoundToInt(texture.width * scale);
            int newHeight = Mathf.RoundToInt(texture.height * scale);

            Debug.Log($"[QRReader] 이미지 축소: {texture.width}x{texture.height} → {newWidth}x{newHeight}");
            scanTexture = ResizeTexture(texture, newWidth, newHeight);
            needsDestroy = true;
        }

        try
        {
            string result = DecodeQR(scanTexture);
            if (result != null)
            {
                Debug.Log($"[QRReader] QR 인식 성공: {result}");
                return result;
            }
        }
        finally
        {
            if (needsDestroy && scanTexture != null)
                Object.Destroy(scanTexture);
        }
#else
        Debug.LogWarning("[QRReader] ZXing 라이브러리 없음");
#endif

        return null;
    }

#if ZXING_AVAILABLE
    private static string DecodeQR(Texture2D texture)
    {
        try
        {
            var pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;

            // ZXing용 RGB 배열 변환 (Y축 뒤집기)
            byte[] rgbBytes = new byte[pixels.Length * 3];
            for (int i = 0; i < pixels.Length; i++)
            {
                int y = i / width;
                int x = i % width;
                int flippedY = height - 1 - y;
                int srcIdx = flippedY * width + x;

                rgbBytes[i * 3] = pixels[srcIdx].r;
                rgbBytes[i * 3 + 1] = pixels[srcIdx].g;
                rgbBytes[i * 3 + 2] = pixels[srcIdx].b;
            }

            var luminanceSource = new ZXing.RGBLuminanceSource(rgbBytes, width, height);
            var binarizer = new ZXing.Common.HybridBinarizer(luminanceSource);
            var binaryBitmap = new ZXing.BinaryBitmap(binarizer);

            // TryHarder 힌트로 인식률 향상
            var hints = new System.Collections.Generic.Dictionary<ZXing.DecodeHintType, object>
            {
                { ZXing.DecodeHintType.TRY_HARDER, true },
                { ZXing.DecodeHintType.POSSIBLE_FORMATS, new System.Collections.Generic.List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE } }
            };

            var reader = new ZXing.MultiFormatReader();
            var result = reader.decode(binaryBitmap, hints);

            return result?.Text;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[QRReader] QR 디코딩 실패: {e.Message}");
            return null;
        }
    }

    private static Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
#endif

    /// <summary>
    /// 특정 영역에서만 QR 코드 인식.
    /// 첫 시도 실패 시 대비 향상 후 재시도.
    /// </summary>
    public static string ReadQRCodeInRegion(Texture2D texture, Rect normalizedRegion)
    {
        if (texture == null) return null;

        int x = Mathf.RoundToInt(normalizedRegion.x * texture.width);
        int y = Mathf.RoundToInt(normalizedRegion.y * texture.height);
        int w = Mathf.RoundToInt(normalizedRegion.width * texture.width);
        int h = Mathf.RoundToInt(normalizedRegion.height * texture.height);

        x = Mathf.Clamp(x, 0, texture.width - 1);
        y = Mathf.Clamp(y, 0, texture.height - 1);
        w = Mathf.Clamp(w, 1, texture.width - x);
        h = Mathf.Clamp(h, 1, texture.height - y);

        var pixels = texture.GetPixels(x, y, w, h);
        var regionTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        regionTex.SetPixels(pixels);
        regionTex.Apply();

        // 1차 시도: 원본
        var result = ReadQRCode(regionTex);

        // 2차 시도: 대비 향상 후 재시도
        if (result == null)
        {
            var enhanced = EnhanceContrast(regionTex);
            result = ReadQRCode(enhanced);
            Object.Destroy(enhanced);

            if (result != null)
                Debug.Log($"[QRReader] 대비 향상 후 QR 인식 성공: {result}");
        }

        Object.Destroy(regionTex);
        return result;
    }

    /// <summary>
    /// 히스토그램 스트레칭으로 대비 향상 (QR 인식률 개선)
    /// </summary>
    private static Texture2D EnhanceContrast(Texture2D source)
    {
        if (source == null) return null;

        var pixels = source.GetPixels32();
        int total = pixels.Length;

        // 밝기 히스토그램 생성
        int[] histogram = new int[256];
        byte[] luminance = new byte[total];

        for (int i = 0; i < total; i++)
        {
            byte lum = (byte)(0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b);
            luminance[i] = lum;
            histogram[lum]++;
        }

        // 5th / 95th percentile 찾기
        int low = 0, high = 255;
        int cumulative = 0;
        int threshold5 = total * 5 / 100;
        int threshold95 = total * 95 / 100;

        for (int i = 0; i < 256; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= threshold5 && low == 0) low = i;
            if (cumulative >= threshold95) { high = i; break; }
        }

        float range = Mathf.Max(1f, high - low);

        // 대비 스트레칭 (그레이스케일)
        var result = new Color32[total];
        for (int i = 0; i < total; i++)
        {
            byte val = (byte)Mathf.Clamp(((luminance[i] - low) / range) * 255f, 0f, 255f);
            result[i] = new Color32(val, val, val, 255);
        }

        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.SetPixels32(result);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// ZXing 사용 가능 여부
    /// </summary>
    public static bool IsAvailable()
    {
#if ZXING_AVAILABLE
        return true;
#else
        return false;
#endif
    }
}
