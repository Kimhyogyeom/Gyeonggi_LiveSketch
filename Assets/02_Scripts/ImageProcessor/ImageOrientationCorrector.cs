using UnityEngine;

/// <summary>
/// 스캔 이미지 방향 자동 보정
/// QR 코드 위치를 기준으로 이미지를 정규화합니다.
///
/// 기준: QR 코드가 우측 상단에 있는 것이 정상 방향
/// </summary>
public static class ImageOrientationCorrector
{
    /// <summary>
    /// QR 코드 발견 위치
    /// </summary>
    public enum QRPosition
    {
        Unknown,
        TopRight,    // 정상 (보정 불필요)
        TopLeft,     // 좌우 반전 필요
        BottomRight, // 상하 반전 필요
        BottomLeft   // 180도 회전 필요
    }

    // 코너 영역 정의 (QR이 코너 근처에 있으므로 40% 영역으로 확대)
    private static readonly (Rect region, QRPosition position, string name)[] Corners = new[]
    {
        (new Rect(0.60f, 0.60f, 0.40f, 0.40f), QRPosition.TopRight, "우상단"),
        (new Rect(0.0f, 0.60f, 0.40f, 0.40f), QRPosition.TopLeft, "좌상단"),
        (new Rect(0.60f, 0.0f, 0.40f, 0.40f), QRPosition.BottomRight, "우하단"),
        (new Rect(0.0f, 0.0f, 0.40f, 0.40f), QRPosition.BottomLeft, "좌하단"),
    };

    /// <summary>
    /// 4개 코너에서 QR 코드를 검색하고, 발견된 위치와 텍스트 반환
    /// </summary>
    public static (string qrText, QRPosition position, Texture2D correctedImage) DetectAndCorrect(Texture2D source)
    {
        if (source == null)
            return (null, QRPosition.Unknown, null);

        Debug.Log($"[ImageOrientationCorrector] 이미지 분석 시작: {source.width}x{source.height}");

        // 1. 모든 코너에서 QR 검색 (어느 코너에서 발견되는지 정확히 파악)
        string foundQRText = null;
        QRPosition foundPosition = QRPosition.Unknown;
        int foundCount = 0;

        foreach (var (region, position, name) in Corners)
        {
            string qrText = QRReader.ReadQRCodeInRegion(source, region);
            if (!string.IsNullOrEmpty(qrText))
            {
                Debug.Log($"[ImageOrientationCorrector] ✓ {name}에서 QR 발견: '{qrText}'");

                // 첫 번째 발견된 것 저장
                if (foundQRText == null)
                {
                    foundQRText = qrText;
                    foundPosition = position;
                }
                foundCount++;
            }
            else
            {
                Debug.Log($"[ImageOrientationCorrector] ✗ {name}: QR 없음");
            }
        }

        // 2. 결과 처리
        if (foundCount == 0)
        {
            // 코너에서 못 찾으면 전체 이미지에서 시도
            Debug.Log("[ImageOrientationCorrector] 코너에서 QR 없음, 전체 이미지 검색 시도...");
            string fullQR = QRReader.ReadQRCode(source);

            if (!string.IsNullOrEmpty(fullQR))
            {
                Debug.LogWarning($"[ImageOrientationCorrector] 전체에서 QR 발견: '{fullQR}' - 위치 특정 불가, 정상 방향 가정");
                return (fullQR, QRPosition.TopRight, DuplicateTexture(source));
            }

            Debug.LogWarning("[ImageOrientationCorrector] QR 코드를 찾을 수 없음");
            return (null, QRPosition.Unknown, null);
        }

        if (foundCount > 1)
        {
            Debug.LogWarning($"[ImageOrientationCorrector] 경고: QR이 {foundCount}개 코너에서 발견됨! 첫 번째 사용: {foundPosition}");
        }

        // 3. 이미지 방향 보정
        Debug.Log($"[ImageOrientationCorrector] 최종 결정: {foundPosition} → {GetCorrectionName(foundPosition)}");
        var corrected = CorrectOrientation(source, foundPosition);

        return (foundQRText, foundPosition, corrected);
    }

    private static string GetCorrectionName(QRPosition position)
    {
        return position switch
        {
            QRPosition.TopRight => "보정 없음 (정상)",
            QRPosition.TopLeft => "좌우 반전",
            QRPosition.BottomRight => "상하 반전",
            QRPosition.BottomLeft => "180도 회전",
            _ => "알 수 없음"
        };
    }

    /// <summary>
    /// QR 위치에 따라 이미지 방향 보정
    /// </summary>
    public static Texture2D CorrectOrientation(Texture2D source, QRPosition qrPosition)
    {
        switch (qrPosition)
        {
            case QRPosition.TopRight:
                // 정상 - 복사만
                return DuplicateTexture(source);

            case QRPosition.TopLeft:
                // QR이 좌상단 → 좌우 반전 필요
                return FlipHorizontal(source);

            case QRPosition.BottomRight:
                // QR이 우하단 → 상하 반전 필요
                return FlipVertical(source);

            case QRPosition.BottomLeft:
                // QR이 좌하단 → 180도 회전 필요
                return Rotate180(source);

            default:
                return DuplicateTexture(source);
        }
    }

    /// <summary>
    /// 좌우 반전
    /// </summary>
    public static Texture2D FlipHorizontal(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        var original = source.GetPixels32();
        var flipped = new Color32[original.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIdx = y * width + x;
                int dstIdx = y * width + (width - 1 - x);
                flipped[dstIdx] = original[srcIdx];
            }
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(flipped);
        result.Apply();
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    /// <summary>
    /// 상하 반전
    /// </summary>
    public static Texture2D FlipVertical(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        var original = source.GetPixels32();
        var flipped = new Color32[original.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIdx = y * width + x;
                int dstIdx = (height - 1 - y) * width + x;
                flipped[dstIdx] = original[srcIdx];
            }
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(flipped);
        result.Apply();
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    /// <summary>
    /// 180도 회전
    /// </summary>
    public static Texture2D Rotate180(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        var original = source.GetPixels32();
        var rotated = new Color32[original.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIdx = y * width + x;
                int dstIdx = (height - 1 - y) * width + (width - 1 - x);
                rotated[dstIdx] = original[srcIdx];
            }
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(rotated);
        result.Apply();
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    private static Texture2D DuplicateTexture(Texture2D source)
    {
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.SetPixels32(source.GetPixels32());
        copy.Apply();
        copy.wrapMode = TextureWrapMode.Clamp;
        return copy;
    }
}
