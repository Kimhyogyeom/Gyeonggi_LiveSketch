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

    // 코너 영역 정의 - 겹침 없이 4분할 (각 45% 크기)
    // x: 좌측 0~45%, 우측 55~100%
    // y: 하단 0~45%, 상단 55~100%
    private static readonly (Rect region, QRPosition position, string name)[] Corners = new[]
    {
        (new Rect(0.55f, 0.55f, 0.45f, 0.45f), QRPosition.TopRight, "우상단"),    // 우측 상단
        (new Rect(0.0f, 0.55f, 0.45f, 0.45f), QRPosition.TopLeft, "좌상단"),      // 좌측 상단
        (new Rect(0.55f, 0.0f, 0.45f, 0.45f), QRPosition.BottomRight, "우하단"),  // 우측 하단
        (new Rect(0.0f, 0.0f, 0.45f, 0.45f), QRPosition.BottomLeft, "좌하단"),    // 좌측 하단
    };

    // 확장 검색용 더 큰 영역 (55% 크기, 약간의 겹침 허용)
    private static readonly (Rect region, QRPosition position, string name)[] LargeCorners = new[]
    {
        (new Rect(0.45f, 0.45f, 0.55f, 0.55f), QRPosition.TopRight, "우상단(확장)"),
        (new Rect(0.0f, 0.45f, 0.55f, 0.55f), QRPosition.TopLeft, "좌상단(확장)"),
        (new Rect(0.45f, 0.0f, 0.55f, 0.55f), QRPosition.BottomRight, "우하단(확장)"),
        (new Rect(0.0f, 0.0f, 0.55f, 0.55f), QRPosition.BottomLeft, "좌하단(확장)"),
    };

    /// <summary>
    /// 4개 코너에서 QR 코드를 검색하고, 발견된 위치와 텍스트 반환
    /// </summary>
    public static (string qrText, QRPosition position, Texture2D correctedImage) DetectAndCorrect(Texture2D source)
    {
        if (source == null)
            return (null, QRPosition.Unknown, null);

        Debug.Log($"[Orientation] 이미지 분석: {source.width}x{source.height}");

        // 1단계: 코너별 검색 (겹침 없는 영역)
        string foundQRText = null;
        QRPosition foundPosition = QRPosition.Unknown;

        foreach (var (region, position, name) in Corners)
        {
            string qrText = QRReader.ReadQRCodeInRegion(source, region);
            if (!string.IsNullOrEmpty(qrText))
            {
                Debug.Log($"[Orientation] ✓ {name}에서 QR 발견: '{qrText}' → {GetCorrectionName(position)}");
                foundQRText = qrText;
                foundPosition = position;
                break; // 첫 번째 발견 시 즉시 종료
            }
        }

        // 2단계: 1단계 실패 시 확장 영역으로 재검색
        if (foundQRText == null)
        {
            Debug.Log($"[Orientation] 기본 영역 검색 실패 → 확장 영역 검색");
            foreach (var (region, position, name) in LargeCorners)
            {
                string qrText = QRReader.ReadQRCodeInRegion(source, region);
                if (!string.IsNullOrEmpty(qrText))
                {
                    Debug.Log($"[Orientation] ✓ {name}에서 QR 발견: '{qrText}' → {GetCorrectionName(position)}");
                    foundQRText = qrText;
                    foundPosition = position;
                    break;
                }
            }
        }

        // 3단계: 확장 영역도 실패 시 전체 이미지 검색
        if (foundQRText == null)
        {
            Debug.Log($"[Orientation] 확장 영역 검색 실패 → 전체 이미지 검색");
            foundQRText = QRReader.ReadQRCode(source);

            if (!string.IsNullOrEmpty(foundQRText))
            {
                // 전체에서 찾았지만 위치 특정 불가 → 정상 방향 가정
                Debug.LogWarning($"[Orientation] 전체 이미지에서 QR 발견: '{foundQRText}' - 위치 특정 불가 → 정상 방향 가정");
                foundPosition = QRPosition.TopRight;
            }
        }

        // 결과 처리
        if (foundQRText == null)
        {
            Debug.LogWarning("[Orientation] QR 코드를 찾을 수 없음");
            return (null, QRPosition.Unknown, null);
        }

        // 이미지 방향 보정
        var corrected = CorrectOrientation(source, foundPosition);
        Debug.Log($"[Orientation] 최종: {foundPosition} ({GetCorrectionName(foundPosition)})");

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
