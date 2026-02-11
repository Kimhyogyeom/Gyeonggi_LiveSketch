using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 3D 모델용 스캔 프로세서
/// - 스캔 이미지에서 색상 추출 → 3D 모델 Material에 텍스처로 적용
/// - UV 매핑으로 애니메이션 포즈와 상관없이 색상 유지
/// </summary>
public class ScanProcessor3D : MonoBehaviour
{
    [Header("=== 필수 연결 ===")]
    [SerializeField] private ScanFolderWatcher watcher;
    [SerializeField] private Model3DManager modelManager;

    [Header("=== 마커 감지 ===")]
    [Tooltip("마커 자동 감지 (실패 시 기본 크롭 사용)")]
    [SerializeField] private bool useAutoMarkerDetection = false;

    [Tooltip("크롭 시작점 (0~1)")]
    [SerializeField] private Vector2 cropMin = new Vector2(0.05f, 0.05f);

    [Tooltip("크롭 끝점 (0~1)")]
    [SerializeField] private Vector2 cropMax = new Vector2(0.95f, 0.70f);

    [Header("=== 텍스처 설정 ===")]
    [Tooltip("텍스처 필터 모드")]
    [SerializeField] private FilterMode textureFilterMode = FilterMode.Bilinear;

    [Tooltip("텍스처 Wrap 모드")]
    [SerializeField] private TextureWrapMode textureWrapMode = TextureWrapMode.Clamp;

    [Header("=== 색상 처리 ===")]
    [Tooltip("배경 제거 (흰색/밝은 색 투명 처리)")]
    [SerializeField] private bool removeBackground = false;

    [Tooltip("배경으로 판단할 최소 밝기")]
    [Range(0.7f, 1f)]
    [SerializeField] private float backgroundBrightnessThreshold = 0.9f;

    [Tooltip("배경으로 판단할 최대 채도")]
    [Range(0f, 0.3f)]
    [SerializeField] private float backgroundSaturationThreshold = 0.1f;

    [Header("=== 성능 ===")]
    [Tooltip("처리용 최대 이미지 크기 (QR+크롭+보정 전체에 적용. 클수록 정확, 작을수록 빠름)")]
    [SerializeField] private int maxProcessingSize = 1000;

    [Header("=== 오디오 ===")]
    [Tooltip("처리 실패 시 재생할 오디오 클립 (QR 미인식, 모델 스폰 실패 등)")]
    [SerializeField] private AudioClip failAudioClip;

    [Tooltip("오디오 재생용 AudioSource (비워두면 자동 생성)")]
    [SerializeField] private AudioSource audioSource;

    [Header("=== 수동 스폰 (스캔 없이) ===")]
    [Tooltip("Model3DManager의 modelEntries 리스트에서 스폰할 캐릭터 인덱스")]
    [SerializeField] private int manualSpawnIndex = 0;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showLogs = true;

    // 캐시
    private Texture2D _lastScanTexture;
    private string _lastQRText;

    void OnEnable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady += OnScanReceived;
    }

    void OnDisable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady -= OnScanReceived;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ManualSpawn();
        }
    }

    /// <summary>
    /// 수동 스폰 - 스캔 없이 3D 모델만 생성
    /// </summary>
    void ManualSpawn()
    {
        if (modelManager == null) return;

        string qrText = modelManager.GetEntryQRText(manualSpawnIndex);
        if (string.IsNullOrEmpty(qrText)) return;

        bool success = modelManager.SpawnModelByQR(qrText);
        if (success)
            Log($"수동 스폰: {qrText} (인덱스 {manualSpawnIndex})");
        else
            Log($"수동 스폰 실패: {qrText}");
    }

    /// <summary>
    /// 스캔 이미지 수신 처리 → 코루틴으로 분산 (렉 방지)
    /// </summary>
    void OnScanReceived(Texture2D tex, string path)
    {
        if (tex == null)
        {
            Log("스캔 텍스처가 null");
            return;
        }

        StartCoroutine(ProcessScanCoroutine(tex, path));
    }

    IEnumerator ProcessScanCoroutine(Texture2D tex, string path)
    {
        Log($"스캔 수신: {tex.width}x{tex.height}");

        // 0. 성능: 이미지가 크면 GPU로 축소 (Graphics.Blit = 즉시)
        //    스캐너 원본(2400x3500) → 축소(~1018x1500) = 픽셀 5.6배 감소
        //    3D 모델 텍스처에 스캐너 원본 해상도는 불필요
        Texture2D processingTex = tex;
        bool needsProcessingTexDestroy = false;

        if (tex.width > maxProcessingSize || tex.height > maxProcessingSize)
        {
            float scale = Mathf.Min((float)maxProcessingSize / tex.width, (float)maxProcessingSize / tex.height);
            int newW = Mathf.RoundToInt(tex.width * scale);
            int newH = Mathf.RoundToInt(tex.height * scale);
            processingTex = DownscaleTextureGPU(tex, newW, newH);
            needsProcessingTexDestroy = true;
            Log($"이미지 축소: {tex.width}x{tex.height} → {newW}x{newH} (처리 속도 향상)");
        }

        yield return null;

        // 1. QR 코드 감지 + 이미지 방향 보정 (축소된 이미지로 처리 → 훨씬 빠름)
        var (qrText, position, correctedImage) = ImageOrientationCorrector.DetectAndCorrect(processingTex);

        if (needsProcessingTexDestroy)
            Destroy(processingTex);

        if (string.IsNullOrEmpty(qrText) || correctedImage == null)
        {
            Log("QR 코드 감지 실패");
            PlayFailAudio();
            yield break;
        }

        Log($"QR: {qrText}, 방향: {position}");
        _lastQRText = qrText;

        yield return null;

        // 2. 3D 모델 스폰 (비동기 → 렉 방지)
        bool spawnSuccess = false;
        yield return StartCoroutine(modelManager.SpawnModelByQRAsync(qrText, success => spawnSuccess = success));
        if (!spawnSuccess)
        {
            Log($"모델 스폰 실패: {qrText}");
            PlayFailAudio();
            Destroy(correctedImage);
            yield break;
        }

        // 3. 마커 기반 크롭
        Texture2D croppedTexture = CropDrawingArea(correctedImage);
        Destroy(correctedImage);

        if (croppedTexture == null)
        {
            Log("크롭 실패");
            yield break;
        }

        // 프레임 분산: 크롭 후 1프레임 대기
        yield return null;

        // 4. 배경 제거 (선택적)
        Texture2D processedTexture = croppedTexture;
        if (removeBackground)
        {
            processedTexture = RemoveBackgroundColor(croppedTexture);
            if (processedTexture != croppedTexture)
            {
                Destroy(croppedTexture);
            }
            yield return null;
        }

        // 5. 텍스처 설정 적용
        processedTexture.filterMode = textureFilterMode;
        processedTexture.wrapMode = textureWrapMode;

        // 6. 3D 모델에 텍스처 적용
        bool applied = modelManager?.ApplyTextureToCurrentModel(processedTexture) ?? false;

        if (applied)
        {
            // 텍스처는 Model3DManager가 관리 (SpawnedModel.appliedTexture)
            // 여기서 이전 텍스처를 Destroy하면 이전 모델이 검은색으로 변함!
            _lastScanTexture = processedTexture;
            Log($"텍스처 적용 완료: {processedTexture.width}x{processedTexture.height}");
        }
        else
        {
            Log("텍스처 적용 실패");
            Destroy(processedTexture);
        }
    }

    /// <summary>
    /// 마커 기반 드로잉 영역 크롭
    /// </summary>
    Texture2D CropDrawingArea(Texture2D source)
    {
        Rect bounds;

        if (useAutoMarkerDetection)
        {
            var detection = CornerMarkerDetector.DetectMarkers(source);

            if (detection.success)
            {
                bounds = detection.sketchBounds;
                Log($"마커 감지 성공: ({bounds.x:F2}, {bounds.y:F2}) ~ ({bounds.xMax:F2}, {bounds.yMax:F2})");
            }
            else
            {
                bounds = new Rect(cropMin.x, cropMin.y, cropMax.x - cropMin.x, cropMax.y - cropMin.y);
                Log($"마커 감지 실패 → 기본값: ({bounds.x:F2}, {bounds.y:F2})");
            }
        }
        else
        {
            bounds = new Rect(cropMin.x, cropMin.y, cropMax.x - cropMin.x, cropMax.y - cropMin.y);
            Log("자동 감지 OFF → 수동 크롭");
        }

        // 픽셀 좌표 계산
        int x = Mathf.RoundToInt(bounds.x * source.width);
        int y = Mathf.RoundToInt(bounds.y * source.height);
        int w = Mathf.RoundToInt(bounds.width * source.width);
        int h = Mathf.RoundToInt(bounds.height * source.height);

        // 범위 클램프
        x = Mathf.Clamp(x, 0, source.width - 1);
        y = Mathf.Clamp(y, 0, source.height - 1);
        w = Mathf.Clamp(w, 1, source.width - x);
        h = Mathf.Clamp(h, 1, source.height - y);

        Log($"크롭: ({x}, {y}) 크기 {w}x{h}");

        // 크롭 실행
        var pixels = source.GetPixels(x, y, w, h);
        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        return cropped;
    }

    /// <summary>
    /// 배경색 제거 (밝은 흰색/회색 → 투명)
    /// </summary>
    Texture2D RemoveBackgroundColor(Texture2D source)
    {
        int w = source.width;
        int h = source.height;
        Color[] pixels = source.GetPixels();
        Color[] result = new Color[pixels.Length];

        int removedCount = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            Color.RGBToHSV(c, out float hue, out float sat, out float val);

            // 배경 판단: 밝고(val > threshold) + 채도가 낮은(sat < threshold) 픽셀
            if (val > backgroundBrightnessThreshold && sat < backgroundSaturationThreshold)
            {
                result[i] = new Color(c.r, c.g, c.b, 0f); // 투명
                removedCount++;
            }
            else
            {
                result[i] = c;
            }
        }

        var processed = new Texture2D(w, h, TextureFormat.RGBA32, false);
        processed.SetPixels(result);
        processed.Apply();

        Log($"배경 제거: {removedCount} 픽셀 ({(float)removedCount / pixels.Length * 100:F1}%)");

        return processed;
    }

    /// <summary>
    /// 마지막으로 적용된 텍스처 반환
    /// </summary>
    public Texture2D GetLastScanTexture() => _lastScanTexture;

    /// <summary>
    /// 마지막 QR 텍스트 반환
    /// </summary>
    public string GetLastQRText() => _lastQRText;

    /// <summary>
    /// GPU 기반 고속 이미지 축소 (Graphics.Blit → ReadPixels)
    /// CPU 픽셀 조작 대비 훨씬 빠름
    /// </summary>
    Texture2D DownscaleTextureGPU(Texture2D source, int newWidth, int newHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        var result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        result.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }

    void PlayFailAudio()
    {
        if (failAudioClip == null) return;
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.PlayOneShot(failAudioClip);
    }

    void Log(string message)
    {
        if (showLogs)
            Debug.Log($"[Scan3D] {message}");
    }
}
