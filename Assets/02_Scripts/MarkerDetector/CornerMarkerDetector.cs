using UnityEngine;

/// <summary>
/// 스캔 이미지에서 4개 코너 마커(■)를 감지하여 스케치 영역을 계산합니다.
///
/// 핵심 설계:
/// - 용지 영역을 먼저 감지하여 배경 노이즈 제거 (Otsu 이진화)
/// - 밀도 기반 슬라이딩 윈도우로 가장 밀집된 검은 사각형을 찾음
///   (마커: ~90% 밀도 vs QR: ~50% vs 그림자: ~10%)
/// - 다단계 적응형 임계값으로 다양한 배경/인쇄 품질 대응
/// - 2단계 윈도우(coarse+fine)로 일관된 마커 위치 보장
/// - 부분 마커 복구로 1-2개 누락 시에도 추정 가능
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

    // === 다단계 임계값 설정 ===
    private struct ThresholdPass
    {
        public float blackThreshold;
        public float saturationThreshold;
        public float minDensity;
        public int minMarkerPixels;
        public string passName;
    }

    private static readonly ThresholdPass[] PASSES = new[]
    {
        new ThresholdPass { blackThreshold = 0.30f, saturationThreshold = 0.15f, minDensity = 0.20f, minMarkerPixels = 150, passName = "Strict" },
        new ThresholdPass { blackThreshold = 0.40f, saturationThreshold = 0.25f, minDensity = 0.15f, minMarkerPixels = 100, passName = "Relaxed" },
        new ThresholdPass { blackThreshold = 0.50f, saturationThreshold = 0.35f, minDensity = 0.10f, minMarkerPixels = 80,  passName = "Permissive" },
    };

    // === 이미지 가장자리 무시 (스캐너 그림자 방지) ===
    private const float EDGE_MARGIN = 0.02f;     // 가장자리 2% 무시

    // === 크롭 여백 ===
    private const float MARKER_OFFSET = 0.08f;       // 마커+테두리 안쪽 (8%)
    private const float TOP_EXTRA_OFFSET = 0.01f;    // 상단 추가 여백

    // === 사각형 검증 ===
    private const float MIN_RECT_SPAN = 0.12f;
    private const float MAX_EDGE_SKEW = 0.45f;

    // === 용지 감지 ===
    private const int PAPER_DOWNSAMPLE = 4;          // 1/4 해상도로 분석
    private const float MIN_PAPER_AREA = 0.25f;      // 최소 25% 이상이어야 유효
    private const float MAX_PAPER_AREA = 0.98f;      // 98% 이상이면 구분 불가

    /// <summary>
    /// 4개 코너 마커 감지
    ///
    /// 1) 용지 영역 자동 감지 (배경 제거)
    /// 2) 다단계 임계값으로 검은 픽셀 맵 생성
    /// 3) Integral Image 기반 슬라이딩 윈도우로 밀도 탐색
    /// 4) 부분 마커 복구 (2-3개만 발견 시)
    /// 5) 기하학적 검증
    /// </summary>
    public static DetectionResult DetectMarkers(Texture2D texture)
    {
        var result = new DetectionResult { success = false };
        if (texture == null) return result;

        int w = texture.width;
        int h = texture.height;
        var pixels = texture.GetPixels();

        // 0단계: 용지 영역 감지 (배경 제거)
        Rect paperRect = DetectPaperRegion(pixels, w, h);
        int paperX = Mathf.RoundToInt(paperRect.x * w);
        int paperY = Mathf.RoundToInt(paperRect.y * h);
        int paperW = Mathf.RoundToInt(paperRect.width * w);
        int paperH = Mathf.RoundToInt(paperRect.height * h);

        Debug.Log($"[MarkerDetector] 이미지: {w}x{h}, 용지영역: ({paperRect.x:F2},{paperRect.y:F2})-({paperRect.xMax:F2},{paperRect.yMax:F2})");

        int windowSize = Mathf.Max(20, Mathf.RoundToInt(Mathf.Min(paperW, paperH) * 0.025f));

        // 검색 영역 계산 (용지 기준)
        int marginX = paperX + Mathf.RoundToInt(paperW * EDGE_MARGIN);
        int marginY = paperY + Mathf.RoundToInt(paperH * EDGE_MARGIN);
        int midX = paperX + paperW / 2;
        int botMinY = marginY;
        int botMaxY = paperY + Mathf.RoundToInt(paperH * 0.55f);
        int topMinY = paperY + Mathf.RoundToInt(paperH * 0.35f);
        int topMaxY = paperY + Mathf.RoundToInt(paperH * 0.90f);
        int rightEdge = paperX + paperW - Mathf.RoundToInt(paperW * EDGE_MARGIN);

        // 1단계: 다단계 임계값으로 마커 탐색
        Vector2Int bl = new Vector2Int(-1, -1);
        Vector2Int br = new Vector2Int(-1, -1);
        Vector2Int tl = new Vector2Int(-1, -1);
        Vector2Int tr = new Vector2Int(-1, -1);

        foreach (var pass in PASSES)
        {
            bool[] isBlack = BuildBlackMap(pixels, w, h, pass.blackThreshold, pass.saturationThreshold, out int totalBlack);

            // Integral Image 생성 (O(1) 윈도우 합계)
            int[] integral = BuildIntegralImage(isBlack, w, h);

            Debug.Log($"[MarkerDetector] Pass '{pass.passName}': 검은픽셀={totalBlack}, 윈도우={windowSize}px");

            // 아직 못 찾은 마커만 재탐색
            if (bl.x < 0)
                bl = FindDensestCluster(isBlack, integral, w, h, marginX, botMinY, midX - marginX, botMaxY - botMinY, windowSize, pass.minDensity, pass.minMarkerPixels, "BL");
            if (br.x < 0)
                br = FindDensestCluster(isBlack, integral, w, h, midX, botMinY, rightEdge - midX, botMaxY - botMinY, windowSize, pass.minDensity, pass.minMarkerPixels, "BR");
            if (tl.x < 0)
                tl = FindDensestCluster(isBlack, integral, w, h, marginX, topMinY, midX - marginX, topMaxY - topMinY, windowSize, pass.minDensity, pass.minMarkerPixels, "TL");
            if (tr.x < 0)
                tr = FindDensestCluster(isBlack, integral, w, h, midX, topMinY, rightEdge - midX, topMaxY - topMinY, windowSize, pass.minDensity, pass.minMarkerPixels, "TR");

            // 4개 모두 찾으면 즉시 종료
            if (bl.x >= 0 && br.x >= 0 && tl.x >= 0 && tr.x >= 0)
            {
                Debug.Log($"[MarkerDetector] Pass '{pass.passName}'에서 4개 모두 발견!");
                break;
            }

            Debug.Log($"[MarkerDetector] Pass '{pass.passName}' 결과: BL={bl}, BR={br}, TL={tl}, TR={tr}");
        }

        // 2단계: 부분 마커 복구
        int foundCount = (bl.x >= 0 ? 1 : 0) + (br.x >= 0 ? 1 : 0) +
                         (tl.x >= 0 ? 1 : 0) + (tr.x >= 0 ? 1 : 0);

        if (foundCount < 4 && foundCount >= 2)
        {
            Debug.Log($"[MarkerDetector] {foundCount}/4 마커 발견 → 누락 마커 추정 시도...");
            EstimateMissingMarkers(ref bl, ref br, ref tl, ref tr, w, h);
        }

        Debug.Log($"[MarkerDetector] 최종: BL={bl}, BR={br}, TL={tl}, TR={tr}");

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

    // ================================================================
    // 용지 영역 감지 (Otsu 이진화)
    // ================================================================

    /// <summary>
    /// 밝기 기반으로 용지(백색) 영역을 감지합니다.
    /// Otsu 임계값으로 밝은 영역(=종이)과 어두운 영역(=배경)을 자동 분리.
    /// 배경이 검은 천이든, 노란 탁상이든 적응적으로 동작합니다.
    /// </summary>
    private static Rect DetectPaperRegion(Color[] pixels, int w, int h)
    {
        // 1/4 해상도 밝기맵 생성
        int dw = w / PAPER_DOWNSAMPLE;
        int dh = h / PAPER_DOWNSAMPLE;
        if (dw < 10 || dh < 10) return new Rect(0, 0, 1, 1);

        float[] brightness = new float[dw * dh];
        for (int dy = 0; dy < dh; dy++)
        {
            for (int dx = 0; dx < dw; dx++)
            {
                int sx = dx * PAPER_DOWNSAMPLE;
                int sy = dy * PAPER_DOWNSAMPLE;
                Color c = pixels[sy * w + sx];
                Color.RGBToHSV(c, out _, out _, out float v);
                brightness[dy * dw + dx] = v;
            }
        }

        // Otsu 임계값 계산
        float threshold = ComputeOtsuThreshold(brightness);
        Debug.Log($"[MarkerDetector] 용지감지: Otsu 임계값={threshold:F3}, 축소크기={dw}x{dh}");

        // 이진맵: 밝으면 용지
        bool[] isPaper = new bool[dw * dh];
        int paperCount = 0;
        for (int i = 0; i < brightness.Length; i++)
        {
            if (brightness[i] > threshold)
            {
                isPaper[i] = true;
                paperCount++;
            }
        }

        float paperRatio = (float)paperCount / (dw * dh);

        // 용지 비율 검증
        if (paperRatio < MIN_PAPER_AREA || paperRatio > MAX_PAPER_AREA)
        {
            Debug.Log($"[MarkerDetector] 용지감지: 비율 {paperRatio:F2} → 전체 이미지 사용");
            return new Rect(0, 0, 1, 1);
        }

        // 가장 큰 연결 영역의 바운딩 박스 찾기
        Rect paperBounds = FindLargestBrightRegionBounds(isPaper, dw, dh);

        // 원본 해상도로 변환 + 약간의 여유
        float margin = 0.01f;
        float rx = Mathf.Max(0f, paperBounds.x / dw - margin);
        float ry = Mathf.Max(0f, paperBounds.y / dh - margin);
        float rw = Mathf.Min(1f - rx, paperBounds.width / dw + margin * 2);
        float rh = Mathf.Min(1f - ry, paperBounds.height / dh + margin * 2);

        Debug.Log($"[MarkerDetector] 용지감지: 비율={paperRatio:F2}, 영역=({rx:F2},{ry:F2})-({rx + rw:F2},{ry + rh:F2})");
        return new Rect(rx, ry, rw, rh);
    }

    /// <summary>
    /// Otsu의 이진화 임계값 계산 (분산 최대화)
    /// </summary>
    private static float ComputeOtsuThreshold(float[] values)
    {
        const int BINS = 256;
        int[] histogram = new int[BINS];

        foreach (float v in values)
        {
            int bin = Mathf.Clamp(Mathf.RoundToInt(v * (BINS - 1)), 0, BINS - 1);
            histogram[bin]++;
        }

        int total = values.Length;
        float sumAll = 0f;
        for (int i = 0; i < BINS; i++)
            sumAll += i * histogram[i];

        float sumB = 0f;
        int wB = 0;
        float maxVariance = 0f;
        int bestThreshold = 0;

        for (int t = 0; t < BINS; t++)
        {
            wB += histogram[t];
            if (wB == 0) continue;

            int wF = total - wB;
            if (wF == 0) break;

            sumB += t * histogram[t];
            float mB = sumB / wB;
            float mF = (sumAll - sumB) / wF;

            float variance = (float)wB * wF * (mB - mF) * (mB - mF);
            if (variance > maxVariance)
            {
                maxVariance = variance;
                bestThreshold = t;
            }
        }

        return bestThreshold / (float)(BINS - 1);
    }

    /// <summary>
    /// 밝은 영역의 바운딩 박스 (가장 큰 연결 영역)
    /// 간단한 flood-fill로 연결 영역 분석
    /// </summary>
    private static Rect FindLargestBrightRegionBounds(bool[] isPaper, int dw, int dh)
    {
        // 행/열별 투영으로 바운딩 박스 직접 계산 (빠르고 충분)
        int minX = dw, maxX = 0, minY = dh, maxY = 0;
        int count = 0;

        for (int y = 0; y < dh; y++)
        {
            for (int x = 0; x < dw; x++)
            {
                if (isPaper[y * dw + x])
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    count++;
                }
            }
        }

        if (count == 0) return new Rect(0, 0, dw, dh);

        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ================================================================
    // 검은 픽셀 맵 생성 (적응형 임계값)
    // ================================================================

    /// <summary>
    /// 지정 임계값으로 검은 픽셀 맵 생성
    /// </summary>
    private static bool[] BuildBlackMap(Color[] pixels, int w, int h,
        float blackThreshold, float saturationThreshold, out int totalBlack)
    {
        bool[] isBlack = new bool[w * h];
        totalBlack = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color.RGBToHSV(pixels[i], out float pH, out float pS, out float pV);
            if (pS < saturationThreshold && pV < blackThreshold)
            {
                isBlack[i] = true;
                totalBlack++;
            }
        }

        return isBlack;
    }

    // ================================================================
    // Integral Image (Summed Area Table)
    // ================================================================

    /// <summary>
    /// Integral Image 생성 — 임의 사각형 내 합계를 O(1)로 계산
    /// </summary>
    private static int[] BuildIntegralImage(bool[] isBlack, int w, int h)
    {
        int iw = w + 1;
        int[] integral = new int[iw * (h + 1)];

        for (int y = 0; y < h; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += isBlack[y * w + x] ? 1 : 0;
                integral[(y + 1) * iw + (x + 1)] = rowSum + integral[y * iw + (x + 1)];
            }
        }

        return integral;
    }

    /// <summary>
    /// Integral Image에서 사각형 내 합계 조회 (O(1))
    /// </summary>
    private static int WindowSum(int[] integral, int iw, int x, int y, int winW, int winH)
    {
        int x1 = x, y1 = y, x2 = x + winW, y2 = y + winH;
        return integral[y2 * iw + x2]
             - integral[y1 * iw + x2]
             - integral[y2 * iw + x1]
             + integral[y1 * iw + x1];
    }

    // ================================================================
    // 사각형 검증
    // ================================================================

    /// <summary>
    /// 4점이 합리적인 직사각형을 이루는지 검증
    /// </summary>
    private static bool ValidateRectangle(Vector2 bl, Vector2 br, Vector2 tl, Vector2 tr)
    {
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

        float topY = Mathf.Min(tl.y, tr.y);
        float botY = Mathf.Max(bl.y, br.y);
        if (topY - botY < MIN_RECT_SPAN)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 상하 간격 {topY - botY:F3} < {MIN_RECT_SPAN}");
            return false;
        }

        float rightX = Mathf.Min(br.x, tr.x);
        float leftX = Mathf.Max(bl.x, tl.x);
        if (rightX - leftX < MIN_RECT_SPAN)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 좌우 간격 {rightX - leftX:F3} < {MIN_RECT_SPAN}");
            return false;
        }

        if (Mathf.Abs(tl.y - tr.y) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 상변 기울기 {Mathf.Abs(tl.y - tr.y):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        if (Mathf.Abs(bl.y - br.y) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 하변 기울기 {Mathf.Abs(bl.y - br.y):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        if (Mathf.Abs(bl.x - tl.x) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 좌변 기울기 {Mathf.Abs(bl.x - tl.x):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        if (Mathf.Abs(br.x - tr.x) > MAX_EDGE_SKEW)
        {
            Debug.LogWarning($"[MarkerDetector] 검증: 우변 기울기 {Mathf.Abs(br.x - tr.x):F3} > {MAX_EDGE_SKEW}");
            return false;
        }

        return true;
    }

    // ================================================================
    // 부분 마커 복구
    // ================================================================

    /// <summary>
    /// 2-3개 마커에서 누락된 마커를 평행사변형 법칙으로 추정
    /// </summary>
    private static void EstimateMissingMarkers(
        ref Vector2Int bl, ref Vector2Int br,
        ref Vector2Int tl, ref Vector2Int tr,
        int w, int h)
    {
        int found = (bl.x >= 0 ? 1 : 0) + (br.x >= 0 ? 1 : 0) +
                    (tl.x >= 0 ? 1 : 0) + (tr.x >= 0 ? 1 : 0);

        // 3개 발견: 평행사변형 법칙으로 4번째 추정
        if (found == 3)
        {
            if (bl.x < 0)
            {
                bl = new Vector2Int(tl.x + br.x - tr.x, tl.y + br.y - tr.y);
                Debug.Log($"[MarkerDetector] BL 추정: {bl} (TL+BR-TR)");
            }
            else if (br.x < 0)
            {
                br = new Vector2Int(tr.x + bl.x - tl.x, tr.y + bl.y - tl.y);
                Debug.Log($"[MarkerDetector] BR 추정: {br} (TR+BL-TL)");
            }
            else if (tl.x < 0)
            {
                tl = new Vector2Int(tr.x + bl.x - br.x, tr.y + bl.y - br.y);
                Debug.Log($"[MarkerDetector] TL 추정: {tl} (TR+BL-BR)");
            }
            else if (tr.x < 0)
            {
                tr = new Vector2Int(tl.x + br.x - bl.x, tl.y + br.y - bl.y);
                Debug.Log($"[MarkerDetector] TR 추정: {tr} (TL+BR-BL)");
            }
        }
        // 2개 발견: 같은 변에 있으면 워크시트 비율로 추정
        else if (found == 2)
        {
            // 하단 2개 발견 → 상단 추정
            if (bl.x >= 0 && br.x >= 0)
            {
                float spanX = Mathf.Abs(br.x - bl.x);
                float estimatedSpanY = spanX * 0.7f; // 활동지 세로/가로 비율 ~0.7
                tl = new Vector2Int(bl.x, bl.y + Mathf.RoundToInt(estimatedSpanY));
                tr = new Vector2Int(br.x, br.y + Mathf.RoundToInt(estimatedSpanY));
                Debug.Log($"[MarkerDetector] TL/TR 추정 (하단 기준): TL={tl}, TR={tr}");
            }
            // 상단 2개 발견 → 하단 추정
            else if (tl.x >= 0 && tr.x >= 0)
            {
                float spanX = Mathf.Abs(tr.x - tl.x);
                float estimatedSpanY = spanX * 0.7f;
                bl = new Vector2Int(tl.x, tl.y - Mathf.RoundToInt(estimatedSpanY));
                br = new Vector2Int(tr.x, tr.y - Mathf.RoundToInt(estimatedSpanY));
                Debug.Log($"[MarkerDetector] BL/BR 추정 (상단 기준): BL={bl}, BR={br}");
            }
            // 좌측 2개 발견 → 우측 추정
            else if (bl.x >= 0 && tl.x >= 0)
            {
                float spanY = Mathf.Abs(tl.y - bl.y);
                float estimatedSpanX = spanY / 0.7f;
                br = new Vector2Int(bl.x + Mathf.RoundToInt(estimatedSpanX), bl.y);
                tr = new Vector2Int(tl.x + Mathf.RoundToInt(estimatedSpanX), tl.y);
                Debug.Log($"[MarkerDetector] BR/TR 추정 (좌측 기준): BR={br}, TR={tr}");
            }
            // 우측 2개 발견 → 좌측 추정
            else if (br.x >= 0 && tr.x >= 0)
            {
                float spanY = Mathf.Abs(tr.y - br.y);
                float estimatedSpanX = spanY / 0.7f;
                bl = new Vector2Int(br.x - Mathf.RoundToInt(estimatedSpanX), br.y);
                tl = new Vector2Int(tr.x - Mathf.RoundToInt(estimatedSpanX), tr.y);
                Debug.Log($"[MarkerDetector] BL/TL 추정 (우측 기준): BL={bl}, TL={tl}");
            }
            // 대각선 2개 발견 → 비율로 추정
            else if (bl.x >= 0 && tr.x >= 0)
            {
                br = new Vector2Int(tr.x, bl.y);
                tl = new Vector2Int(bl.x, tr.y);
                Debug.Log($"[MarkerDetector] BR/TL 추정 (대각선 BL-TR): BR={br}, TL={tl}");
            }
            else if (br.x >= 0 && tl.x >= 0)
            {
                bl = new Vector2Int(tl.x, br.y);
                tr = new Vector2Int(br.x, tl.y);
                Debug.Log($"[MarkerDetector] BL/TR 추정 (대각선 BR-TL): BL={bl}, TR={tr}");
            }
        }

        // 추정된 좌표가 이미지 범위 내인지 클램프
        bl = ClampToImage(bl, w, h);
        br = ClampToImage(br, w, h);
        tl = ClampToImage(tl, w, h);
        tr = ClampToImage(tr, w, h);
    }

    private static Vector2Int ClampToImage(Vector2Int pos, int w, int h)
    {
        if (pos.x < 0) return pos; // 미발견 상태 유지
        return new Vector2Int(
            Mathf.Clamp(pos.x, 0, w - 1),
            Mathf.Clamp(pos.y, 0, h - 1)
        );
    }

    // ================================================================
    // 밀도 기반 클러스터 탐색 (2단계: coarse + fine)
    // ================================================================

    /// <summary>
    /// 지정 영역에서 가장 밀도 높은 검은 클러스터의 중심 좌표 반환
    ///
    /// 1) 영역 내 검은 픽셀 수 확인 (Integral Image)
    /// 2) Coarse 슬라이딩 윈도우 (step=windowSize/3)
    /// 3) Fine 슬라이딩 윈도우 (step=1, coarse 주변)
    /// 4) 최대 밀도 위치 주변 정밀 무게중심
    /// </summary>
    private static Vector2Int FindDensestCluster(
        bool[] isBlack, int[] integral, int texW, int texH,
        int startX, int startY, int searchW, int searchH,
        int windowSize, float minDensity, int minMarkerPixels, string debugName)
    {
        int endX = Mathf.Min(startX + searchW, texW);
        int endY = Mathf.Min(startY + searchH, texH);

        if (endX - startX <= windowSize || endY - startY <= windowSize)
            return new Vector2Int(-1, -1);

        int iw = texW + 1; // integral image width

        // 영역 내 검은 픽셀 수 (Integral Image O(1))
        int areaBlack = WindowSum(integral, iw, startX, startY, endX - startX, endY - startY);

        if (areaBlack < minMarkerPixels)
        {
            Debug.Log($"[MarkerDetector] {debugName} ({startX},{startY})-({endX},{endY}): 검은픽셀 {areaBlack}개 부족 (최소 {minMarkerPixels})");
            return new Vector2Int(-1, -1);
        }

        float windowArea = windowSize * windowSize;

        // === Stage 1: Coarse 탐색 ===
        float bestDensity = 0f;
        int bestWX = -1, bestWY = -1;
        int step = Mathf.Max(1, windowSize / 3);

        for (int wy = startY; wy + windowSize <= endY; wy += step)
        {
            for (int wx = startX; wx + windowSize <= endX; wx += step)
            {
                int blackCount = WindowSum(integral, iw, wx, wy, windowSize, windowSize);
                float density = blackCount / windowArea;
                if (density > bestDensity)
                {
                    bestDensity = density;
                    bestWX = wx;
                    bestWY = wy;
                }
            }
        }

        if (bestDensity < minDensity)
        {
            Debug.Log($"[MarkerDetector] {debugName}: coarse 최대밀도={bestDensity:F3} < {minDensity}");
            return new Vector2Int(-1, -1);
        }

        // === Stage 2: Fine 탐색 (coarse 주변, step=1) ===
        int fineRadius = step + 2;
        int fineStartX = Mathf.Max(startX, bestWX - fineRadius);
        int fineStartY = Mathf.Max(startY, bestWY - fineRadius);
        int fineEndX = Mathf.Min(endX, bestWX + windowSize + fineRadius);
        int fineEndY = Mathf.Min(endY, bestWY + windowSize + fineRadius);

        for (int wy = fineStartY; wy + windowSize <= fineEndY; wy++)
        {
            for (int wx = fineStartX; wx + windowSize <= fineEndX; wx++)
            {
                int blackCount = WindowSum(integral, iw, wx, wy, windowSize, windowSize);
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

        // === 무게중심 계산 ===
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

    // ================================================================
    // 텍스처 크롭 유틸리티
    // ================================================================

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
