using UnityEngine;

/// <summary>
/// 스캔 파이프라인 메인 컨트롤러
/// A4 가로 방향 스캔 기준
///
/// 처리 흐름:
/// 1. 스캔 이미지 수신
/// 2. QR 코드 인식 → 해당 모델 스폰
/// 3. 마커 영역 기준 스케치 영역 크롭
/// 4. 테두리 제거 → 컬러만 추출
/// 5. 새로 스폰된 모델에만 텍스처 적용 (기존 모델 유지)
/// </summary>
public class ScanProcessor : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private ScanFolderWatcher watcher;
    [SerializeField] private AnimalModelManager modelManager;

    [Header("텍스처 설정")]
    [Tooltip("셰이더에서 사용하는 텍스처 프로퍼티 이름")]
    [SerializeField] private string textureProperty = "_MainTex";

    [Header("크롭 설정 (A4 가로 기준)")]
    [Tooltip("4개 모서리 마커 자동 감지 사용")]
    [SerializeField] private bool useAutoMarkerDetection = true;

    [Tooltip("자동 감지 실패 시 고정 크롭 영역 (normalized 0~1)")]
    [SerializeField] private Vector2 cropMin = new Vector2(0.05f, 0.1f);
    [SerializeField] private Vector2 cropMax = new Vector2(0.75f, 0.9f);

    [Header("컬러 추출 설정")]
    [Tooltip("테두리/윤곽선 제거하고 컬러만 추출")]
    [SerializeField] private bool extractColorOnly = true;

    [Header("QR 설정 (A4 가로 기준 - 우측 하단)")]
    [Tooltip("QR 코드 검색 영역 (normalized)")]
    [SerializeField] private Rect qrSearchRegion = new Rect(0.75f, 0.0f, 0.25f, 0.3f);

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = true;
    [Tooltip("처리된 텍스처 미리보기 (선택)")]
    [SerializeField] private Renderer previewRenderer;

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

        // 1. QR 코드 인식 → 모델 스폰
        string qrText = ReadQRCode(scanTexture);
        if (!string.IsNullOrEmpty(qrText))
        {
            Log($"QR 인식: {qrText}");
            modelManager?.SpawnModelByQR(qrText);
        }
        else
        {
            Log("QR 인식 실패 - 모델 스폰 안함");
            return; // QR 없으면 처리 안함
        }

        // 2. 스케치 영역 크롭 (마커 기준)
        Texture2D cropped = CropSketchArea(scanTexture);
        if (cropped == null)
        {
            Log("크롭 실패, 원본 사용");
            cropped = DuplicateTexture(scanTexture);
        }

        // 3. 컬러만 추출 (테두리/윤곽선 제거)
        Texture2D colorOnly = cropped;
        if (extractColorOnly)
        {
            colorOnly = ColorExtractor.ExtractColorsOnly(cropped);
            if (colorOnly != cropped)
                Object.Destroy(cropped);

            Log("컬러 추출 완료 (테두리/윤곽선 제거됨)");
        }

        // 4. 미리보기 (선택)
        if (previewRenderer != null)
            previewRenderer.material.mainTexture = colorOnly;

        // 5. 새로 스폰된 모델에 텍스처 적용 (텍스처는 모델이 소유)
        ApplyToCurrentModel(colorOnly);
    }

    /// <summary>
    /// QR 코드 읽기 (전체 → 4분할 영역 순으로 시도)
    /// </summary>
    private string ReadQRCode(Texture2D tex)
    {
        // 1. 전체 이미지에서 시도
        string result = QRReader.ReadQRCode(tex);
        if (!string.IsNullOrEmpty(result)) return result;

        // 2. 인스펙터 지정 영역에서 시도
        result = QRReader.ReadQRCodeInRegion(tex, qrSearchRegion);
        if (!string.IsNullOrEmpty(result)) return result;

        // 3. 4개 코너 영역 순차 검색 (우하 → 우상 → 좌하 → 좌상)
        Rect[] cornerRegions = new Rect[]
        {
            new Rect(0.7f, 0.0f, 0.3f, 0.3f),  // 우측 하단
            new Rect(0.7f, 0.7f, 0.3f, 0.3f),  // 우측 상단
            new Rect(0.0f, 0.0f, 0.3f, 0.3f),  // 좌측 하단
            new Rect(0.0f, 0.7f, 0.3f, 0.3f),  // 좌측 상단
        };

        foreach (var region in cornerRegions)
        {
            result = QRReader.ReadQRCodeInRegion(tex, region);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        return null;
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

    private Texture2D DuplicateTexture(Texture2D source)
    {
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.SetPixels(source.GetPixels());
        copy.Apply();
        copy.wrapMode = TextureWrapMode.Clamp;
        return copy;
    }

    /// <summary>
    /// 현재(방금 스폰된) 모델에 텍스처 적용
    /// 텍스처는 모델이 소유 - 삭제하지 않음
    /// </summary>
    private void ApplyToCurrentModel(Texture2D texture)
    {
        Renderer renderer = modelManager?.GetCurrentRenderer();

        if (renderer != null)
        {
            // 머티리얼 배열에서 마지막 머티리얼 (스캔 텍스처용)에만 적용
            Material[] materials = renderer.sharedMaterials;

            if (materials.Length > 1)
            {
                // Material 1 = 스캔 텍스처 (투명 쉐이더)
                // 인스턴스 생성해서 텍스처 적용
                materials[1] = new Material(materials[1]);
                materials[1].SetTexture(textureProperty, texture);
                renderer.materials = materials;

                Log($"텍스처 적용 완료 (Material[1]): {renderer.name}");
            }
            else
            {
                // 머티리얼이 1개만 있으면 기존 방식 (하위 호환)
                renderer.material.SetTexture(textureProperty, texture);
                Log($"텍스처 적용 완료 (단일 Material): {renderer.name}");
            }
        }
        else
        {
            Log("텍스처 적용할 모델 없음");
            // 적용할 모델이 없으면 텍스처 삭제
            Object.Destroy(texture);
        }
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[ScanProcessor] {message}");
    }
}
