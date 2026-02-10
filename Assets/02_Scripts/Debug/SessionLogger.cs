using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 세션 로그 파일 저장
/// - 프로그램 시작 시 로그 파일 생성
/// - 모든 Debug.Log 메시지 저장
/// - 프로그램 종료 시 삭제 (옵션)
/// </summary>
public class SessionLogger : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("로그 파일 경로 (비워두면 프로젝트 루트)")]
    [SerializeField] private string logFilePath = "";

    [Tooltip("프로그램 종료 시 로그 파일 삭제")]
    [SerializeField] private bool deleteOnQuit = false;

    [Tooltip("저장할 로그 타입")]
    [SerializeField] private bool logMessages = true;
    [SerializeField] private bool logWarnings = true;
    [SerializeField] private bool logErrors = true;

    private string _fullPath;
    private StreamWriter _writer;

    private void Awake()
    {
        // 싱글톤 패턴 (선택적)
        DontDestroyOnLoad(gameObject);

        // 파일 경로 설정
        if (string.IsNullOrEmpty(logFilePath))
        {
            // 프로젝트 루트에 저장
            _fullPath = Path.Combine(Application.dataPath, "..", "SessionLog.txt");
        }
        else
        {
            _fullPath = logFilePath;
        }

        _fullPath = Path.GetFullPath(_fullPath);

        try
        {
            // 기존 파일 삭제 후 새로 생성
            if (File.Exists(_fullPath))
                File.Delete(_fullPath);

            _writer = new StreamWriter(_fullPath, false, System.Text.Encoding.UTF8);
            _writer.AutoFlush = true;

            WriteHeader();
            Debug.Log($"[SessionLogger] 로그 파일 생성: {_fullPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionLogger] 파일 생성 실패: {e.Message}");
        }
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void OnApplicationQuit()
    {
        WriteFooter();
        CloseWriter();

        if (deleteOnQuit && File.Exists(_fullPath))
        {
            try
            {
                File.Delete(_fullPath);
            }
            catch { }
        }
    }

    private void OnDestroy()
    {
        CloseWriter();
    }

    private void WriteHeader()
    {
        if (_writer == null) return;

        _writer.WriteLine("========================================");
        _writer.WriteLine($"  LiveSketch Session Log");
        _writer.WriteLine($"  시작: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _writer.WriteLine("========================================");
        _writer.WriteLine();
    }

    private void WriteFooter()
    {
        if (_writer == null) return;

        _writer.WriteLine();
        _writer.WriteLine("========================================");
        _writer.WriteLine($"  종료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _writer.WriteLine("========================================");
    }

    private void HandleLog(string message, string stackTrace, LogType type)
    {
        if (_writer == null) return;

        // 필터링
        if (type == LogType.Log && !logMessages) return;
        if (type == LogType.Warning && !logWarnings) return;
        if ((type == LogType.Error || type == LogType.Exception) && !logErrors) return;

        string prefix = type switch
        {
            LogType.Warning => "[WARN]",
            LogType.Error => "[ERROR]",
            LogType.Exception => "[EXCEPTION]",
            _ => "[LOG]"
        };

        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        try
        {
            _writer.WriteLine($"{timestamp} {prefix} {message}");

            // 에러/예외는 스택트레이스도 저장
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
            {
                _writer.WriteLine($"  StackTrace: {stackTrace}");
            }
        }
        catch { }
    }

    private void CloseWriter()
    {
        if (_writer != null)
        {
            try
            {
                _writer.Close();
                _writer.Dispose();
            }
            catch { }
            _writer = null;
        }
    }

    /// <summary>
    /// 현재 로그 파일 경로 반환
    /// </summary>
    public string GetLogFilePath() => _fullPath;
}
