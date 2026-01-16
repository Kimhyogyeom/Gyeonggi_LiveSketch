using UnityEngine;

/// <summary>
/// 스캔 파이프라인 메인 컨트롤러
///
/// [파이프라인 흐름]
/// 1. 스캔 이미지 수신 (ScanFolderWatcher)
/// 2. QR 코드 인식 → 이미지 방향 보정 (ImageOrientationCorrector)
/// 3. QR 텍스트로 해당 동물 모델 스폰 (AnimalModelManager)
/// 4. 마커 기준 스케치 영역 크롭 (CornerMarkerDetector)
/// 5. 테두리 제거 + 컬러 추출 (ColorExtractor)
/// 6. 3D 모델에 텍스처 적용 (SideMirrorBlend 쉐이더)
/// </summary>
public class ScanProcessor : MonoBehaviour
{
    [Header("=== 필수 연결 ===")]
    [Tooltip("스캔 폴더 감시 컴포넌트")]
    [SerializeField] private ScanFolderWatcher watcher;

    [Tooltip("동물 모델 관리 컴포넌트")]
    [SerializeField] private AnimalModelManager modelManager;

    [Tooltip("텍스처 매핑 쉐이더")]
    [SerializeField] private Shader sideMirrorShader;

    [Header("=== UV 매핑 설정 ===")]
    [Tooltip("텍스처 좌우 위치 조정")]
    [Range(-1f, 1f)]
    [SerializeField] private float uvOffsetX = 0.06f;

    [Tooltip("텍스처 상하 위치 조정")]
    [Range(-1f, 1f)]
    [SerializeField] private float uvOffsetY = -0.019f;

    [Tooltip("텍스처 좌우 크기 (작을수록 넓게 펼쳐짐)")]
    [Range(0.1f, 2f)]
    [SerializeField] private float uvScaleX = 0.52f;

    [Tooltip("텍스처 상하 크기")]
    [Range(0.1f, 2f)]
    [SerializeField] private float uvScaleY = 1.02f;

    [Header("=== 이미지 크롭 설정 ===")]
    [Tooltip("코너 마커 자동 감지 사용 (비활성화시 고정 영역 사용)")]
    [SerializeField] private bool useAutoMarkerDetection = true;

    [Tooltip("고정 크롭 시작점 (normalized 0~1)")]
    [SerializeField] private Vector2 cropMin = new Vector2(0.05f, 0.1f);

    [Tooltip("고정 크롭 끝점 (normalized 0~1)")]
    [SerializeField] private Vector2 cropMax = new Vector2(0.75f, 0.9f);

    [Header("=== 컬러 추출 설정 ===")]
    [Tooltip("테두리/윤곽선 제거 + 컬러만 추출")]
    [SerializeField] private bool extractColorOnly = true;

    [Header("=== 빈 영역 채우기 설정 ===")]
    [Tooltip("빈 영역 채우기 모드\n0 = 회색 채우기\n1 = 윤곽선 모드 (흰색 테두리)\n2 = 주요 색상으로 채우기")]
    [Range(0, 2)]
    [SerializeField] private int fillMode = 0;

    [Tooltip("빈 영역 채우기 색상 (회색 권장)")]
    [SerializeField] private Color fillColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Tooltip("윤곽선 색상 (모드 1에서 사용)")]
    [SerializeField] private Color outlineColor = Color.white;

    [Tooltip("윤곽선 두께 (모드 1에서 사용)")]
    [Range(0.001f, 0.05f)]
    [SerializeField] private float outlineThickness = 0.01f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    private void OnEnable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady += HandleScan;
    }

    private void OnDisable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady -= HandleScan;
    }

    /// <summary>
    /// 스캔 이미지 처리 메인 파이프라인
    /// </summary>
    private void HandleScan(Texture2D scanTexture, string filePath)
    {
        if (scanTexture == null) return;

        Log($"스캔 이미지 수신: {filePath} ({scanTexture.width}x{scanTexture.height})");

        // 1. QR 코드 인식 + 이미지 방향 보정
        var (qrText, qrPosition, correctedImage) = ImageOrientationCorrector.DetectAndCorrect(scanTexture);

        if (string.IsNullOrEmpty(qrText) || correctedImage == null)
        {
            Log("QR 인식 실패 - 스캔 무시");
            return;
        }

        Log($"QR 인식 성공: {qrText} (위치: {qrPosition})");

        // 2. 해당 동물 모델 스폰
        modelManager?.SpawnModelByQR(qrText);

        // 3. 스케치 영역 크롭
        Texture2D cropped = CropSketchArea(correctedImage);
        if (cropped == null)
        {
            Log("크롭 실패 → 보정 이미지 그대로 사용");
            cropped = correctedImage;
        }
        else
        {
            Object.Destroy(correctedImage);
        }

        // 4. 컬러 추출 (테두리 제거)
        Texture2D finalTexture = cropped;
        if (extractColorOnly)
        {
            finalTexture = ColorExtractor.ExtractColorsWithBorderMask(cropped, true);
            Log("컬러 추출 완료");

            if (finalTexture != cropped)
                Object.Destroy(cropped);
        }

        // 5. 모델에 텍스처 적용
        ApplyTextureToModel(finalTexture);
    }

    /// <summary>
    /// 마커 기반 스케치 영역 크롭
    /// </summary>
    private Texture2D CropSketchArea(Texture2D tex)
    {
        Rect cropBounds;

        if (useAutoMarkerDetection)
        {
            var detection = CornerMarkerDetector.DetectMarkers(tex);

            if (detection.success)
            {
                Log($"마커 감지 성공: {detection.sketchBounds}");
                cropBounds = detection.sketchBounds;
            }
            else
            {
                Log("마커 감지 실패 → 고정 영역 사용");
                cropBounds = GetFixedCropBounds();
            }
        }
        else
        {
            cropBounds = GetFixedCropBounds();
        }

        return CropTexture(tex, cropBounds);
    }

    private Rect GetFixedCropBounds()
    {
        return new Rect(cropMin.x, cropMin.y, cropMax.x - cropMin.x, cropMax.y - cropMin.y);
    }

    private Texture2D CropTexture(Texture2D source, Rect normalizedBounds)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.x * source.width), 0, source.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.y * source.height), 0, source.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.width * source.width), 1, source.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.height * source.height), 1, source.height - y);

        var pixels = source.GetPixels(x, y, w, h);
        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply(false, false);
        cropped.wrapMode = TextureWrapMode.Clamp;

        return cropped;
    }

    /// <summary>
    /// 현재 모델에 텍스처 적용
    /// </summary>
    private void ApplyTextureToModel(Texture2D texture)
    {
        Renderer renderer = modelManager?.GetCurrentRenderer();

        if (renderer == null)
        {
            Log("텍스처 적용할 모델 없음");
            Object.Destroy(texture);
            return;
        }

        // GPU 텍스처 준비
        Texture2D gpuTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
        gpuTexture.SetPixels32(texture.GetPixels32());
        gpuTexture.Apply(true, false);
        gpuTexture.filterMode = FilterMode.Bilinear;
        gpuTexture.wrapMode = TextureWrapMode.Clamp;

        Object.Destroy(texture);

        // 쉐이더 확인
        Shader shader = sideMirrorShader != null ? sideMirrorShader : Shader.Find("LiveSketch/SideMirrorBlend");
        if (shader == null)
        {
            Log("ERROR: SideMirrorBlend 쉐이더를 찾을 수 없음!");
            Object.Destroy(gpuTexture);
            return;
        }

        // 머티리얼 생성 및 설정
        Material mat = new Material(shader);
        mat.mainTexture = gpuTexture;
        mat.SetTexture("_MainTex", gpuTexture);
        mat.SetFloat("_HasTexture", 1f);
        mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f, 1f));

        // UV 설정 적용 (AnimalEntry 설정 우선, 없으면 기본값)
        var entry = modelManager?.GetCurrentEntry();
        if (entry != null)
        {
            mat.SetFloat("_OffsetX", entry.uvOffsetX);
            mat.SetFloat("_OffsetY", entry.uvOffsetY);
            mat.SetFloat("_ScaleX", entry.uvScaleX);
            mat.SetFloat("_ScaleY", entry.uvScaleY);
            Log($"AnimalEntry UV 설정 적용: {entry.qrText}");
        }
        else
        {
            mat.SetFloat("_OffsetX", uvOffsetX);
            mat.SetFloat("_OffsetY", uvOffsetY);
            mat.SetFloat("_ScaleX", uvScaleX);
            mat.SetFloat("_ScaleY", uvScaleY);
        }

        // 텍스처에서 주요 색상 자동 추출
        Color dominantColor = ExtractDominantColor(gpuTexture);
        mat.SetColor("_OutOfBoundsColor", dominantColor);
        mat.SetColor("_BaseColor", dominantColor);
        Log($"자동 추출 색상: R={dominantColor.r:F2} G={dominantColor.g:F2} B={dominantColor.b:F2}");

        // 빈 영역 채우기 설정
        mat.SetFloat("_FillMode", fillMode);
        mat.SetColor("_FillColor", fillColor);
        mat.SetColor("_OutlineColor", outlineColor);
        mat.SetFloat("_OutlineThickness", outlineThickness);
        mat.SetFloat("_AlphaThreshold", 0.1f);
        Log($"빈 영역 채우기 모드: {fillMode} ({(fillMode == 0 ? "회색" : fillMode == 1 ? "윤곽선" : "주요색상")})");

        // 머티리얼 적용
        renderer.material = mat;

        Log($"텍스처 적용 완료: {renderer.name} ({gpuTexture.width}x{gpuTexture.height})");
    }

    /// <summary>
    /// 텍스처에서 주요 색상 추출 (흰색/투명 제외)
    /// </summary>
    private Color ExtractDominantColor(Texture2D texture)
    {
        var pixels = texture.GetPixels32();
        float totalR = 0, totalG = 0, totalB = 0;
        int count = 0;

        // 샘플링 (전체 픽셀 중 일부만)
        int step = Mathf.Max(1, pixels.Length / 5000);

        for (int i = 0; i < pixels.Length; i += step)
        {
            var p = pixels[i];

            // 투명 픽셀 제외
            if (p.a < 128) continue;

            // 흰색/밝은 색 제외 (배경)
            float brightness = (p.r + p.g + p.b) / 3f / 255f;
            if (brightness > 0.85f) continue;

            // 너무 어두운 색도 제외 (윤곽선)
            if (brightness < 0.1f) continue;

            totalR += p.r;
            totalG += p.g;
            totalB += p.b;
            count++;
        }

        if (count == 0)
            return new Color(0.2f, 0.2f, 0.2f, 1f); // 기본값

        return new Color(
            totalR / count / 255f,
            totalG / count / 255f,
            totalB / count / 255f,
            1f
        );
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[ScanProcessor] {message}");
    }
}
