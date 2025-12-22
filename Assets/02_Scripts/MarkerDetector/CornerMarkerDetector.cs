using UnityEngine;

/// <summary>
/// 스캔 이미지에서 4개 모서리의 검은색 마커를 감지하고
/// 스케치 영역(마커 안쪽)을 계산합니다.
/// </summary>
public static class CornerMarkerDetector
{
    /// <summary>
    /// 마커 감지 결과
    /// </summary>
    public struct DetectionResult
    {
        public bool success;
        public Vector2 topLeft;
        public Vector2 topRight;
        public Vector2 bottomLeft;
        public Vector2 bottomRight;
        public Rect sketchBounds; // normalized 0~1
    }

    // 설정값
    private const float BLACK_THRESHOLD = 0.3f;      // 검은색 판단 임계값
    private const float SEARCH_RATIO = 0.25f;        // 각 모서리 탐색 영역 비율
    private const int MIN_MARKER_SIZE = 20;          // 최소 마커 크기 (px)
    private const float MARKER_OFFSET = 0.02f;       // 마커 안쪽 여백 (2%)

    /// <summary>
    /// 4개 모서리 마커 감지
    /// </summary>
    public static DetectionResult DetectMarkers(Texture2D texture)
    {
        var result = new DetectionResult { success = false };

        if (texture == null) return result;

        int w = texture.width;
        int h = texture.height;
        var pixels = texture.GetPixels();

        int searchW = Mathf.RoundToInt(w * SEARCH_RATIO);
        int searchH = Mathf.RoundToInt(h * SEARCH_RATIO);

        // 4개 모서리에서 마커 중심 찾기
        var bl = FindMarkerCenter(pixels, w, h, 0, 0, searchW, searchH);
        var br = FindMarkerCenter(pixels, w, h, w - searchW, 0, searchW, searchH);
        var tl = FindMarkerCenter(pixels, w, h, 0, h - searchH, searchW, searchH);
        var tr = FindMarkerCenter(pixels, w, h, w - searchW, h - searchH, searchW, searchH);

        // 모든 마커 감지 확인
        if (bl.x < 0 || br.x < 0 || tl.x < 0 || tr.x < 0)
        {
            Debug.LogWarning("[MarkerDetector] 마커 감지 실패");
            return result;
        }

        result.success = true;
        result.bottomLeft = new Vector2(bl.x / (float)w, bl.y / (float)h);
        result.bottomRight = new Vector2(br.x / (float)w, br.y / (float)h);
        result.topLeft = new Vector2(tl.x / (float)w, tl.y / (float)h);
        result.topRight = new Vector2(tr.x / (float)w, tr.y / (float)h);

        // 스케치 영역 계산 (마커 안쪽)
        float left = Mathf.Max(result.bottomLeft.x, result.topLeft.x) + MARKER_OFFSET;
        float right = Mathf.Min(result.bottomRight.x, result.topRight.x) - MARKER_OFFSET;
        float bottom = Mathf.Max(result.bottomLeft.y, result.bottomRight.y) + MARKER_OFFSET;
        float top = Mathf.Min(result.topLeft.y, result.topRight.y) - MARKER_OFFSET;

        result.sketchBounds = new Rect(left, bottom, right - left, top - bottom);

        Debug.Log($"[MarkerDetector] 스케치 영역: {result.sketchBounds}");
        return result;
    }

    /// <summary>
    /// 지정 영역에서 검은색 마커 중심 찾기
    /// </summary>
    private static Vector2Int FindMarkerCenter(
        Color[] pixels, int texW, int texH,
        int startX, int startY, int searchW, int searchH)
    {
        int blackCount = 0;
        long sumX = 0, sumY = 0;

        for (int y = startY; y < startY + searchH && y < texH; y++)
        {
            for (int x = startX; x < startX + searchW && x < texW; x++)
            {
                var pixel = pixels[y * texW + x];
                float brightness = (pixel.r + pixel.g + pixel.b) / 3f;

                if (brightness < BLACK_THRESHOLD)
                {
                    blackCount++;
                    sumX += x;
                    sumY += y;
                }
            }
        }

        // 충분한 검은색 픽셀 필요
        if (blackCount < MIN_MARKER_SIZE * MIN_MARKER_SIZE)
            return new Vector2Int(-1, -1);

        return new Vector2Int(
            (int)(sumX / blackCount),
            (int)(sumY / blackCount)
        );
    }

    /// <summary>
    /// 감지 결과로 텍스처 크롭
    /// </summary>
    public static Texture2D CropToSketchArea(Texture2D source, DetectionResult detection)
    {
        if (!detection.success || source == null) return null;

        var bounds = detection.sketchBounds;

        int x = Mathf.Clamp(Mathf.RoundToInt(bounds.x * source.width), 0, source.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(bounds.y * source.height), 0, source.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(bounds.width * source.width), 1, source.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(bounds.height * source.height), 1, source.height - y);

        var pixels = source.GetPixels(x, y, w, h);
        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();
        cropped.wrapMode = TextureWrapMode.Clamp;

        return cropped;
    }
}
