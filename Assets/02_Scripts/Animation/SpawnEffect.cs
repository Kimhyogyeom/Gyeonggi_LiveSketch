using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

/// <summary>
/// 모델 스폰 이펙트: 에너지 응축(2초) → 팡! 대폭발 + 스케일 팝
///
/// 연출 흐름:
///   1. 에너지 응축: 대량 빛 파티클이 회전하며 중심으로 모이고 점점 빨라짐
///      + 별 궤적 파티클 추가
///   2. 안내 텍스트: 파도 + 깜빡이 + 무지개 색상 효과
///   3. 팡! 폭발: 대량 파티클 + 이중 충격파 + 컨페티 + 플래시
///   4. 모델 등장: 통통 튀는 스케일업 애니메이션
/// </summary>
public class SpawnEffect : MonoBehaviour
{
    private Vector3 _targetScale;
    private float _gatherDuration;
    private float _scaleUpDuration;
    private Color _color;
    private string _characterName;
    private Material _particleMat;    // 응축 단계용
    private Material _burstMat;       // 폭발 단계용
    private Material _additiveMat;
    private Material _customMat;
    private Material _customBurstMat;
    private bool _hasCustomMat;
    private bool _hasCustomBurstMat;
    private float _range = 1f;        // 파티클 범위 배율

    // 생성된 이펙트 오브젝트들 (정리용)
    private GameObject _fxRoot;
    private TMP_Text _announceText;
    private ParticleSystemRenderer[] _cachedRenderers;

    // 텍스트 애니메이션 상태
    private bool _animatingText;
    private float _textAnimTime;

    public void Play(Vector3 targetScale, float gatherDuration, float scaleUpDuration, Color color, string characterName = "", TMP_Text announceText = null, Material customMaterial = null, Material burstMaterial = null, float range = 1f)
    {
        _targetScale = targetScale;
        _gatherDuration = gatherDuration;
        _scaleUpDuration = scaleUpDuration;
        _color = color;
        _characterName = characterName;
        _range = range;

        // 안내 텍스트 (인스펙터에서 연결된 외부 오브젝트)
        _announceText = announceText;

        // 응축 단계 커스텀 머티리얼
        _hasCustomMat = customMaterial != null;
        _customMat = _hasCustomMat ? new Material(customMaterial) : null;

        // 폭발 단계 커스텀 머티리얼 (없으면 응축 머티리얼 공유)
        _hasCustomBurstMat = burstMaterial != null;
        _customBurstMat = _hasCustomBurstMat ? new Material(burstMaterial) : null;

        // 머티리얼 설정
        _particleMat = _hasCustomMat ? _customMat : CreateDefaultAdditiveMaterial();
        _burstMat = _hasCustomBurstMat ? _customBurstMat : _particleMat;
        _additiveMat = CreateDefaultAdditiveMaterial();

        // 이펙트 루트 (씬 루트에 배치 - 모델 스케일 0의 영향 안 받음)
        _fxRoot = new GameObject("SpawnFX_Root");
        _fxRoot.transform.position = transform.position + new Vector3(0f, 0f, -2f);
        _fxRoot.transform.localScale = Vector3.one;

        // URP 렌더링 직전 콜백 등록 (컬링 전에 bounds 강제 확장)
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

        // 모델 숨기기
        transform.localScale = Vector3.zero;

        StartCoroutine(SpawnSequence());
    }

    IEnumerator SpawnSequence()
    {
        // ============================================
        // Phase 1: 에너지 응축 (서서히 강렬해짐)
        // ============================================
        var gatherPS = CreateGatherSpiral();
        var corePS = CreateEnergyCore();
        var starTrailPS = CreateStarTrail();
        var outerRingPS = CreateOuterGatherRing();

        gatherPS.Play();
        corePS.Play();
        starTrailPS.Play();
        outerRingPS.Play();

        // 렌더러 캐시 갱신
        _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        // 안내 텍스트 활성화
        if (_announceText != null && !string.IsNullOrEmpty(_characterName))
        {
            string particle = HasBatchim(_characterName) ? "이" : "가";
            _announceText.text = $"{_characterName}{particle} 나타날 준비를 하고 있어요~!";
            _announceText.alpha = 0f;
            _announceText.gameObject.SetActive(true);
            _animatingText = true;
            _textAnimTime = 0f;
        }

        // 응축 중 점점 강렬해지는 연출
        float gatherElapsed = 0f;
        var gatherEmission = gatherPS.emission;

        while (gatherElapsed < _gatherDuration)
        {
            gatherElapsed += Time.deltaTime;
            float progress = gatherElapsed / _gatherDuration;

            // 시간 갈수록: 파티클 더 많이, 더 빠르게
            float intensity = Mathf.Lerp(0.3f, 1f, progress * progress);
            gatherEmission.rateOverTime = Mathf.Lerp(80f, 600f, intensity);

            // 텍스트 파도 + 깜빡이 애니메이션
            if (_animatingText && _announceText != null)
            {
                _textAnimTime += Time.deltaTime;

                float textAlpha = Mathf.Clamp01(gatherElapsed / 0.5f);
                if (gatherElapsed > _gatherDuration - 0.3f)
                    textAlpha *= Mathf.Clamp01((_gatherDuration - gatherElapsed) / 0.3f);

                AnimateTextWaveAndBlink(textAlpha);
            }

            yield return null;
        }

        gatherPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        corePS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        starTrailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        outerRingPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // 텍스트 비활성화 및 메시 복구
        _animatingText = false;
        if (_announceText != null)
        {
            _announceText.alpha = 1f;
            _announceText.ForceMeshUpdate();
            _announceText.gameObject.SetActive(false);
        }

        // ============================================
        // Phase 2: 팡!!! 대폭발
        // ============================================

        // 큰 플래시 (화면 번쩍)
        var flashPS = CreateFlashGlow();
        flashPS.Emit(3);

        // 메인 폭발 (큰 파티클)
        var burstPS = CreateMainBurst();
        burstPS.Emit(200);

        // 이중 충격파 링
        var ringPS1 = CreateShockwaveRing(12f, 0.6f);
        ringPS1.Emit(1);

        // 컨페티 (알록달록 종이 조각)
        var confettiPS = CreateConfettiBurst();
        confettiPS.Emit(150);

        // 반짝이 샤워 (작은 별들)
        var sparklePS = CreateSparkleShower();
        sparklePS.Emit(120);

        // 렌더러 캐시 갱신 (폭발 파티클 추가됨)
        _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        // 약간 딜레이된 2번째 충격파
        yield return new WaitForSeconds(0.1f);
        var ringPS2 = CreateShockwaveRing(8f, 0.8f);
        ringPS2.Emit(1);

        // 2차 미니 폭발 (여운)
        yield return new WaitForSeconds(0.15f);
        var miniBurstPS = CreateMiniBurst();
        miniBurstPS.Emit(60);

        // ============================================
        // Phase 3: 모델 등장 (탄력 있는 팝!)
        // ============================================
        float elapsed = 0f;
        while (elapsed < _scaleUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _scaleUpDuration);
            float s = EaseOutElastic(t);
            transform.localScale = _targetScale * s;
            yield return null;
        }
        transform.localScale = _targetScale;

        // 파티클 자연 소멸 대기
        yield return new WaitForSeconds(3f);

        // 정리
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        _cachedRenderers = null;
        if (_fxRoot != null) Destroy(_fxRoot);
        if (_customMat != null) Destroy(_customMat);
        if (_customBurstMat != null) Destroy(_customBurstMat);
        if (!_hasCustomMat && _particleMat != null) Destroy(_particleMat);
        if (_additiveMat != null) Destroy(_additiveMat);
        Destroy(this);
    }

    // ================================================================
    // 텍스트 파도 + 깜빡이 + 무지개 애니메이션
    // ================================================================
    void AnimateTextWaveAndBlink(float globalAlpha)
    {
        if (_announceText == null) return;

        _announceText.ForceMeshUpdate();
        TMP_TextInfo textInfo = _announceText.textInfo;

        if (textInfo == null || textInfo.characterCount == 0) return;

        int charCount = textInfo.characterCount;

        for (int i = 0; i < charCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int matIdx = charInfo.materialReferenceIndex;
            int vertIdx = charInfo.vertexIndex;

            Vector3[] vertices = textInfo.meshInfo[matIdx].vertices;
            Color32[] colors = textInfo.meshInfo[matIdx].colors32;

            // --- 파도 효과: 사인파로 Y 오프셋 ---
            float waveOffset = Mathf.Sin(_textAnimTime * 4f + i * 0.5f) * 8f;

            // 약간의 X 흔들림도 추가
            float xWobble = Mathf.Sin(_textAnimTime * 3f + i * 0.7f) * 2f;

            for (int v = 0; v < 4; v++)
            {
                vertices[vertIdx + v] += new Vector3(xWobble, waveOffset, 0);
            }

            // --- 깜빡이 + 무지개 색상 ---
            float blinkPhase = Mathf.Sin(_textAnimTime * 6f + i * 0.8f);
            float charAlpha = globalAlpha * Mathf.Lerp(0.4f, 1f, (blinkPhase + 1f) * 0.5f);

            // 무지개 색상: 캐릭터별 hue 오프셋
            float hue = Mathf.Repeat(_textAnimTime * 0.3f + i * 0.08f, 1f);
            Color rainbow = Color.HSVToRGB(hue, 0.8f, 1f);

            byte a = (byte)(charAlpha * 255);
            Color32 c32 = new Color32(
                (byte)(rainbow.r * 255),
                (byte)(rainbow.g * 255),
                (byte)(rainbow.b * 255),
                a
            );

            for (int v = 0; v < 4; v++)
            {
                colors[vertIdx + v] = c32;
            }
        }

        // 메시 업데이트
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textInfo.meshInfo[i].mesh.colors32 = textInfo.meshInfo[i].colors32;
            _announceText.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }

    // ================================================================
    // 응축 이펙트: 회전하며 중심으로 모이는 빛 줄기 (대량)
    // ================================================================
    ParticleSystem CreateGatherSpiral()
    {
        var ps = CreatePS("FX_GatherSpiral");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(-8f, -3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
        main.startColor = _color;
        main.maxParticles = 800;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 80; // 시작 (코루틴에서 600까지 증가)

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 5f * _range;

        // 회전 (소용돌이 효과)
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(3f, 7f);
        vel.radial = new ParticleSystem.MinMaxCurve(-3f, -1f);

        // 크기: 중심에 가까워질수록 작아짐
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.8f), new Keyframe(0.5f, 1f), new Keyframe(1, 0.05f)));

        ApplyGatherGradient(ps);
        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 별 궤적: 응축 중 별이 빙빙 돌면서 흡수
    // ================================================================
    ParticleSystem CreateStarTrail()
    {
        var ps = CreatePS("FX_StarTrail");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(-4f, -2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.95f, 0.4f, 1f),  // 금색
            new Color(1f, 0.7f, 0.9f, 1f)     // 분홍
        );
        main.maxParticles = 300;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 60;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 3.5f * _range;

        // 더 빠른 회전 + 흡수
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(5f, 10f);
        vel.radial = new ParticleSystem.MinMaxCurve(-2f, -0.5f);

        // 크기: 깜빡이는 별
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.3f),
            new Keyframe(0.2f, 1f),
            new Keyframe(0.4f, 0.4f),
            new Keyframe(0.6f, 0.9f),
            new Keyframe(0.8f, 0.3f),
            new Keyframe(1, 0f)));

        // 색상
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f),
                new GradientColorKey(Color.white, 0.5f),
                new GradientColorKey(_color, 1f)
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 외곽 링: 응축 중 바깥에서 빙빙 도는 큰 파티클 링
    // ================================================================
    ParticleSystem CreateOuterGatherRing()
    {
        var ps = CreatePS("FX_OuterRing");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, Color.white);
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 40;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 3f * _range;
        shape.arc = 360f;

        // 빠른 회전
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(4f, 8f);
        vel.radial = new ParticleSystem.MinMaxCurve(-1f, -0.3f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(_color, 0f), new GradientColorKey(Color.white, 0.6f), new GradientColorKey(_color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 에너지 코어: 중심에서 빛나는 구 (더 강렬)
    // ================================================================
    ParticleSystem CreateEnergyCore()
    {
        var ps = CreatePS("FX_EnergyCore");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 0.3f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startColor = Color.white;
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.rateOverTime = 50;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f * _range;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.5f), new Keyframe(0.5f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(_color, 0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(_color, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, false);
        return ps;
    }

    // ================================================================
    // 플래시: 폭발 순간 화면 번쩍
    // ================================================================
    ParticleSystem CreateFlashGlow()
    {
        var ps = CreatePS("FX_Flash");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 0.3f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.maxParticles = 5;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 1f), new Keyframe(0.15f, 1.5f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.5f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, true);
        return ps;
    }

    // ================================================================
    // 메인 폭발: 큰 파티클이 사방으로 터짐 (대량!)
    // ================================================================
    ParticleSystem CreateMainBurst()
    {
        var ps = CreatePS("FX_MainBurst");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f * _range, 22f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, Color.white);
        main.maxParticles = 250;
        main.gravityModifier = 0.3f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f * _range;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 1f), new Keyframe(0.2f, 0.7f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.3f), new GradientColorKey(_color, 0.7f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 미니 2차 폭발 (여운)
    // ================================================================
    ParticleSystem CreateMiniBurst()
    {
        var ps = CreatePS("FX_MiniBurst");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f * _range, 10f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, new Color(1f, 0.95f, 0.7f));
        main.maxParticles = 80;
        main.gravityModifier = 0.2f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f * _range;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.4f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 충격파 링: 원형으로 퍼져나가는 파동
    // ================================================================
    ParticleSystem CreateShockwaveRing(float speed, float lifetime)
    {
        var ps = CreatePS("FX_Shockwave");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = lifetime;
        main.startSpeed = speed * _range;
        main.startSize = 1.2f;
        main.startColor = new Color(_color.r, _color.g, _color.b, 0.8f);
        main.maxParticles = 40;
        main.gravityModifier = 0f;
        main.startRotation3D = false;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f * _range;
        shape.arc = 360f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.4f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 컨페티: 알록달록 종이 조각이 터지며 떨어짐
    // ================================================================
    ParticleSystem CreateConfettiBurst()
    {
        var ps = CreatePS("FX_Confetti");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f * _range, 14f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.maxParticles = 200;
        main.gravityModifier = 1.2f;

        // 알록달록 랜덤 색상
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.3f, 0.5f),  // 분홍
            new Color(0.3f, 0.8f, 1f)   // 하늘
        );

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 60f;
        shape.radius = 0.3f * _range;

        // 회전 (종이 조각이 빙글빙글)
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var rotOverLife = ps.rotationOverLifetime;
        rotOverLife.enabled = true;
        rotOverLife.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

        // 크기
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1, 0f)));

        // 색상: 다양한 색으로 변화
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.3f),
                new GradientColorKey(new Color(0.5f, 0.3f, 1f), 0.6f),
                new GradientColorKey(new Color(0.2f, 1f, 0.5f), 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        // 공기 저항 (천천히 떨어지는 느낌)
        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.dampen = 0.2f;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 반짝이 샤워: 별처럼 반짝이며 천천히 떨어지는 파티클 (대량)
    // ================================================================
    ParticleSystem CreateSparkleShower()
    {
        var ps = CreatePS("FX_Sparkle");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * _range, 10f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.9f, 0.3f, 1f),  // 금색
            Color.white
        );
        main.maxParticles = 150;
        main.gravityModifier = 0.8f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f * _range;

        // 크기: 깜빡이는 별
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0f),
            new Keyframe(0.1f, 1f),
            new Keyframe(0.25f, 0.2f),
            new Keyframe(0.4f, 0.9f),
            new Keyframe(0.55f, 0.15f),
            new Keyframe(0.7f, 0.7f),
            new Keyframe(0.85f, 0.1f),
            new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0.3f),
                new GradientColorKey(_color, 0.7f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 헬퍼 메서드
    // ================================================================

    ParticleSystem CreatePS(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_fxRoot.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 화면 밖으로 나가도 시뮬레이션 + 렌더링 유지
        var main = ps.main;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        return ps;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    /// <summary>
    /// URP 렌더링 직전 콜백: 컬링 판정 전에 bounds를 강제 확장.
    /// LateUpdate보다 늦게 실행되므로 Unity의 bounds 재계산을 확실히 덮어씀.
    /// </summary>
    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_fxRoot == null) return;

        // 캐시가 없거나 자식 수가 변했으면 갱신
        if (_cachedRenderers == null)
            _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        var largeBounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
        foreach (var psr in _cachedRenderers)
        {
            if (psr != null)
                psr.localBounds = largeBounds;
        }
    }

    void SetupRenderer(ParticleSystem ps, bool useAdditive = false, bool isBurst = false)
    {
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;

        // 바운딩 박스를 크게 설정 (초기값, LateUpdate에서 매 프레임 갱신)
        psr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2000f);

        if (isBurst || useAdditive)
        {
            // 폭발/플래시 단계: 버스트 머티리얼 우선 → 없으면 기본 Additive
            if (_burstMat != null)
                psr.material = _burstMat;
            else if (_additiveMat != null)
                psr.material = _additiveMat;
        }
        else
        {
            // 응축 단계: 응축 머티리얼 우선
            if (_particleMat != null)
                psr.material = _particleMat;
        }
    }

    Material CreateDefaultAdditiveMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) return null;

        var mat = new Material(shader);
        mat.SetColor("_BaseColor", Color.white);

        // 프로시저럴 원형 그라데이션 텍스처 (빌트인 리소스가 Unity 6에서 없을 수 있음)
        var circleTex = CreateCircleTexture(64);
        mat.SetTexture("_BaseMap", circleTex);
        mat.SetTexture("_MainTex", circleTex);

        // Transparent + Additive
        mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = 3000;

        return mat;
    }

    Texture2D CreateCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - dist / center);
                alpha *= alpha; // 부드러운 가장자리
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void ApplyGatherGradient(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(_color, 0f), new GradientColorKey(Color.white, 0.8f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0.6f, 1f) }
        );
        col.color = gradient;
    }

    /// <summary>
    /// 탄력 있는 이징 (튕기면서 등장)
    /// </summary>
    static float EaseOutElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;

        float p = 0.4f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
    }

    /// <summary>
    /// 한글 마지막 글자에 받침이 있는지 확인 (이/가 조사 판별용)
    /// </summary>
    static bool HasBatchim(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        char last = text[text.Length - 1];
        if (last < 0xAC00 || last > 0xD7A3) return false; // 한글 범위 아님
        return (last - 0xAC00) % 28 != 0;
    }
}
