using UnityEngine;

public class ScanProcessor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ScanFolderWatcher watcher;

    [Header("Output Preview (optional)")]
    [SerializeField] private Renderer previewRenderer; // Plane/Quad 미리보기

    [Header("Apply To Model (optional)")]
    [SerializeField] private Renderer targetModelRenderer; // 캡슐(또는 임포트 모델)
    [SerializeField] private string textureProperty = "_MainTex"; // 트라이플래너 셰이더가 쓰는 텍스처 슬롯

    [Header("Fixed Crop (normalized 0~1)")]
    [Tooltip("0~1 범위. (0,0)=왼쪽아래, (1,1)=오른쪽위")]
    [SerializeField] private Vector2 cropMin = new Vector2(0.0f, 0.0f);
    [SerializeField] private Vector2 cropMax = new Vector2(1.0f, 1.0f);

    private Texture2D _lastCropped;

    private void OnEnable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady += HandleScan;
    }

    private void OnDisable()
    {
        if (watcher != null)
            watcher.OnScanTextureReady -= HandleScan;

        if (_lastCropped != null)
        {
            Destroy(_lastCropped);
            _lastCropped = null;
        }
    }

    private void HandleScan(Texture2D tex, string path)
    {
        if (tex == null) return;

        // 1) 고정 크롭 좌표 계산(정규화 -> 픽셀)
        int xMin = Mathf.Clamp(Mathf.RoundToInt(cropMin.x * tex.width), 0, tex.width - 1);
        int yMin = Mathf.Clamp(Mathf.RoundToInt(cropMin.y * tex.height), 0, tex.height - 1);

        int xMax = Mathf.Clamp(Mathf.RoundToInt(cropMax.x * tex.width), xMin + 1, tex.width);
        int yMax = Mathf.Clamp(Mathf.RoundToInt(cropMax.y * tex.height), yMin + 1, tex.height);

        int w = xMax - xMin;
        int h = yMax - yMin;

        // 2) 픽셀 복사해서 새 텍스처 만들기
        var pixels = tex.GetPixels(xMin, yMin, w, h);

        var cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply(false, false);
        cropped.wrapMode = TextureWrapMode.Clamp;

        // 이전 크롭 텍스처 정리
        if (_lastCropped != null) Destroy(_lastCropped);
        _lastCropped = cropped;

        // 3) 미리보기 출력(Plane/Quad)
        if (previewRenderer != null)
            previewRenderer.material.mainTexture = cropped;

        // 4) 캡슐/모델에 적용 (트라이플래너 머티리얼의 _MainTex에 넣기)
        if (targetModelRenderer != null)
        {
            var mat = targetModelRenderer.material;
            mat.SetTexture(textureProperty, cropped);
        }
    }
}
