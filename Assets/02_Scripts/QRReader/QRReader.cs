using UnityEngine;

/// <summary>
/// 스캔 이미지에서 QR 코드를 인식하고 텍스트 추출.
/// ZXing 라이브러리 사용.
/// </summary>
public static class QRReader
{
    /// <summary>
    /// 텍스처에서 QR 코드 텍스트 추출
    /// </summary>
    public static string ReadQRCode(Texture2D texture)
    {
        if (texture == null) return null;

#if ZXING_AVAILABLE
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

            var reader = new ZXing.QrCode.QRCodeReader();
            var result = reader.decode(binaryBitmap);

            if (result != null)
            {
                Debug.Log($"[QRReader] QR 인식: {result.Text}");
                return result.Text;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[QRReader] QR 인식 실패: {e.Message}");
        }
#else
        Debug.LogWarning("[QRReader] ZXing 라이브러리 없음");
#endif

        return null;
    }

    /// <summary>
    /// 특정 영역에서만 QR 코드 인식
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

        var result = ReadQRCode(regionTex);
        Object.Destroy(regionTex);

        return result;
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
