using UnityEngine;

/// <summary>
/// 스캔 이미지에서 4개 코너 마커(■)를 감지하여 스케치 영역을 계산합니다.
///
/// 핵심 설계:
/// - 용지 위치가 매번 달라져도 감지 가능하도록 이미지 전체를 4분할 검색
/// - 밀도 기반 슬라이딩 윈도우로 가장 밀집된 검은 사각형을 찾음
///   (마커: ~90% 밀도 vs QR: ~50% vs 그림자: ~10%)
/// - 찾은 4점이 합리적인 사각형인지 기하학적 검증
/// </summary>
public static class CornerMarkerDetector
{
    public struct DetectionResult
    {
        public bool success;
        public Vector2 topLeft;
        public Vector2 topRight;
        public Vector2 bottomLeft;
        public Vector2 bottomRight;
        public Rect sketchBounds; // normalized 0~1
    }

    // === 검은색 판별 ===
    private const float BLACK_THRESHOLD = 0.3f;
    private const float SATURATION_THRESHOLD = 0.15f;

    // === 밀도 탐색 ===
    private const int MIN_MARKER_PIXELS = 150;   // 200→150 (작은 마커도 감지)
    private const float MIN_DENSITY = 0.20f;     // 0.25→0.20 (약간 흐린 마커도 감지)

    // === 이미지 가장자리 무시 (스캐너 그림자 방지) ===
    private const float EDGE_MARGIN = 0.02f;     // 가장자리 2% 무시

    // === 크롭 여백 ===
    // 마커 바깥쪽 → 테두리 라인 → 그림 영역 순서이므로 여백을 충분히 줘야 함
    private const float MARKER_OFFSET = 0.08f;       // 마커+테두리 안쪽 (8%)
    private const float TOP_EXTRA_OFFSET = 0.01f;    // 상단 추가 여백

    // === 사각형 검증 ===
    private const float MIN_RECT_SPAN = 0.12f;   // 0.15→0.12 (더 작은 그림 영역도 허용)
    private const float MAX_EDGE_SKEW = 0.45f;   // 0.35→0.45 (더 기울어진 용지도 허용)

    /// <summary>
    /// 4개 코너 마커 감지
    ///
    /// 1) 전체 이미지의 검은 픽셀 맵 생성
    /// 2) 4분할 영역에서 각각 가장 밀도 높은 클러스터 탐색
    /// 3) 찾은 4점이 유효한 사각형인지 검증
    /// </summary>
    public static DetectionResult DetectMarkers(Texture2D texture)
    {
        var result = new DetectionResult { success = false };
        if (texture == null) return result;

        int w = texture.width;
        int h = texture.height;
        var pixels = texture.GetPixels();

        // 1단계: 전체 이미지 검은 픽셀 맵 (HSV 변환 1회)
        bool[] isBlack = new bool[w * h];
        int totalBlack = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color.RGBToHSV(pixels[i], out float pH, out float pS, out float pV);
            if (pS < SATURATION_THRESHOLD && pV < BLACK_THRESHOLD)
            {
                isBlack[i] = true;
                totalBlack++;
            }
        }

        int windowSize = Mathf.Max(20, Mathf.RoundToInt(Mathf.Min(w, h) * 0.025f));

        Debug.Log($"[MarkerDetector] 이미지: {w}x{h}, 검은픽셀: {totalBlack}, 윈도우: {windowSize}px");

        // 2단계: 4분할 검색 (용지 위치에 관계없이 넓게 검색)
        //
        // 가장자리 마진 적용 (스캐너 그림자/잘림 방지)
        int marginX = Mathf.RoundToInt(w * EDGE_MARGIN);
        int marginY = Mathf.RoundToInt(h * EDGE_MARGIN);

        // 좌하(BL): x=2~50%, y=2~55%
        // 우하(BR): x=50~98%, y=2~55%
        // 좌상(TL): x=2~50%, y=35~90%
        // 우상(TR): x=50~98%, y=35~90%
        //
        // 상하 겹침(35~55%)은 의도적 — 밀도 탐색이 올바른 마커를 찾음
        int midX = w / 2;
        int botMinY = marginY;                         // 하단 시작: 2% (가장자리 무시)
        int botMaxY = Mathf.RoundToInt(h * 0.55f);
        int topMinY = Mathf.RoundToInt(h * 0.35f);
        int topMaxY = Mathf.RoundToInt(h * 0.90f);     // 92%→90% (QR 간섭 방지)

        var bl = FindDensestCluster(isBlack, w, h, marginX, botMinY, midX - marginX, botMaxY - botMinY, windowSize, "BL");
        var br = FindDensestCluster(isBlack, w, h, midX, botMinY, w - midX - marginX, botMaxY - botMinY, windowSize, "BR");
        var tl = FindDensestCluster(isBlack, w, h, marginX, topMinY, midX - marginX, topMaxY - topMinY, windowSize, "TL");
        var tr = FindDensestCluster(isBlack, w, h, midX, topMinY, w - midX - marginX, topMaxY - topMinY, windowSize, "TR");

        Debug.Log($"[MarkerDetector] 결과: BL={bl}, BR={br}, TL={tl}, TR={tr}");

        if (bl.x < 0 || br.x < 0 || tl.x < 0 || tr.x < 0)
        {
            if (bl.x < 0) Debug.LogWarning("[MarkerDetector] ✗ BL(좌하) 미발견");
            if (br.x < 0) Debug.LogWarning("[MarkerDetector] ✗ BR(우하) 미발견");
            if (tl.x < 0) Debug.LogWarning("[MarkerDetector] ✗ TL(좌상) 미발견");
            if (tr.x < 0) Debug.LogWarning("[MarkerDetector] ✗ TR(우상) 미발견");
            return result;
        }

        // 3단계: normalized 좌표로 변환
        Vector2 nBL = new Vector2(bl.x / (float)w, bl.y / (float)h);
        Vector2 nBR = new Vector2(br.x / (float)w, br.y / (float)h);
        Vector2 nTL = new Vector2(tl.x / (float)w, tl.y / (float)h);
        Vector2 nTR = new Vector2(tr.x / (float)w, tr.y / (float)h);

        // 4단계: 사각형 기하학 검증 (QR/노이즈 오인식 방지)
        if (!ValidateRectangle(nBL, nBR, nTL, nTR))
        {
            Debug.LogWarning("[MarkerDetector] 사각형 검증 실패 — QR 오인식 또는 마커 누락");
            return result;
        }

        result.success = true;
        result.bottomLeft = nBL;
        result.bottomRight = nBR;
        result.topLeft = nTL;
        result.topRight = nTR;

        float left = Mathf.Max(nBL.x, nTL.x) + MARKER_OFFSET;
        float right = Mathf.Min(nBR.x, nTR.x) - MARKER_OFFSET;
        float bottom = Mathf.Max(nBL.y, nBR.y) + MARKER_OFFSET;
        float top = Mathf.Min(nTL.y, nTR.y) - MARKER_OFFSET - TOP_EXTRA_OFFSET;

        result.sketchBounds = new Rect(left, bottom, right - left, top - bottom);
        Debug.Log($"[MarkerDetector] ✓ 스케치 영역: {result.sketchBounds}");
        return result;
    }

    /// <summary>
    /// 4점이 합리적인 직사각형을 이루는지 검증
    /// - QR코드를 마커로 오인하면 한쪽이 크게 치우침 → 탈락
    /// - 노이즈로 엉뚱한 위치를 잡으면 변이 기울어짐 → 탈락
    /// </summary>
    private static bool ValidateRectangle(Vector2 bl, Vector2 br, Vector2 tl, Vector2 tr)
    {
        // 기본 위치 검증: 마커가 이미지의 합리적인 영역에 있어야 함
        // 하단 마커: y가 5~50% 사이
        // 상단 마커: y가 40~95% 사이
        if (bl.y < 0.03f || br.y < 0.03f)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 하단 마커가 너무 아래 (BL.y={bl.y:F3}, BR.y={br.y:F3})");
            return false;
        }
        if (tl.y > 0.95f || tr.y > 0.95f)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 상단 마커가 너무 위 (TL.y={tl.y:F3}, TR.y={tr.y:F3})");
            return false;
        }

        // 상단이 하단보다 위에 있어야 함 (최소 12% 차이)
        float topY = Mathf.Min(tl.y, tr.y);
        float botY = Mathf.Max(bl.y, br.y);
        if (topY - botY < MIN_RECT_SPAN)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 상하 간격 {topY - botY:F3} < {MIN_RECT_SPAN}");
            return false;
        }

        // 우측이 좌측보다 오른쪽에 있어야 함
        float rightX = Mathf.Min(br.x, tr.x);
        float leftX = Mathf.Max(bl.x, tl.x);
        if (rightX - leftX < MIN_RECT_SPAN)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 좌우 간격 {rightX - leftX:F3} < {MIN_RECT_SPAN}");
            return false;
        }

        // 상변이 수평에 가까워야 함 (TL.y ≈ TR.y)
        if (Mathf.Abs(tl.y - tr.y) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 상변 기울기 {Mathf.Abs(tl.y - tr.y):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        // 하변이 수평에 가까워야 함
        if (Mathf.Abs(bl.y - br.y) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 하변 기울기 {Mathf.Abs(bl.y - br.y):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        // 좌변이 수직에 가까워야 함 (BL.x ≈ TL.x)
        if (Mathf.Abs(bl.x - tl.x) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 좌변 기울기 {Mathf.Abs(bl.x - tl.x):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        // 우변이 수직에 가까워야 함
        if (Mathf.Abs(br.x - tr.x) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 우변 기울기 {Mathf.Abs(br.x - tr.x):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 지정 영역에서 가장 밀도 높은 검은 클러스터의 중심 좌표 반환
    ///
    /// 1) 영역 내 검은 픽셀 수 확인
    /// 2) 슬라이딩 윈도우로 최대 밀도 위치 탐색
    /// 3) 최대 밀도 위치 주변에서 정밀 무게중심 계산
    /// </summary>
    private static Vector2Int FindDensestCluster(
        bool[] isBlack, int texW, int texH,
        int startX, int startY, int searchW, int searchH,
        int windowSize, string debugName)
    {
        int endX = Mathf.Min(startX + searchW, texW);
        int endY = Mathf.Min(startY + searchH, texH);

        if (endX - startX <= windowSize || endY - startY <= windowSize)
            return new Vector2Int(-1, -1);

        // 영역 내 검은 픽셀 수
        int areaBlack = 0;
        for (int y = startY; y < endY; y++)
            for (int x = startX; x < endX; x++)
                if (isBlack[y * texW + x])
                    areaBlack++;

        if (areaBlack < MIN_MARKER_PIXELS)
        {
            Debug.Log($"[MarkerDetector] {debugName} ({startX},{startY})-({endX},{endY}): 검은픽셀 {areaBlack}개 부족");
            return new Vector2Int(-1, -1);
        }

        // 슬라이딩 윈도우 밀도 탐색
        float bestDensity = 0f;
        int bestWX = -1, bestWY = -1;
        int step = Mathf.Max(1, windowSize / 3);
        float windowArea = windowSize * windowSize;

        for (int wy = startY; wy + windowSize <= endY; wy += step)
        {
            for (int wx = startX; wx + windowSize <= endX; wx += step)
            {
                int blackCount = 0;
                for (int dy = 0; dy < windowSize; dy++)
                {
                    int rowIdx = (wy + dy) * texW + wx;
                    for (int dx = 0; dx < windowSize; dx++)
                    {
                        if (isBlack[rowIdx + dx])
                            blackCount++;
                    }
                }

                float density = blackCount / windowArea;
                if (density > bestDensity)
                {
                    bestDensity = density;
                    bestWX = wx;
                    bestWY = wy;
                }
            }
        }

        Debug.Log($"[MarkerDetector] {debugName} ({startX},{startY})-({endX},{endY}): 검은={areaBlack}, 최대밀도={bestDensity:F2}");

        if (bestDensity < MIN_DENSITY)
            return new Vector2Int(-1, -1);

        // 최대 밀도 주변 정밀 무게중심
        int r = windowSize;
        int rMinX = Mathf.Max(startX, bestWX - r / 2);
        int rMinY = Mathf.Max(startY, bestWY - r / 2);
        int rMaxX = Mathf.Min(endX, bestWX + windowSize + r / 2);
        int rMaxY = Mathf.Min(endY, bestWY + windowSize + r / 2);

        long sumX = 0, sumY = 0;
        int count = 0;
        for (int y = rMinY; y < rMaxY; y++)
        {
            for (int x = rMinX; x < rMaxX; x++)
            {
                if (isBlack[y * texW + x])
                {
                    sumX += x;
                    sumY += y;
                    count++;
                }
            }
        }

        if (count == 0)
            return new Vector2Int(-1, -1);

        return new Vector2Int((int)(sumX / count), (int)(sumY / count));
    }

    /// <summary>
    /// 감지 결과로 텍스처 크롭
    /// </summary>
    public static Texture2D CropToSketchArea(Texture2D source, DetectionResult detection)
    {
        if (!detection.success || source == null) return null;
        return CropTexture(source, detection.sketchBounds);
    }

    /// <summary>
    /// Rect 영역으로 텍스처 크롭 (범용)
    /// </summary>
    public static Texture2D CropTexture(Texture2D source, Rect normalizedBounds)
    {
        if (source == null) return null;

        int x = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.x * source.width), 0, source.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.y * source.height), 0, source.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.width * source.width), 1, source.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.height * source.height), 1, source.height - y);

        var pixels = source.GetPixels(x, y, w, h);
        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();
        cropped.wrapMode = TextureWrapMode.Clamp;

        return cropped;
    }
}
