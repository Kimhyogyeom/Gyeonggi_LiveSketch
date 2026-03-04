using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BackgroundImage/Use 폴더의 이미지들을 순환하며 크로스페이드 전환.
///
/// 폴더 구조 (자동 생성):
///   exe 옆/BackgroundImage/
///     Resource/   ← 운영자가 이미지를 보관하는 곳
///     Use/        ← 여기에 이미지들을 넣으면 이름순 순환
///     config.txt  ← 전환 시간 설정 (자동 생성)
///
/// config.txt 예시:
///   displayTime=5
///   fadeTime=1.5
/// </summary>
public class BackgroundImageLoader : MonoBehaviour
{
    [Header("=== 기본 설정 (config.txt로 덮어쓰기) ===")]
    [SerializeField] private float displayTime = 5f;
    [SerializeField] private float fadeTime = 1.5f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showLogs = true;

    // A = 뒤(현재 이미지), B = 앞(페이드인 오버레이)
    private RawImage _imageA;
    private RawImage _imageB;
    private Texture2D _textureA;
    private Texture2D _textureB;

    private string _baseFolderPath;
    private string _useFolderPath;
    private string _configPath;

    private List<string> _imageFiles = new List<string>();
    private int _currentIndex = 0;

    void Start()
    {
        _imageA = GetComponent<RawImage>();
        if (_imageA == null)
        {
            Log("RawImage 컴포넌트가 없음! RawImage가 있는 오브젝트에 붙여주세요.");
            enabled = false;
            return;
        }

        CreateOverlay();

        // 폴더 설정
        string basePath = Path.GetDirectoryName(Application.dataPath);
        _baseFolderPath = Path.Combine(basePath, "BackgroundImage");
        _useFolderPath = Path.Combine(_baseFolderPath, "Use");
        _configPath = Path.Combine(_baseFolderPath, "config.txt");

        if (!Directory.Exists(Path.Combine(_baseFolderPath, "Resource")))
            Directory.CreateDirectory(Path.Combine(_baseFolderPath, "Resource"));
        if (!Directory.Exists(_useFolderPath))
            Directory.CreateDirectory(_useFolderPath);

        CreateDefaultConfig();
        LoadConfig();
        RefreshImageList();

        Log($"배경 폴더: {_useFolderPath}");
        Log($"이미지 {_imageFiles.Count}장 발견, displayTime={displayTime}s, fadeTime={fadeTime}s");

        if (_imageFiles.Count > 0)
        {
            _textureA = LoadTexture(_imageFiles[0]);
            _imageA.texture = _textureA;
            _imageA.color = Color.white;
            _imageA.gameObject.SetActive(true);
            _currentIndex = 0;

            StartCoroutine(SlideShowLoop());
        }
        else
        {
            _imageA.texture = null;
            Log("Use 폴더에 이미지 없음");
        }
    }

    void CreateOverlay()
    {
        var go = new GameObject("BackgroundOverlay", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(transform.parent, false);
        go.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);

        // RectTransform 복사
        RectTransform src = GetComponent<RectTransform>();
        RectTransform dst = go.GetComponent<RectTransform>();
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.offsetMin = src.offsetMin;
        dst.offsetMax = src.offsetMax;
        dst.pivot = src.pivot;
        dst.sizeDelta = src.sizeDelta;
        dst.anchoredPosition = src.anchoredPosition;

        _imageB = go.GetComponent<RawImage>();
        _imageB.color = new Color(1, 1, 1, 0);
        _imageB.raycastTarget = false;
    }

    IEnumerator SlideShowLoop()
    {
        while (true)
        {
            // 현재 이미지 표시 대기
            yield return new WaitForSeconds(displayTime);

            // 매 전환마다 설정 & 이미지 목록 새로고침
            LoadConfig();
            RefreshImageList();

            if (_imageFiles.Count <= 1) continue;

            // 다음 이미지 인덱스
            _currentIndex = (_currentIndex + 1) % _imageFiles.Count;

            // 새 텍스처 로드 → B에 세팅
            var newTex = LoadTexture(_imageFiles[_currentIndex]);
            if (newTex == null) continue;

            if (_textureB != null) Destroy(_textureB);
            _textureB = newTex;
            _imageB.texture = _textureB;

            Log($"전환 → {Path.GetFileName(_imageFiles[_currentIndex])} ({_currentIndex + 1}/{_imageFiles.Count})");

            // B를 투명→불투명으로 페이드인
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsed / fadeTime);
                _imageB.color = new Color(1, 1, 1, alpha);
                yield return null;
            }
            _imageB.color = Color.white;

            // 전환 완료: A ← B의 텍스처, B 초기화
            Texture2D oldA = _textureA;
            _textureA = _textureB;
            _textureB = null;

            _imageA.texture = _textureA;
            _imageA.color = Color.white;

            _imageB.texture = null;
            _imageB.color = new Color(1, 1, 1, 0);

            if (oldA != null) Destroy(oldA);
        }
    }

    void RefreshImageList()
    {
        _imageFiles.Clear();
        if (!Directory.Exists(_useFolderPath)) return;

        string[] exts = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
        _imageFiles = exts
            .SelectMany(ext => Directory.GetFiles(_useFolderPath, ext))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    void LoadConfig()
    {
        if (!File.Exists(_configPath)) return;

        try
        {
            foreach (var line in File.ReadAllLines(_configPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim().ToLower();
                string val = parts[1].Trim();

                if (key == "displaytime" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float dt))
                    displayTime = Mathf.Max(0.1f, dt);
                else if (key == "fadetime" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ft))
                    fadeTime = Mathf.Max(0.1f, ft);
            }
        }
        catch (Exception e)
        {
            Log($"config.txt 읽기 실패: {e.Message}");
        }
    }

    void CreateDefaultConfig()
    {
        if (File.Exists(_configPath)) return;

        try
        {
            File.WriteAllText(_configPath,
                "# 배경 슬라이드쇼 설정\n" +
                "# displayTime = 이미지 표시 시간 (초)\n" +
                "# fadeTime = 전환 페이드 시간 (초)\n" +
                "displayTime=5\n" +
                "fadeTime=1.5\n");
            Log("config.txt 기본 생성됨");
        }
        catch (Exception e)
        {
            Log($"config.txt 생성 실패: {e.Message}");
        }
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

    void OnDestroy()
    {
        if (_textureA != null) Destroy(_textureA);
        if (_textureB != null) Destroy(_textureB);
        if (_imageB != null) Destroy(_imageB.gameObject);
    }

    void Log(string msg)
    {
        if (showLogs)
            Debug.Log($"[BackgroundImage] {msg}");
    }
}
