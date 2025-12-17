using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

public class ScanFolderWatcher : MonoBehaviour
{
    [Header("Debug Preview (Optional)")]
    [Tooltip("비워두면 이벤트로만 전달됩니다. 넣어두면 스캔 결과를 미리보기로 붙여줍니다.")]
    [SerializeField] private Renderer debugRenderer;

    [Header("Scan Folder (Optional)")]
    [Tooltip("비워두면 기본값: C:\\ProgramData\\LiveSketch\\Scans")]
    [SerializeField] private string overrideScanFolder = "";

    [Header("Read Retry")]
    [SerializeField] private int readRetryCount = 60;
    [SerializeField] private int readRetryDelayMs = 100;

    /// <summary>
    /// 새 스캔 이미지가 준비되면 호출됨. (Texture2D, filePath)
    /// </summary>
    public event Action<Texture2D, string> OnScanTextureReady;

    public string ScanFolderPath => _scanFolder;

    private string _scanFolder;
    private FileSystemWatcher _watcher;
    private readonly ConcurrentQueue<string> _pending = new();

    private Texture2D _lastTexture;

    private void Awake()
    {
        _scanFolder = ResolveScanFolder();
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

    private string ResolveScanFolder()
    {
        if (!string.IsNullOrWhiteSpace(overrideScanFolder))
            return overrideScanFolder;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LiveSketch", "Scans"
        );
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsJpeg(e.FullPath)) return;
        _pending.Enqueue(e.FullPath);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        if (!IsJpeg(e.FullPath)) return;
        _pending.Enqueue(e.FullPath);
    }

    private void Update()
    {
        // 여러 이벤트가 한 번에 오면 "마지막(최신)"만 처리
        string lastPath = null;
        while (_pending.TryDequeue(out var path))
            lastPath = path;

        if (string.IsNullOrEmpty(lastPath)) return;

        var bytes = TryReadAllBytes(lastPath, readRetryCount, readRetryDelayMs);
        if (bytes == null) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return;
        }

        tex.wrapMode = TextureWrapMode.Clamp;

        if (_lastTexture != null)
            Destroy(_lastTexture);
        _lastTexture = tex;

        // 1) 확장 포인트: 이벤트
        OnScanTextureReady?.Invoke(tex, lastPath);

        // 2) 디버그 미리보기(있으면)
        if (debugRenderer != null)
            debugRenderer.material.mainTexture = tex;
    }

    private bool IsJpeg(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg";
    }

    private byte[] TryReadAllBytes(string path, int retry, int delayMs)
    {
        for (int i = 0; i < retry; i++)
        {
            try { return File.ReadAllBytes(path); }
            catch (IOException) { Thread.Sleep(delayMs); }
            catch (UnauthorizedAccessException) { Thread.Sleep(delayMs); }
        }
        return null;
    }

    // 필요하면 외부에서 미리보기 렌더러를 바꿀 수 있게
    public void SetDebugRenderer(Renderer renderer) => debugRenderer = renderer;
}
