using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 빌드 폴더 옆 BackgroundImage/Use 폴더의 이미지를 배경 RawImage에 적용.
/// 실행 중 이미지 교체 시 실시간 반영.
///
/// 폴더 구조 (자동 생성):
///   exe 옆/BackgroundImage/
///     Resource/   ← 운영자가 이미지를 보관하는 곳 (사용 안 함)
///     Use/        ← 여기에 이미지 1장 넣으면 배경으로 적용
/// </summary>
public class BackgroundImageLoader : MonoBehaviour
{
    [Header("=== 설정 ===")]
    [Tooltip("폴더 변경 감지 주기 (초)")]
    [SerializeField] private float pollInterval = 2f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showLogs = true;

    private RawImage _rawImage;
    private string _useFolderPath;
    private float _pollTimer;
    private string _lastFileHash = "";
    private Texture2D _loadedTexture;

    void Start()
    {
        // 자기 자신의 RawImage 자동 획득
        _rawImage = GetComponent<RawImage>();
        if (_rawImage == null)
        {
            Log("RawImage 컴포넌트가 없음! RawImage가 있는 오브젝트에 붙여주세요.");
            enabled = false;
            return;
        }

        string basePath = Path.GetDirectoryName(Application.dataPath);
        string rootFolder = Path.Combine(basePath, "BackgroundImage");
        string resourceFolder = Path.Combine(rootFolder, "Resource");
        _useFolderPath = Path.Combine(rootFolder, "Use");

        // 폴더 자동 생성
        if (!Directory.Exists(resourceFolder))
            Directory.CreateDirectory(resourceFolder);
        if (!Directory.Exists(_useFolderPath))
            Directory.CreateDirectory(_useFolderPath);

        Log($"배경 폴더: {_useFolderPath}");

        // 초기 로드
        LoadAndApply();
    }

    void Update()
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer < pollInterval) return;
        _pollTimer = 0f;

        string currentHash = GetFileHash();
        if (currentHash != _lastFileHash)
        {
            Log("이미지 변경 감지 → 리로드");
            LoadAndApply();
        }
    }

    void LoadAndApply()
    {
        if (_rawImage == null)
        {
            Log("RawImage가 지정되지 않음");
            return;
        }

        // Use 폴더에서 이미지 1장 찾기
        string file = FindImageFile();
        _lastFileHash = GetFileHash();

        // 기존 텍스처 해제
        if (_loadedTexture != null)
        {
            Destroy(_loadedTexture);
            _loadedTexture = null;
        }

        if (file == null)
        {
            // 이미지 없으면 빈 배경 (텍스처 제거)
            _rawImage.texture = null;
            Log("Use 폴더에 이미지 없음 → 빈 배경");
            return;
        }

        // 로드
        _loadedTexture = LoadTexture(file);
        if (_loadedTexture != null)
        {
            _rawImage.texture = _loadedTexture;
            _rawImage.gameObject.SetActive(true);
            Log($"배경 적용: {Path.GetFileName(file)} ({_loadedTexture.width}x{_loadedTexture.height})");
        }
    }

    string FindImageFile()
    {
        if (!Directory.Exists(_useFolderPath)) return null;

        string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        var files = extensions
            .SelectMany(ext => Directory.GetFiles(_useFolderPath, ext))
            .ToArray();

        if (files.Length == 0) return null;
        if (files.Length > 1)
            Log($"Use 폴더에 이미지가 {files.Length}장 있음 - 1장만 넣어주세요. 첫 번째 파일 사용.");

        return files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).First();
    }

    Texture2D LoadTexture(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(data))
            {
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            Destroy(tex);
        }
        catch (Exception e)
        {
            Log($"로드 실패: {Path.GetFileName(path)} - {e.Message}");
        }
        return null;
    }

    string GetFileHash()
    {
        if (!Directory.Exists(_useFolderPath)) return "";

        string[] extensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        var files = extensions
            .SelectMany(ext => Directory.GetFiles(_useFolderPath, ext))
            .OrderBy(f => f);

        var sb = new System.Text.StringBuilder();
        foreach (var f in files)
        {
            sb.Append(Path.GetFileName(f));
            sb.Append(File.GetLastWriteTimeUtc(f).Ticks);
        }
        return sb.ToString();
    }

    void OnDestroy()
    {
        if (_loadedTexture != null)
            Destroy(_loadedTexture);
    }

    void Log(string msg)
    {
        if (showLogs)
            Debug.Log($"[BackgroundImage] {msg}");
    }
}
