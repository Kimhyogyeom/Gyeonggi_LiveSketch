using System.Collections;
using UnityEngine;

/// <summary>
/// 화면 찢김 이펙트: 화면을 캡처 → 가운데 번개 균열 → 좌반쪽 위로 / 우반쪽 아래로 사라짐.
/// ScreenGlitchTear.Play()로 호출.
/// </summary>
public class ScreenGlitchTear : MonoBehaviour
{
    public static void Play()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var go = new GameObject("ScreenTear");
        var sgt = go.AddComponent<ScreenGlitchTear>();
        sgt.StartCoroutine(sgt.TearSequence(cam));
    }

    IEnumerator TearSequence(Camera cam)
    {
        // ========================================
        // 1. 화면 캡처
        // ========================================
        yield return new WaitForEndOfFrame();

        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        tex.Apply();

        // ========================================
        // 2. 좌우 분할 스프라이트
        // ========================================
        float ppu = tex.height / (cam.orthographicSize * 2f);
        int halfW = tex.width / 2;

        // 좌측: pivot을 오른쪽 가장자리에 → 화면 중앙에 배치하면 왼쪽으로 펼쳐짐
        var leftSprite = Sprite.Create(tex,
            new Rect(0, 0, halfW, tex.height),
            new Vector2(1f, 0.5f), ppu);
        // 우측: pivot을 왼쪽 가장자리에 → 화면 중앙에 배치하면 오른쪽으로 펼쳐짐
        var rightSprite = Sprite.Create(tex,
            new Rect(halfW, 0, halfW, tex.height),
            new Vector2(0f, 0.5f), ppu);

        float z = cam.transform.position.z + cam.nearClipPlane + 0.5f;
        float cx = cam.transform.position.x;
        float cy = cam.transform.position.y;

        var leftGO = CreateHalf("TearLeft", leftSprite, cx, cy, z, 960);
        var leftSR = leftGO.GetComponent<SpriteRenderer>();
        var rightGO = CreateHalf("TearRight", rightSprite, cx, cy, z, 960);
        var rightSR = rightGO.GetComponent<SpriteRenderer>();

        // ========================================
        // 3. 번개 균열선 (가운데 지그재그)
        // ========================================
        var crackLR = CreateLightningCrack(cx, cy, z - 0.01f, cam.orthographicSize);

        // 균열 잠깐 보여주기
        yield return new WaitForSeconds(0.12f);

        // ========================================
        // 4. 좌측 위로, 우측 아래로 슬라이드
        // ========================================
        float duration = 0.5f;
        float elapsed = 0f;
        float moveDistance = cam.orthographicSize * 2.5f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t; // 가속

            leftGO.transform.position = new Vector3(cx, cy + moveDistance * eased, z);
            rightGO.transform.position = new Vector3(cx, cy - moveDistance * eased, z);

            // 페이드 아웃
            float alpha = Mathf.Lerp(1f, 0f, t);
            leftSR.color = new Color(1, 1, 1, alpha);
            rightSR.color = new Color(1, 1, 1, alpha);

            // 균열선 페이드 + 찌지직 깜빡임
            if (crackLR != null)
            {
                bool flicker = Random.value > 0.2f;
                crackLR.enabled = flicker;
                float ca = Mathf.Lerp(1f, 0f, t * 1.5f);
                crackLR.startColor = new Color(1, 1, 1, ca);
                crackLR.endColor = new Color(0.5f, 0.8f, 1f, ca * 0.7f);

                // 경로 재생성 (찌지직)
                if (flicker && Random.value > 0.5f)
                    RegenerateCrackPath(crackLR, cx, cy, cam.orthographicSize);
            }

            yield return null;
        }

        // 정리
        Destroy(tex);
        Destroy(gameObject);
    }

    // ================================================================
    // 반쪽 화면 스프라이트 생성
    // ================================================================
    GameObject CreateHalf(string name, Sprite sprite, float x, float y, float z, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(x, y, z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return go;
    }

    // ================================================================
    // 번개 모양 균열선 (LineRenderer)
    // ================================================================
    LineRenderer CreateLightningCrack(float cx, float cy, float z, float camH)
    {
        var go = new GameObject("Crack");
        go.transform.SetParent(transform);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sortingOrder = 961;
        lr.startWidth = 0.15f;
        lr.endWidth = 0.15f;
        lr.startColor = Color.white;
        lr.endColor = new Color(0.5f, 0.8f, 1f, 0.8f);
        lr.numCapVertices = 2;

        // Sprites/Default 쉐이더 사용
        var sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var mat = new Material(sh);
            mat.color = Color.white;
            lr.material = mat;
        }

        RegenerateCrackPath(lr, cx, cy, camH);
        return lr;
    }

    void RegenerateCrackPath(LineRenderer lr, float cx, float cy, float camH)
    {
        int segments = 20;
        lr.positionCount = segments;

        float top = cy + camH * 1.1f;
        float bottom = cy - camH * 1.1f;
        float z = lr.transform.position.z;

        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            float y = Mathf.Lerp(top, bottom, t);
            // 지그재그: 중앙 부근이 가장 크게 흔들림
            float jag = Mathf.Sin(t * Mathf.PI) * 0.3f;
            float x = cx + Random.Range(-jag, jag);
            lr.SetPosition(i, new Vector3(x, y, z));
        }
    }
}
