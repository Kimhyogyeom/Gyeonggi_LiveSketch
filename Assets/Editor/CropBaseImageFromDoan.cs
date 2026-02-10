using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 도안 이미지에서 마커 안쪽 영역을 크롭하여 베이스 이미지 생성
/// 메뉴: Tools > LiveSketch > Crop Base Image From Doan
/// </summary>
public class CropBaseImageFromDoan : EditorWindow
{
    private string doanImagePath = "Assets/05_Images/Character/꾸구리도안.png";
    private string outputFileName = "꾸구리_base";

    // 마커 감지와 동일한 설정값 사용
    private const float BLACK_THRESHOLD = 0.3f;
    private const float SATURATION_THRESHOLD = 0.15f;
    private const int MIN_MARKER_PIXELS = 200;
    private const float MIN_DENSITY = 0.25f;
    private const float MARKER_OFFSET = 0.08f;

    [MenuItem("Tools/LiveSketch/Crop Base Image From Doan")]
    public static void ShowWindow()
    {
        GetWindow<CropBaseImageFromDoan>("Crop Base Image");
    }

    private void OnGUI()
    {
        GUILayout.Label("도안에서 베이스 이미지 크롭", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "도안 이미지 경로를 입력하면 마커 안쪽 영역만 크롭하여 베이스 이미지를 생성합니다.\n" +
            "생성된 이미지는 스캔 시 색상 매핑에 정확히 맞습니다.",
            MessageType.Info);

        GUILayout.Space(10);

        doanImagePath = EditorGUILayout.TextField("도안 이미지 경로", doanImagePath);
        outputFileName = EditorGUILayout.TextField("출력 파일명", outputFileName);

        GUILayout.Space(5);

        // 파일 존재 확인
        bool fileExists = File.Exists(doanImagePath);
        if (!fileExists)
        {
            EditorGUILayout.HelpBox($"파일을 찾을 수 없습니다: {doanImagePath}", MessageType.Warning);
        }

        GUILayout.Space(10);

        EditorGUI.BeginDisabledGroup(!fileExists);
        if (GUILayout.Button("마커 감지 & 크롭", GUILayout.Height(30)))
        {
            CropAndSave();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(20);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // 빠른 실행 버튼들
        GUILayout.Label("빠른 실행", EditorStyles.boldLabel);

        if (GUILayout.Button("꾸구리도안.png → 꾸구리_base.png"))
        {
            doanImagePath = "Assets/05_Images/Character/꾸구리도안.png";
            outputFileName = "꾸구리_base";
            if (File.Exists(doanImagePath))
                CropAndSave();
            else
                EditorUtility.DisplayDialog("오류", $"파일 없음: {doanImagePath}", "확인");
        }
    }

    private void CropAndSave()
    {
        // 1. 이미지 로드 (File.ReadAllBytes 사용)
        byte[] imageData = File.ReadAllBytes(doanImagePath);
        Texture2D doanTexture = new Texture2D(2, 2);
        if (!doanTexture.LoadImage(imageData))
        {
            EditorUtility.DisplayDialog("실패", "이미지를 로드할 수 없습니다.", "확인");
            return;
        }

        Debug.Log($"도안 이미지 로드: {doanTexture.width} x {doanTexture.height}");

        // 2. 마커 감지
        var detection = DetectMarkers(doanTexture);

        if (!detection.success)
        {
            EditorUtility.DisplayDialog("실패",
                "마커를 감지하지 못했습니다.\n" +
                "도안에 4개의 코너 마커(■)가 있는지 확인해주세요.\n\n" +
                "콘솔 로그를 확인하세요.", "확인");
            DestroyImmediate(doanTexture);
            return;
        }

        Debug.Log($"마커 감지 성공!\n" +
            $"BL(좌하): {detection.bottomLeft}\n" +
            $"BR(우하): {detection.bottomRight}\n" +
            $"TL(좌상): {detection.topLeft}\n" +
            $"TR(우상): {detection.topRight}\n" +
            $"크롭 영역: {detection.sketchBounds}");

        // 3. 크롭
        Texture2D cropped = CropTexture(doanTexture, detection.sketchBounds);
        DestroyImmediate(doanTexture);

        if (cropped == null)
        {
            EditorUtility.DisplayDialog("실패", "크롭에 실패했습니다.", "확인");
            return;
        }

        // 4. PNG로 저장
        string directory = Path.GetDirectoryName(doanImagePath);
        string outputPath = Path.Combine(directory, outputFileName + ".png");

        byte[] pngData = cropped.EncodeToPNG();
        File.WriteAllBytes(outputPath, pngData);
        DestroyImmediate(cropped);

        AssetDatabase.Refresh();

        // 생성된 텍스처의 설정 자동 변경
        TextureImporter importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        Debug.Log($"베이스 이미지 저장 완료: {outputPath}");
        EditorUtility.DisplayDialog("성공",
            $"베이스 이미지가 생성되었습니다!\n\n" +
            $"경로: {outputPath}\n\n" +
            "이 이미지를 AnimalModelManager의\n" +
            "꾸구리 항목의 baseSprite로 설정하세요.",
            "확인");

        // 생성된 파일 선택
        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }

    private struct MarkerDetectionResult
    {
        public bool success;
        public Vector2 topLeft, topRight, bottomLeft, bottomRight;
        public Rect sketchBounds;
    }

    private MarkerDetectionResult DetectMarkers(Texture2D texture)
    {
        var result = new MarkerDetectionResult { success = false };

        int w = texture.width;
        int h = texture.height;
        var pixels = texture.GetPixels();

        // 검은 픽셀 맵 생성
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
        Debug.Log($"[CropTool] 이미지: {w}x{h}, 검은픽셀: {totalBlack}, 윈도우: {windowSize}px");

        // 4분할 검색
        int midX = w / 2;
        int botMaxY = Mathf.RoundToInt(h * 0.55f);
        int topMinY = Mathf.RoundToInt(h * 0.35f);
        int topMaxY = Mathf.RoundToInt(h * 0.92f);

        var bl = FindDensestCluster(isBlack, w, h, 0, 0, midX, botMaxY, windowSize, "BL");
        var br = FindDensestCluster(isBlack, w, h, midX, 0, w - midX, botMaxY, windowSize, "BR");
        var tl = FindDensestCluster(isBlack, w, h, 0, topMinY, midX, topMaxY - topMinY, windowSize, "TL");
        var tr = FindDensestCluster(isBlack, w, h, midX, topMinY, w - midX, topMaxY - topMinY, windowSize, "TR");

        Debug.Log($"[CropTool] 마커 좌표(픽셀): BL={bl}, BR={br}, TL={tl}, TR={tr}");

        if (bl.x < 0 || br.x < 0 || tl.x < 0 || tr.x < 0)
        {
            if (bl.x < 0) Debug.LogWarning("[CropTool] BL(좌하) 마커 미발견");
            if (br.x < 0) Debug.LogWarning("[CropTool] BR(우하) 마커 미발견");
            if (tl.x < 0) Debug.LogWarning("[CropTool] TL(좌상) 마커 미발견");
            if (tr.x < 0) Debug.LogWarning("[CropTool] TR(우상) 마커 미발견");
            return result;
        }

        // normalized 좌표
        Vector2 nBL = new Vector2(bl.x / (float)w, bl.y / (float)h);
        Vector2 nBR = new Vector2(br.x / (float)w, br.y / (float)h);
        Vector2 nTL = new Vector2(tl.x / (float)w, tl.y / (float)h);
        Vector2 nTR = new Vector2(tr.x / (float)w, tr.y / (float)h);

        result.success = true;
        result.bottomLeft = nBL;
        result.bottomRight = nBR;
        result.topLeft = nTL;
        result.topRight = nTR;

        // 크롭 영역 계산 (마커 안쪽)
        float left = Mathf.Max(nBL.x, nTL.x) + MARKER_OFFSET;
        float right = Mathf.Min(nBR.x, nTR.x) - MARKER_OFFSET;
        float bottom = Mathf.Max(nBL.y, nBR.y) + MARKER_OFFSET;
        float top = Mathf.Min(nTL.y, nTR.y) - MARKER_OFFSET;

        result.sketchBounds = new Rect(left, bottom, right - left, top - bottom);

        return result;
    }

    private Vector2Int FindDensestCluster(bool[] isBlack, int texW, int texH,
        int startX, int startY, int searchW, int searchH, int windowSize, string debugName)
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
            Debug.Log($"[CropTool] {debugName}: 검은픽셀 {areaBlack}개 부족 (최소 {MIN_MARKER_PIXELS})");
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
                        if (isBlack[rowIdx + dx])
                            blackCount++;
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

        Debug.Log($"[CropTool] {debugName}: 검은픽셀={areaBlack}, 최대밀도={bestDensity:F2}");

        if (bestDensity < MIN_DENSITY)
            return new Vector2Int(-1, -1);

        // 무게중심 계산
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

    private Texture2D CropTexture(Texture2D source, Rect normalizedBounds)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.x * source.width), 0, source.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.y * source.height), 0, source.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.width * source.width), 1, source.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(normalizedBounds.height * source.height), 1, source.height - y);

        Debug.Log($"[CropTool] 크롭: ({x}, {y}) ~ ({x+w}, {y+h}), 크기: {w}x{h}");

        var pixels = source.GetPixels(x, y, w, h);
        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        return cropped;
    }
}
