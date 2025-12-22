using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// 지정 폴더에서 스캔된 JPG 파일을 감시하고
/// 새 파일이 감지되면 텍스처로 로드하여 이벤트로 전달합니다.
/// </summary>
public class ScanFolderWatcher : MonoBehaviour
{
    [Header("스캔 폴더")]
    [Tooltip("비워두면 기본값: C:\\ProgramData\\LiveSketch\\Scans")]
    [SerializeField] private string scanFolderPath = "";

    [Header("파일 읽기 설정")]
    [Tooltip("파일 읽기 재시도 횟수")]
    [SerializeField] private int readRetryCount = 60;

    [Tooltip("재시도 간격 (ms)")]
    [SerializeField] private int readRetryDelayMs = 100;

    [Header("디버그 (선택)")]
    [SerializeField] private Renderer previewRenderer;

    /// <summary>
    /// 새 스캔 이미지 준비 시 호출
    /// </summary>
    public event Action<Texture2D, string> OnScanTextureReady;

    public string FolderPath => _scanFolder;

    private string _scanFolder;
    private FileSystemWatcher _watcher;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private Texture2D _lastTexture;

    private void Awake()
    {
        _scanFolder = GetScanFolder();
        Directory.CreateDirectory(_scanFolder);

        _watcher = new FileSystemWatcher(_scanFolder, "*.*")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnRenamedEvent;
        _watcher.EnableRaisingEvents = true;

        Debug.Log($"[ScanFolderWatcher] 감시 시작: {_scanFolder}");
    }

    private void OnDestroy()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileEvent;
            _watcher.Changed -= OnFileEvent;
            _watcher.Renamed -= OnRenamedEvent;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_lastTexture != null)
        {
            Destroy(_lastTexture);
            _lastTexture = null;
        }
    }

    private string GetScanFolder()
    {
        if (!string.IsNullOrWhiteSpace(scanFolderPath))
            return scanFolderPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LiveSketch", "Scans"
        );
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsJpeg(e.FullPath))
            _pendingFiles.Enqueue(e.FullPath);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        if (IsJpeg(e.FullPath))
            _pendingFiles.Enqueue(e.FullPath);
    }

    private void Update()
    {
        // 여러 이벤트 중 마지막(최신)만 처리
        string lastPath = null;
        while (_pendingFiles.TryDequeue(out var path))
            lastPath = path;

        if (string.IsNullOrEmpty(lastPath)) return;

        var bytes = TryReadFile(lastPath);
        if (bytes == null) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return;
        }

        tex.wrapMode = TextureWrapMode.Clamp;

        // 이전 텍스처 정리
        if (_lastTexture != null)
            Destroy(_lastTexture);
        _lastTexture = tex;

        // 이벤트 발행
        OnScanTextureReady?.Invoke(tex, lastPath);

        // 미리보기
        if (previewRenderer != null)
            previewRenderer.material.mainTexture = tex;
    }

    private bool IsJpeg(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg";
    }

    private byte[] TryReadFile(string path)
    {
        for (int i = 0; i < readRetryCount; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                Thread.Sleep(readRetryDelayMs);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(readRetryDelayMs);
            }
        }

        Debug.LogWarning($"[ScanFolderWatcher] 파일 읽기 실패: {path}");
        return null;
    }
}
