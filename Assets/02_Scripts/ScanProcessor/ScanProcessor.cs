using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 캐릭터별 색상 합성 설정
/// </summary>
[Serializable]
public class CharacterColorSettings
{
    [Tooltip("QR 코드 텍스트 (캐릭터 이름)")]
    public string characterName;

    [Header("=== 색상 필터링 ===")]
    [Tooltip("최소 채도 (0.15~0.3)")]
    [Range(0.05f, 0.5f)]
    public float minSaturation = 0.2f;

    [Tooltip("외곽선 밝기 임계값 (베이스 이미지)")]
    [Range(0.2f, 0.7f)]
    public float outlineThreshold = 0.5f;

    [Tooltip("스캔 색상 최소 밝기 (어두운 색 필터링)")]
    [Range(0.1f, 0.8f)]
    public float minBrightness = 0.3f;

    [Header("=== 위치 조정 ===")]
    public bool flipY = true;

    [Tooltip("X 이동 (픽셀 비율)")]
    [Range(-1f, 1f)]
    public float offsetX = 0f;

    [Tooltip("Y 이동 (픽셀 비율)")]
    [Range(-1f, 1f)]
    public float offsetY = 0f;

    [Tooltip("X축 스케일")]
    [Range(0.5f, 2f)]
    public float scaleX = 1f;

    [Tooltip("Y축 스케일")]
    [Range(0.5f, 2f)]
    public float scaleY = 1f;

    [Header("=== 머리 분리 애니메이션 ===")]
    [Tooltip("머리 분리 활성화")]
    public bool enableHeadSplit = false;

    [Tooltip("머리/몸통 경계선 (아래에서 몇 % 위치)")]
    [Range(0.3f, 0.9f)]
    public float headSplitY = 0.7f;

    [Tooltip("머리 피벗 Y 오프셋 (회전 중심점 조정)")]
    [Range(-0.5f, 0.5f)]
    public float headPivotOffsetY = 0f;
}

/// <summary>
/// 스캔 → 색상 입히기 파이프라인
/// </summary>
public class ScanProcessor : MonoBehaviour
{
    [Header("=== 필수 연결 ===")]
    [SerializeField] private ScanFolderWatcher watcher;
    [SerializeField] private AnimalModelManager modelManager;

    [Header("=== 마커 감지 ===")]
    [SerializeField] private bool useAutoMarkerDetection = true;
    [SerializeField] private Vector2 cropMin = new Vector2(0.04f, 0.03f);
    [SerializeField] private Vector2 cropMax = new Vector2(0.96f, 0.75f);

    [Header("=== 캐릭터별 설정 ===")]
    [Tooltip("캐릭터별 색상/위치 설정 리스트")]
    [SerializeField] private List<CharacterColorSettings> characterSettings = new List<CharacterColorSettings>();

    [Header("=== 기본 설정 (매칭 실패 시) ===")]
    [SerializeField] private CharacterColorSettings defaultSettings = new CharacterColorSettings();

    [Header("=== 런타임 조정 ===")]
    [SerializeField] private bool liveAdjust = true;

    [Header("=== 수동 스폰 (스캔 없이) ===")]
    [Tooltip("characterSettings 리스트에서 스폰할 캐릭터 인덱스")]
    [SerializeField] private int manualSpawnIndex = 0;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showLogs = true;

    // 캐시
    private Texture2D _scanTex, _baseTex;
    private SpriteRenderer _sr;
    private string _currentCharacter;
    private CharacterColorSettings _currentSettings;

    // 머리 분리용
    private GameObject _headObject;
    private SpriteRenderer _headSr;
    private SpriteRenderer _bodySr;

    // 변경 감지용
    private float _pOffX, _pOffY, _pScaleX, _pScaleY, _pMinSat, _pOutline, _pMinBright;
    private bool _pFlipY, _pHeadSplit;
    private float _pHeadSplitY;

    void OnEnable() { if (watcher) watcher.OnScanTextureReady += OnScan; }
    void OnDisable() { if (watcher) watcher.OnScanTextureReady -= OnScan; }

    void Update()
    {
        // 수동 스폰: Space 키
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ManualSpawn();
        }

        if (!liveAdjust || _scanTex == null || _baseTex == null || _sr == null || _currentSettings == null) return;

        // 현재 설정값 변경 감지
        if (HasSettingsChanged())
        {
            CacheCurrentSettings();
            ApplyColors();
        }
    }

    bool HasSettingsChanged()
    {
        return _pOffX != _currentSettings.offsetX ||
               _pOffY != _currentSettings.offsetY ||
               _pScaleX != _currentSettings.scaleX ||
               _pScaleY != _currentSettings.scaleY ||
               _pMinSat != _currentSettings.minSaturation ||
               _pOutline != _currentSettings.outlineThreshold ||
               _pMinBright != _currentSettings.minBrightness ||
               _pFlipY != _currentSettings.flipY ||
               _pHeadSplit != _currentSettings.enableHeadSplit ||
               _pHeadSplitY != _currentSettings.headSplitY;
    }

    void CacheCurrentSettings()
    {
        if (_currentSettings == null) return;
        _pOffX = _currentSettings.offsetX;
        _pOffY = _currentSettings.offsetY;
        _pScaleX = _currentSettings.scaleX;
        _pScaleY = _currentSettings.scaleY;
        _pMinSat = _currentSettings.minSaturation;
        _pOutline = _currentSettings.outlineThreshold;
        _pMinBright = _currentSettings.minBrightness;
        _pFlipY = _currentSettings.flipY;
        _pHeadSplit = _currentSettings.enableHeadSplit;
        _pHeadSplitY = _currentSettings.headSplitY;
    }

    /// <summary>
    /// QR 코드로 캐릭터 설정 찾기
    /// </summary>
    CharacterColorSettings FindSettings(string qrText)
    {
        foreach (var setting in characterSettings)
        {
            if (setting.characterName == qrText)
            {
                Log($"캐릭터 설정 찾음: {qrText}");
                return setting;
            }
        }
        Log($"캐릭터 설정 없음: {qrText} → 기본값 사용");
        return defaultSettings;
    }

    /// <summary>
    /// 수동 스폰 - 스캔 없이 베이스 스프라이트만으로 캐릭터 생성
    /// </summary>
    void ManualSpawn()
    {
        if (characterSettings == null || characterSettings.Count == 0) return;

        int idx = Mathf.Clamp(manualSpawnIndex, 0, characterSettings.Count - 1);
        string charName = characterSettings[idx].characterName;
        if (string.IsNullOrEmpty(charName)) return;

        _currentCharacter = charName;
        _currentSettings = characterSettings[idx];

        modelManager?.SpawnSpriteByQR(charName);
        _sr = modelManager?.GetCurrentSpriteRenderer();

        Log($"수동 스폰: {charName} (인덱스 {idx})");
    }

    void OnScan(Texture2D tex, string path)
    {
        if (tex == null) return;
        Log($"스캔: {tex.width}x{tex.height}");

        // QR + 방향 보정
        var (qr, pos, corrected) = ImageOrientationCorrector.DetectAndCorrect(tex);
        if (string.IsNullOrEmpty(qr) || corrected == null) { Log("QR 실패"); return; }
        Log($"QR: {qr}, 방향: {pos}");

        // 캐릭터 설정 찾기
        _currentCharacter = qr;
        _currentSettings = FindSettings(qr);

        // 캐릭터 스폰
        modelManager?.SpawnSpriteByQR(qr);

        // 크롭
        Texture2D cropped = Crop(corrected);
        Destroy(corrected);
        if (cropped == null) { Log("크롭 실패"); return; }

        // 캐시 저장
        _scanTex = cropped;
        _sr = modelManager?.GetCurrentSpriteRenderer();
        if (_sr == null || _sr.sprite == null) { Log("스프라이트 없음"); return; }
        _baseTex = _sr.sprite.texture;
        if (_baseTex == null || !_baseTex.isReadable) { Log("텍스처 읽기 불가"); return; }

        CacheCurrentSettings();
        ApplyColors();
    }

    void ApplyColors()
    {
        if (_currentSettings == null) return;

        Texture2D result = Blend(_baseTex, _scanTex, _currentSettings);
        if (result == null) return;

        // 머리 분리 활성화 시
        if (_currentSettings.enableHeadSplit)
        {
            ApplyWithHeadSplit(result);
        }
        else
        {
            // 기존 방식: 단일 스프라이트
            CleanupHeadObject();
            Sprite spr = Sprite.Create(result, new Rect(0, 0, result.width, result.height), new Vector2(0.5f, 0.5f), 100f);
            _sr.sprite = spr;
        }

        modelManager?.TriggerBurstForLatest();
        Log("적용 완료");
    }

    /// <summary>
    /// 머리/몸통 분리 적용
    /// </summary>
    void ApplyWithHeadSplit(Texture2D coloredTex)
    {
        int w = coloredTex.width;
        int h = coloredTex.height;
        int splitY = Mathf.RoundToInt(h * _currentSettings.headSplitY);

        // 몸통 (아래 부분)
        int bodyH = splitY;
        if (bodyH > 0)
        {
            var bodyPixels = coloredTex.GetPixels(0, 0, w, bodyH);
            var bodyTex = new Texture2D(w, bodyH, TextureFormat.RGBA32, false);
            bodyTex.SetPixels(bodyPixels);
            bodyTex.Apply();
            bodyTex.filterMode = FilterMode.Bilinear;

            // 몸통 피벗: 하단 중앙 기준, 상단이 분리 지점
            float bodyPivotY = 0f; // 하단
            Sprite bodySpr = Sprite.Create(bodyTex, new Rect(0, 0, w, bodyH), new Vector2(0.5f, bodyPivotY), 100f);
            _sr.sprite = bodySpr;
        }

        // 머리 (위 부분)
        int headH = h - splitY;
        if (headH > 0)
        {
            var headPixels = coloredTex.GetPixels(0, splitY, w, headH);
            var headTex = new Texture2D(w, headH, TextureFormat.RGBA32, false);
            headTex.SetPixels(headPixels);
            headTex.Apply();
            headTex.filterMode = FilterMode.Bilinear;

            // 머리 오브젝트 생성/업데이트
            SetupHeadObject(headTex, w, headH, splitY, h);
        }
    }

    /// <summary>
    /// 머리 오브젝트 설정
    /// </summary>
    void SetupHeadObject(Texture2D headTex, int w, int headH, int splitY, int totalH)
    {
        GameObject parent = _sr.gameObject;

        // 기존 머리 오브젝트가 없으면 생성
        if (_headObject == null)
        {
            _headObject = new GameObject("Head");
            _headObject.transform.SetParent(parent.transform);
            _headObject.transform.localScale = Vector3.one;

            _headSr = _headObject.AddComponent<SpriteRenderer>();
            _headSr.sortingOrder = _sr.sortingOrder + 1; // 몸통 위에 렌더링

            // HeadAnimator 추가
            var animator = _headObject.AddComponent<HeadAnimator>();
            Log("머리 오브젝트 생성 + HeadAnimator 추가");
        }

        // 머리 피벗: 하단 중앙 (회전 중심점)
        float headPivotY = _currentSettings.headPivotOffsetY;
        Sprite headSpr = Sprite.Create(headTex, new Rect(0, 0, w, headH), new Vector2(0.5f, headPivotY), 100f);
        _headSr.sprite = headSpr;

        // 머리 위치: 몸통 위에 배치
        // 몸통 피벗이 하단이므로, 머리는 splitY/100 만큼 위에
        float headLocalY = (float)splitY / 100f;
        _headObject.transform.localPosition = new Vector3(0, headLocalY, 0);

        // HeadAnimator 초기값 재설정
        var headAnimator = _headObject.GetComponent<HeadAnimator>();
        if (headAnimator != null)
        {
            headAnimator.RecaptureInitialValues();
        }
    }

    /// <summary>
    /// 머리 오브젝트 정리
    /// </summary>
    void CleanupHeadObject()
    {
        if (_headObject != null)
        {
            // 스프라이트/텍스처 정리
            if (_headSr != null && _headSr.sprite != null)
            {
                var tex = _headSr.sprite.texture;
                Destroy(_headSr.sprite);
                if (tex != null) Destroy(tex);
            }
            Destroy(_headObject);
            _headObject = null;
            _headSr = null;
            Log("머리 오브젝트 정리됨");
        }
    }

    Texture2D Crop(Texture2D src)
    {
        Rect bounds;

        if (useAutoMarkerDetection)
        {
            var det = CornerMarkerDetector.DetectMarkers(src);

            if (det.success)
            {
                bounds = det.sketchBounds;
                Log($"마커 감지 성공! 크롭 영역: ({bounds.x:F2}, {bounds.y:F2}) ~ ({bounds.xMax:F2}, {bounds.yMax:F2})");
            }
            else
            {
                // 재시도: 대비 강화 이미지로 마커 재감지
                Log("마커 감지 실패 → 대비 강화 후 재시도...");
                Texture2D enhanced = EnhanceForMarkerDetection(src);
                det = CornerMarkerDetector.DetectMarkers(enhanced);
                Destroy(enhanced);

                if (det.success)
                {
                    bounds = det.sketchBounds;
                    Log($"대비 강화 후 마커 감지 성공! 크롭 영역: ({bounds.x:F2}, {bounds.y:F2}) ~ ({bounds.xMax:F2}, {bounds.yMax:F2})");
                }
                else
                {
                    bounds = new Rect(cropMin.x, cropMin.y, cropMax.x - cropMin.x, cropMax.y - cropMin.y);
                    Log($"모든 감지 시도 실패 → 기본값 사용: ({bounds.x:F2}, {bounds.y:F2}) ~ ({bounds.xMax:F2}, {bounds.yMax:F2})");
                }
            }
        }
        else
        {
            bounds = new Rect(cropMin.x, cropMin.y, cropMax.x - cropMin.x, cropMax.y - cropMin.y);
            Log("자동 마커 감지 OFF → 수동 크롭");
        }

        int x = Mathf.RoundToInt(bounds.x * src.width);
        int y = Mathf.RoundToInt(bounds.y * src.height);
        int w = Mathf.RoundToInt(bounds.width * src.width);
        int h = Mathf.RoundToInt(bounds.height * src.height);
        x = Mathf.Clamp(x, 0, src.width - 1);
        y = Mathf.Clamp(y, 0, src.height - 1);
        w = Mathf.Clamp(w, 1, src.width - x);
        h = Mathf.Clamp(h, 1, src.height - y);

        Log($"크롭 실행: ({x}, {y}) 크기: {w}x{h}");

        var px = src.GetPixels(x, y, w, h);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 색상 합성 - 캐릭터별 설정 적용
    /// </summary>
    Texture2D Blend(Texture2D baseImg, Texture2D scanImg, CharacterColorSettings settings)
    {
        int bw = baseImg.width, bh = baseImg.height;
        int sw = scanImg.width, sh = scanImg.height;

        Color[] basePx = baseImg.GetPixels();
        Color[] result = new Color[basePx.Length];

        // 설정값 로컬 변수로 캐시 (성능)
        float minSat = settings.minSaturation;
        float outlineTh = settings.outlineThreshold;
        float minBright = settings.minBrightness;
        float offX = settings.offsetX;
        float offY = settings.offsetY;
        float sclX = settings.scaleX;
        float sclY = settings.scaleY;
        bool flip = settings.flipY;

        // 오프셋 (픽셀 단위)
        int pixelOffX = Mathf.RoundToInt(offX * bw);
        int pixelOffY = Mathf.RoundToInt(offY * bh);

        for (int i = 0; i < basePx.Length; i++)
        {
            Color baseCol = basePx[i];
            int bx = i % bw;
            int by = i / bw;

            // 투명 유지
            if (baseCol.a < 0.1f) { result[i] = baseCol; continue; }

            // 외곽선 유지
            Color.RGBToHSV(baseCol, out _, out _, out float v);
            if (v < outlineTh) { result[i] = baseCol; continue; }

            // 베이스 좌표 → 스캔 좌표 (스케일 + 오프셋 적용)
            float normX = (float)(bx - bw / 2) / (bw * sclX) + 0.5f - (float)pixelOffX / bw;
            float normY = (float)(by - bh / 2) / (bh * sclY) + 0.5f - (float)pixelOffY / bh;

            // 상하 반전
            if (flip) normY = 1f - normY;

            // 범위 체크
            if (normX < 0 || normX > 1 || normY < 0 || normY > 1)
            {
                result[i] = baseCol;
                continue;
            }

            // 스캔 좌표 (리사이즈된 것처럼 샘플링)
            int sx = Mathf.Clamp(Mathf.RoundToInt(normX * (sw - 1)), 0, sw - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(normY * (sh - 1)), 0, sh - 1);

            Color scanCol = scanImg.GetPixel(sx, sy);

            // HSV 변환
            Color.RGBToHSV(scanCol, out _, out float sat, out float val);

            // 채도 체크 - 무채색(회색/검정/흰색)이면 베이스 유지
            if (sat < minSat) { result[i] = baseCol; continue; }

            // 밝기 체크 - 어두운 색(검정/진한 색)이면 베이스 유지
            if (val < minBright) { result[i] = baseCol; continue; }

            // 색상 적용
            result[i] = new Color(scanCol.r, scanCol.g, scanCol.b, 1f);
        }

        var output = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
        output.SetPixels(result);
        output.Apply();
        output.filterMode = FilterMode.Bilinear;
        return output;
    }

    /// <summary>
    /// 마커 감지용 대비 강화 (어두운 부분 더 어둡게, 밝은 부분 더 밝게)
    /// </summary>
    Texture2D EnhanceForMarkerDetection(Texture2D source)
    {
        var pixels = source.GetPixels();
        var result = new Color[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            float r = Mathf.Clamp01((c.r - 0.5f) * 1.5f + 0.5f);
            float g = Mathf.Clamp01((c.g - 0.5f) * 1.5f + 0.5f);
            float b = Mathf.Clamp01((c.b - 0.5f) * 1.5f + 0.5f);
            result[i] = new Color(r, g, b, c.a);
        }

        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        tex.SetPixels(result);
        tex.Apply();
        return tex;
    }

    void Log(string m) { if (showLogs) Debug.Log($"[Scan] {m}"); }

    /// <summary>
    /// 현재 캐릭터 이름 반환 (에디터용)
    /// </summary>
    public string GetCurrentCharacterName() => _currentCharacter;

    /// <summary>
    /// 현재 사용 중인 설정 반환 (에디터용)
    /// </summary>
    public CharacterColorSettings GetCurrentSettings() => _currentSettings;
}
