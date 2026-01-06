using UnityEngine;

/// <summary>
/// 스캔 파이프라인 메인 컨트롤러
/// A4 가로 방향 스캔 기준 (QR코드: 우상단, 마커: 스케치 영역 네 모서리)
///
/// 처리 흐름:
/// 1. 스캔 이미지 수신
/// 2. QR 코드 위치 감지 → 이미지 방향 자동 보정
/// 3. QR 텍스트로 해당 모델 스폰
/// 4. 마커 영역 기준 스케치 영역 크롭
/// 5. 테두리 제거 → 컬러만 추출
/// 6. 새로 스폰된 모델에만 텍스처 적용 (기존 모델 유지)
/// </summary>
public class ScanProcessor : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private ScanFolderWatcher watcher;
    [SerializeField] private AnimalModelManager modelManager;

    [Header("Triplanar 쉐이더")]
    [Tooltip("Triplanar 투영 쉐이더 (LiveSketch/SimpleTriplanar)")]
    [SerializeField] private Shader triplanarShader;


    [Header("크롭 설정 (A4 가로 기준)")]
    [Tooltip("4개 모서리 마커 자동 감지 사용")]
    [SerializeField] private bool useAutoMarkerDetection = true;

    [Tooltip("자동 감지 실패 시 고정 크롭 영역 (normalized 0~1)")]
    [SerializeField] private Vector2 cropMin = new Vector2(0.05f, 0.1f);
    [SerializeField] private Vector2 cropMax = new Vector2(0.75f, 0.9f);

    [Header("컬러 추출 설정")]
    [Tooltip("테두리/윤곽선 제거하고 컬러만 추출")]
    [SerializeField] private bool extractColorOnly = true;

    [Tooltip("테두리 밖으로 벗어난 색칠 무시 (Flood Fill 마스킹)")]
    [SerializeField] private bool removeOutsideColors = true;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = true;
    [Tooltip("처리된 텍스처를 파일로 저장 (디버그용)")]
    [SerializeField] private bool saveDebugTexture = true;

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

        // 1. QR 코드 위치 감지 + 이미지 방향 자동 보정
        var (qrText, qrPosition, correctedImage) = ImageOrientationCorrector.DetectAndCorrect(scanTexture);

        if (string.IsNullOrEmpty(qrText) || correctedImage == null)
        {
            Log("QR 인식 실패 - 모델 스폰 안함");
            return;
        }

        Log($"QR 인식: {qrText} (위치: {qrPosition})");

        // 2. 모델 스폰
        modelManager?.SpawnModelByQR(qrText);

        // 3. 스케치 영역 크롭 (보정된 이미지에서 마커 기준)
        Texture2D cropped = CropSketchArea(correctedImage);
        if (cropped == null)
        {
            Log("크롭 실패, 보정된 이미지 사용");
            cropped = correctedImage;
        }
        else
        {
            // 크롭 성공하면 보정 이미지는 삭제
            Object.Destroy(correctedImage);
        }

        // 4. 컬러만 추출 (테두리/윤곽선 제거 + 테두리 밖 색칠 무시)
        Texture2D colorOnly = cropped;
        if (extractColorOnly)
        {
            if (removeOutsideColors)
            {
                // 테두리 밖 색칠도 제거하는 새 메서드 사용
                colorOnly = ColorExtractor.ExtractColorsWithBorderMask(cropped, true);
                Log("컬러 추출 완료 (테두리/윤곽선 제거 + 테두리 밖 색칠 무시)");
            }
            else
            {
                // 기존 방식 (테두리만 제거)
                colorOnly = ColorExtractor.ExtractColorsOnly(cropped);
                Log("컬러 추출 완료 (테두리/윤곽선 제거됨)");
            }

            if (colorOnly != cropped)
                Object.Destroy(cropped);
        }

        // 5. 디버그: 텍스처 저장
        if (saveDebugTexture)
        {
            SaveDebugTexture(colorOnly, "processed_texture.png");
        }

        // 6. 새로 스폰된 모델에 텍스처 적용 (텍스처는 모델이 소유)
        ApplyToCurrentModel(colorOnly);
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

    /// <summary>
    /// 텍스처 크롭
    /// </summary>
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
    /// 현재(방금 스폰된) 모델에 텍스처 적용
    /// </summary>
    private void ApplyToCurrentModel(Texture2D texture)
    {
        Renderer renderer = modelManager?.GetCurrentRenderer();

        if (renderer == null)
        {
            Log("텍스처 적용할 모델 없음");
            Object.Destroy(texture);
            return;
        }

        // ★ 텍스처 그대로 GPU에 업로드 (좌우반전 제거)
        Texture2D gpuTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
        gpuTexture.SetPixels32(texture.GetPixels32());
        gpuTexture.Apply(true, false);
        gpuTexture.filterMode = FilterMode.Bilinear;
        gpuTexture.wrapMode = TextureWrapMode.Clamp;

        // 원본 텍스처 삭제
        Object.Destroy(texture);

        // 커스텀 Triplanar 쉐이더 사용
        Shader shader = triplanarShader != null ? triplanarShader : Shader.Find("LiveSketch/SimpleTriplanar");
        if (shader == null)
        {
            Log("ERROR: SimpleTriplanar 쉐이더를 찾을 수 없음!");
            Object.Destroy(gpuTexture);
            return;
        }

        // ★ 새 머티리얼 생성
        Material mat = new Material(shader);

        // ★ sharedMesh.bounds 사용 (스케일 적용 전 원본 메시 바운드)
        // 쉐이더의 v.vertex.xyz는 스케일 적용 전 로컬 좌표이므로 이와 일치해야 함
        Bounds meshBounds;

        if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
        {
            meshBounds = skinnedRenderer.sharedMesh.bounds;
            Log($"  - SkinnedMesh 바운드: min{meshBounds.min}, max{meshBounds.max}");
        }
        else if (renderer is MeshRenderer meshRenderer)
        {
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshBounds = meshFilter.sharedMesh.bounds;
                Log($"  - Mesh 바운드: min{meshBounds.min}, max{meshBounds.max}");
            }
            else
            {
                // 폴백: 월드 바운드를 로컬로 변환
                meshBounds = renderer.bounds;
                Log($"  - 폴백 바운드: min{meshBounds.min}, max{meshBounds.max}");
            }
        }
        else
        {
            meshBounds = renderer.bounds;
            Log($"  - 기본 바운드: min{meshBounds.min}, max{meshBounds.max}");
        }

        // ★ 머티리얼 속성 설정 (텍스처는 mainTexture로도 설정)
        mat.mainTexture = gpuTexture;
        mat.SetTexture("_MainTex", gpuTexture);
        mat.SetFloat("_HasTexture", 1f);
        mat.SetFloat("_MinZ", meshBounds.min.z);
        mat.SetFloat("_MaxZ", meshBounds.max.z);
        mat.SetFloat("_MinY", meshBounds.min.y);
        mat.SetFloat("_MaxY", meshBounds.max.y);
        mat.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f, 1f));

        // ★ 머티리얼 적용
        renderer.material = mat;

        Log($"새 머티리얼 생성 및 적용: {renderer.name}");
        Log($"  - 바운드 (YZ): Z({meshBounds.min.z:F4}~{meshBounds.max.z:F4}), Y({meshBounds.min.y:F4}~{meshBounds.max.y:F4})");
        Log($"  - 텍스처: {gpuTexture.width}x{gpuTexture.height}");
        Log($"  - GPU 텍스처 ID: {gpuTexture.GetNativeTexturePtr()}");

        // 텍스처 적용 확인
        var appliedTex = mat.GetTexture("_MainTex") as Texture2D;
        Log($"  - _MainTex 확인: {(appliedTex != null ? $"{appliedTex.width}x{appliedTex.height}" : "NULL!")}");
        Log($"  - mainTexture 확인: {(mat.mainTexture != null ? $"{mat.mainTexture.width}x{mat.mainTexture.height}" : "NULL!")}");
        Log($"  - _HasTexture: {mat.GetFloat("_HasTexture")}");
        Log($"  - Shader: {mat.shader.name}");
    }

    private static readonly string DebugFolder = "C:/ProgramData/LiveSketch/Debug";
    private static readonly string LogFile = "C:/ProgramData/LiveSketch/Debug/scan_log.txt";

    private void Log(string message)
    {
        string logMessage = $"[{System.DateTime.Now:HH:mm:ss}] {message}";
        if (showDebugLogs)
            Debug.Log($"[ScanProcessor] {message}");

        // 파일로도 저장
        AppendToLogFile(logMessage);
    }

    private void AppendToLogFile(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(DebugFolder);
            System.IO.File.AppendAllText(LogFile, message + "\n");
        }
        catch { }
    }

    private void SaveDebugTexture(Texture2D texture, string filename)
    {
        try
        {
            System.IO.Directory.CreateDirectory(DebugFolder);
            byte[] bytes = texture.EncodeToPNG();
            string path = System.IO.Path.Combine(DebugFolder, filename);
            System.IO.File.WriteAllBytes(path, bytes);
            Log($"디버그 텍스처 저장: {path} ({texture.width}x{texture.height})");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ScanProcessor] 텍스처 저장 실패: {e.Message}");
        }
    }
}
