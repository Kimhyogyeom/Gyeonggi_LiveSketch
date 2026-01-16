using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 스캔 도안에서 테두리/윤곽선을 제거하고 컬러만 추출합니다.
/// 테두리 밖으로 벗어난 색칠도 무시합니다.
/// </summary>
public static class ColorExtractor
{
    // 검은 테두리 완전 제거 + 원본 컬러 유지
    private const float SATURATION_THRESHOLD = 0.06f;       // 채도 임계값 (무채색만 제거)
    private const float DARK_THRESHOLD = 0.60f;             // 검은색 제거 (0.55 → 0.60, 더 강하게!)
    private const float BRIGHT_THRESHOLD = 0.97f;           // 밝은 색 제거 (거의 흰색만)
    private const float BORDER_DARKNESS_THRESHOLD = 0.50f;  // 테두리 감지

    /// <summary>
    /// 테두리 밖 색칠을 제거하고 컬러만 추출
    /// </summary>
    public static Texture2D ExtractColorsWithBorderMask(Texture2D source, bool removeOutside = true)
    {
        if (source == null) return null;

        int width = source.width;
        int height = source.height;
        var pixels = source.GetPixels32();

        bool[] outsideMask = removeOutside ? CreateOutsideMask(source.GetPixels(), width, height) : null;

        for (int i = 0; i < pixels.Length; i++)
        {
            // 테두리 밖 영역 제거
            if (outsideMask != null && outsideMask[i])
            {
                pixels[i] = new Color32(255, 255, 255, 255);
                continue;
            }

            Color color = pixels[i];
            Color.RGBToHSV(color, out float h, out float s, out float v);

            // 1. 거의 흰색 배경만 제거
            if (v > BRIGHT_THRESHOLD && s < 0.10f)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
                continue;
            }

            // 2. 검은색 테두리 제거 (핵심!)
            // 채도 낮고(s < 0.35) 어두우면(v < 0.60) → 검은 테두리 → 제거
            // 채도 있으면 진한 색칠로 판단 → 유지
            if (v < DARK_THRESHOLD && s < 0.35f)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
                continue;
            }

            // 3. 순수 무채색(회색)만 제거
            if (s < SATURATION_THRESHOLD && v > 0.3f && v < 0.90f)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            // 컬러는 원본 그대로 유지! (변환 없음)
        }

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(pixels);
        result.Apply(false, false);
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    /// <summary>
    /// 기본 컬러 추출 (테두리 밖 제거 없음)
    /// </summary>
    public static Texture2D ExtractColorsOnly(Texture2D source)
    {
        return ExtractColorsWithBorderMask(source, false);
    }

    private static bool[] CreateOutsideMask(Color[] pixels, int width, int height)
    {
        bool[] outside = new bool[pixels.Length];
        bool[] visited = new bool[pixels.Length];
        var queue = new Queue<int>();

        // 가장자리에서 시작
        for (int x = 0; x < width; x++)
        {
            TryEnqueue(queue, visited, pixels, (height - 1) * width + x);
            TryEnqueue(queue, visited, pixels, x);
        }
        for (int y = 0; y < height; y++)
        {
            TryEnqueue(queue, visited, pixels, y * width);
            TryEnqueue(queue, visited, pixels, y * width + width - 1);
        }

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            outside[idx] = true;

            int x = idx % width, y = idx / width;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d], ny = y + dy[d];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                int nIdx = ny * width + nx;
                if (visited[nIdx] || IsBorder(pixels[nIdx])) continue;

                visited[nIdx] = true;
                queue.Enqueue(nIdx);
            }
        }
        return outside;
    }

    private static void TryEnqueue(Queue<int> q, bool[] visited, Color[] pixels, int idx)
    {
        if (!visited[idx] && !IsBorder(pixels[idx]))
        {
            q.Enqueue(idx);
            visited[idx] = true;
        }
    }

    private static bool IsBorder(Color c)
    {
        return (c.r + c.g + c.b) / 3f < BORDER_DARKNESS_THRESHOLD;
    }
}
