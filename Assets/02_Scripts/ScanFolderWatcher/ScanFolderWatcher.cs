using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// 지정 폴더에서 스캔된 JPG 파일을 감시하고
/// 새 파일이 감지되면 텍스처로 로드하여 이벤트로 전달합니다.
///
/// 비정상 이미지(잘린 이미지 등)가 감지되면 정상 이미지를 기다립니다.
/// </summary>
public class ScanFolderWatcher : MonoBehaviour
{
    [Header("스캔 폴더")]
    [Tooltip("비워두면 기본값: C:\\ProgramData\\LiveSketch\\Scans")]
    [SerializeField] private string scanFolderPath = "";

    [Header("파일 읽기 설정")]
    [Tooltip("파일 읽기 재시도 횟수")]
    [SerializeField] private int readRetryCount = 10;

    [Tooltip("재시도 간격 (ms)")]
    [SerializeField] private int readRetryDelayMs = 50;

    [Header("이미지 필터링")]
    [Tooltip("최소 이미지 너비 (픽셀) - 이보다 작으면 대기")]
    [SerializeField] private int minImageWidth = 800;

    [Tooltip("최소 이미지 높이 (픽셀) - 이보다 작으면 대기")]
    [SerializeField] private int minImageHeight = 600;

    [Tooltip("최소 가로세로 비율 (width/height) - 세로 활동지는 약 0.7~0.8")]
    [SerializeField] private float minAspectRatio = 0.6f;

    [Tooltip("최대 가로세로 비율 - 너무 길쭉하면 대기")]
    [SerializeField] private float maxAspectRatio = 2.0f;

    [Tooltip("비정상 이미지 후 정상 이미지 대기 시간 (초)")]
    [SerializeField] private float waitForValidImageTimeout = 10.0f;

    [Header("오디오")]
    [Tooltip("비정상 이미지 감지 시 재생할 오디오 클립")]
    [SerializeField] private AudioClip invalidImageClip;

    [Tooltip("오디오 재생용 AudioSource (비워두면 자동 생성)")]
    [SerializeField] private AudioSource audioSource;

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

    // 대기 상태 관리
    private bool _waitingForValidImage = false;
    private float _waitStartTime = 0f;
    private string _invalidImagePath = null;

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
        // 대기 타임아웃 체크
        if (_waitingForValidImage)
        {
            if (Time.time - _waitStartTime > waitForValidImageTimeout)
            {
                Debug.LogWarning($"[ScanFolderWatcher] 정상 이미지 대기 타임아웃 ({waitForValidImageTimeout}초) - 스캔 실패");
                _waitingForValidImage = false;
                _invalidImagePath = null;
            }
        }

        // 큐에서 파일 경로 가져오기
        string lastPath = null;
        while (_pendingFiles.TryDequeue(out var path))
            lastPath = path;

        if (string.IsNullOrEmpty(lastPath)) return;

        // 파일 읽기
        var bytes = TryReadFile(lastPath);
        if (bytes == null) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return;
        }

        // 이미지 유효성 검사
        var validationResult = ValidateScanImage(tex, lastPath);

        if (!validationResult.isValid)
        {
            Destroy(tex);

            // 비정상 이미지 → 정상 이미지 대기 시작
            if (!_waitingForValidImage)
            {
                _waitingForValidImage = true;
                _waitStartTime = Time.time;
                _invalidImagePath = lastPath;
                Debug.Log($"[ScanFolderWatcher] 비정상 이미지 감지 - 정상 이미지 대기 중... ({validationResult.reason})");

                // 비정상 이미지 오디오 재생
                if (invalidImageClip != null)
                {
                    if (audioSource == null)
                        audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.PlayOneShot(invalidImageClip);
                }
            }
            return;
        }

        // 정상 이미지 도착!
        if (_waitingForValidImage)
        {
            Debug.Log($"[ScanFolderWatcher] 정상 이미지 수신 완료! (대기 시간: {Time.time - _waitStartTime:F2}초)");
            _waitingForValidImage = false;
            _invalidImagePath = null;
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

    /// <summary>
    /// 스캔 이미지 유효성 검사
    /// </summary>
    private (bool isValid, string reason) ValidateScanImage(Texture2D tex, string path)
    {
        int width = tex.width;
        int height = tex.height;
        float aspectRatio = (float)width / height;
        string fileName = Path.GetFileName(path);

        // 크기 검사
        if (width < minImageWidth || height < minImageHeight)
        {
            return (false, $"크기 부족: {width}x{height}");
        }

        // 비율 검사 (너무 길쭉하면 잘린 이미지)
        if (aspectRatio < minAspectRatio || aspectRatio > maxAspectRatio)
        {
            return (false, $"비정상 비율: {aspectRatio:F2}");
        }

        Debug.Log($"[ScanFolderWatcher] 이미지 검증 통과: {width}x{height}, 비율: {aspectRatio:F2} - {fileName}");
        return (true, null);
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
