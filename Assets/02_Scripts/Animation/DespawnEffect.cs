using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 모델 퇴장 이펙트: 에너지 흡수 → 모델 축소 → 팡! 소멸 폭발 → 파티클 페이드아웃
///
/// 연출 흐름:
///   1. 에너지 흡수: 파티클이 모델 주변에서 빙글빙글 돌며 점점 빨라짐
///   2. 모델 축소: 탄력 있는 축소 애니메이션 (EaseInBack)
///   3. 소멸 폭발: 파티클이 사방으로 흩어지며 사라짐
///   4. 파티클 페이드아웃: 지정된 시간 후 자연스럽게 소멸
/// </summary>
public class DespawnEffect : MonoBehaviour
{
    private Vector3 _originalScale;
    private float _gatherDuration;
    private float _shrinkDuration;
    private float _fadeoutDuration;
    private Color _color;
    private Material _particleMat;
    private Material _burstMat;
    private Material _additiveMat;
    private Material _customMat;
    private Material _customBurstMat;
    private bool _hasCustomMat;
    private bool _hasCustomBurstMat;
    private float _range = 1f;

    private GameObject _fxRoot;
    private ParticleSystemRenderer[] _cachedRenderers;
    private Action _onComplete;

    /// <summary>
    /// 퇴장 이펙트 실행
    /// </summary>
    /// <param name="gatherDuration">에너지 흡수 시간 (초)</param>
    /// <param name="shrinkDuration">모델 축소 시간 (초)</param>
    /// <param name="fadeoutDuration">파티클 페이드아웃 대기 시간 (초)</param>
    /// <param name="color">파티클 색상</param>
    /// <param name="customMaterial">흡수 단계 커스텀 머티리얼</param>
    /// <param name="burstMaterial">폭발 단계 커스텀 머티리얼</param>
    /// <param name="range">파티클 범위 배율</param>
    /// <param name="onComplete">이펙트 완료 시 콜백 (모델 삭제용)</param>
    public void Play(float gatherDuration, float shrinkDuration, float fadeoutDuration,
                     Color color, Material customMaterial = null, Material burstMaterial = null,
                     float range = 1f, Action onComplete = null)
    {
        _originalScale = transform.localScale;
        _gatherDuration = gatherDuration;
        _shrinkDuration = shrinkDuration;
        _fadeoutDuration = fadeoutDuration;
        _color = color;
        _range = range;
        _onComplete = onComplete;

        // 커스텀 머티리얼 설정
        _hasCustomMat = customMaterial != null;
        _customMat = _hasCustomMat ? new Material(customMaterial) : null;
        _hasCustomBurstMat = burstMaterial != null;
        _customBurstMat = _hasCustomBurstMat ? new Material(burstMaterial) : null;

        _particleMat = _hasCustomMat ? _customMat : CreateDefaultAdditiveMaterial();
        _burstMat = _hasCustomBurstMat ? _customBurstMat : _particleMat;
        _additiveMat = CreateDefaultAdditiveMaterial();

        // 이펙트 루트 (씬 루트에 배치 - 모델 스케일 변경의 영향 안 받음)
        _fxRoot = new GameObject("DespawnFX_Root");
        _fxRoot.transform.position = transform.position + new Vector3(0f, 0f, -2f);
        _fxRoot.transform.localScale = Vector3.one;

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

        StartCoroutine(DespawnSequence());
    }

    IEnumerator DespawnSequence()
    {
        // ============================================
        // Phase 1: 에너지 흡수 (서서히 강렬해짐)
        // ============================================
        var gatherPS = CreateReverseGatherSpiral();
        var corePS = CreateDespawnCore();
        var starTrailPS = CreateDespawnStarTrail();
        var outerRingPS = CreateDespawnOuterRing();

        gatherPS.Play();
        corePS.Play();
        starTrailPS.Play();
        outerRingPS.Play();

        _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        // 흡수 중 점점 강렬해지는 연출 + 모델 깜빡임
        float gatherElapsed = 0f;
        var gatherEmission = gatherPS.emission;

        while (gatherElapsed < _gatherDuration)
        {
            gatherElapsed += Time.deltaTime;
            float progress = gatherElapsed / _gatherDuration;

            // 파티클 점점 많아짐
            float intensity = Mathf.Lerp(0.2f, 1f, progress * progress);
            gatherEmission.rateOverTime = Mathf.Lerp(40f, 400f, intensity);

            // 모델 깜빡임 (후반부에 점점 빨라짐)
            if (progress > 0.3f)
            {
                float blinkSpeed = Mathf.Lerp(3f, 15f, (progress - 0.3f) / 0.7f);
                float blinkAlpha = 0.5f + 0.5f * Mathf.Sin(gatherElapsed * blinkSpeed);
                SetModelAlpha(blinkAlpha);
            }

            yield return null;
        }

        gatherPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        corePS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        starTrailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        outerRingPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // ============================================
        // Phase 2: 플래시 + 폭발 + 모델 축소 동시 실행
        //   → 모델이 파티클로 흩어지는 느낌
        // ============================================

        // 플래시 (번쩍)
        var flashPS = CreateDespawnFlash();
        flashPS.Emit(2);

        // 폭발 파티클 (모델에서 흩어짐)
        var burstPS = CreateDespawnBurst();
        burstPS.Emit(120);

        // 충격파
        var ringPS = CreateDespawnShockwave(10f, 0.5f);
        ringPS.Emit(1);

        // 반짝이
        var sparklePS = CreateDespawnSparkle();
        sparklePS.Emit(80);

        _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        // 모델 축소 (폭발과 동시에 → 파티클로 흩어지는 느낌)
        SetModelAlpha(1f);
        float shrinkElapsed = 0f;
        bool emittedRing2 = false;
        bool emittedMini = false;

        while (shrinkElapsed < _shrinkDuration)
        {
            shrinkElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(shrinkElapsed / _shrinkDuration);

            // 빠르게 줄어드는 이징 (EaseOutQuad 역방향)
            float s = 1f - t * t;
            transform.localScale = _originalScale * Mathf.Max(0f, s);

            // 축소 중 추가 파티클 (딜레이 느낌)
            if (!emittedRing2 && t > 0.3f)
            {
                emittedRing2 = true;
                var ringPS2 = CreateDespawnShockwave(7f, 0.6f);
                ringPS2.Emit(1);
            }
            if (!emittedMini && t > 0.6f)
            {
                emittedMini = true;
                var miniPS = CreateDespawnMini();
                miniPS.Emit(40);
                _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();
            }

            yield return null;
        }
        transform.localScale = Vector3.zero;

        // ============================================
        // Phase 3: 모델 즉시 삭제 + 파티클만 자연 소멸
        // ============================================

        // 렌더링 콜백 해제
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        _cachedRenderers = null;

        // 파티클 + 머티리얼 모두 파티클 소멸 후 삭제
        float delay = _fadeoutDuration;
        if (_fxRoot != null) Destroy(_fxRoot, delay);
        if (_customMat != null) Destroy(_customMat, delay);
        if (_customBurstMat != null) Destroy(_customBurstMat, delay);
        if (!_hasCustomMat && _particleMat != null) Destroy(_particleMat, delay);
        if (_additiveMat != null) Destroy(_additiveMat, delay);

        // 모델 즉시 삭제
        _onComplete?.Invoke();
        Destroy(this);
    }

    // ================================================================
    // 역방향 응축: 파티클이 바깥에서 안으로 빨려들어감
    // ================================================================
    ParticleSystem CreateReverseGatherSpiral()
    {
        var ps = CreatePS("FX_DespawnGather");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(-6f, -2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startColor = _color;
        main.maxParticles = 600;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 40;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 4f * _range;

        // 소용돌이 + 흡수
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(4f, 9f);
        vel.radial = new ParticleSystem.MinMaxCurve(-4f, -2f);

        // 크기: 점점 작아짐
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 1f), new Keyframe(0.5f, 0.6f), new Keyframe(1, 0f)));

        // 색상: 커스텀 색 → 흰색 → 투명
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(_color, 0f), new GradientColorKey(Color.white, 0.7f) },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.2f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0.5f, 1f)
            }
        );
        col.color = gradient;

        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 에너지 코어: 중심에서 맥동하는 빛
    // ================================================================
    ParticleSystem CreateDespawnCore()
    {
        var ps = CreatePS("FX_DespawnCore");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 0.4f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
        main.startColor = Color.white;
        main.maxParticles = 60;

        var emission = ps.emission;
        emission.rateOverTime = 40;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f * _range;

        // 맥동하는 크기
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.3f), new Keyframe(0.3f, 1f),
            new Keyframe(0.6f, 0.5f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.5f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, false);
        return ps;
    }

    // ================================================================
    // 별 궤적: 빙빙 돌면서 흡수되는 별
    // ================================================================
    ParticleSystem CreateDespawnStarTrail()
    {
        var ps = CreatePS("FX_DespawnStar");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(-3f, -1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.7f, 0.3f, 1f),  // 주황
            new Color(0.8f, 0.5f, 1f, 1f)    // 보라
        );
        main.maxParticles = 200;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 45;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 3f * _range;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(6f, 12f);
        vel.radial = new ParticleSystem.MinMaxCurve(-3f, -1f);

        // 깜빡이는 별
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.4f), new Keyframe(0.2f, 1f),
            new Keyframe(0.4f, 0.3f), new Keyframe(0.6f, 0.8f),
            new Keyframe(0.8f, 0.2f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0f),
                new GradientColorKey(Color.white, 0.4f),
                new GradientColorKey(_color, 1f)
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 외곽 링: 수축하는 큰 파티클 링
    // ================================================================
    ParticleSystem CreateDespawnOuterRing()
    {
        var ps = CreatePS("FX_DespawnRing");
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, Color.white);
        main.maxParticles = 150;

        var emission = ps.emission;
        emission.rateOverTime = 35;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 2.5f * _range;
        shape.arc = 360f;

        // 회전하면서 수축
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.orbitalZ = new ParticleSystem.MinMaxCurve(5f, 10f);
        vel.radial = new ParticleSystem.MinMaxCurve(-2f, -0.5f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(_color, 0f), new GradientColorKey(Color.white, 0.5f), new GradientColorKey(_color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps);
        return ps;
    }

    // ================================================================
    // 소멸 플래시: 순간 번쩍
    // ================================================================
    ParticleSystem CreateDespawnFlash()
    {
        var ps = CreatePS("FX_DespawnFlash");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 0.25f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startColor = new Color(1f, 1f, 1f, 0.8f);
        main.maxParticles = 5;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 1f), new Keyframe(0.2f, 1.3f), new Keyframe(1, 0f)));

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
    // 소멸 폭발: 에너지가 흩어짐
    // ================================================================
    ParticleSystem CreateDespawnBurst()
    {
        var ps = CreatePS("FX_DespawnBurst");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f * _range, 16f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 1.0f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, Color.white);
        main.maxParticles = 150;
        main.gravityModifier = 0.4f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f * _range;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 1f), new Keyframe(0.3f, 0.5f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.4f), new GradientColorKey(_color * 0.5f, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 소멸 충격파
    // ================================================================
    ParticleSystem CreateDespawnShockwave(float speed, float lifetime)
    {
        var ps = CreatePS("FX_DespawnWave");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = lifetime;
        main.startSpeed = speed * _range;
        main.startSize = 0.8f;
        main.startColor = new Color(_color.r, _color.g, _color.b, 0.7f);
        main.maxParticles = 30;
        main.gravityModifier = 0f;

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
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.5f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.4f, 0.3f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 잔여 미니 파티클
    // ================================================================
    ParticleSystem CreateDespawnMini()
    {
        var ps = CreatePS("FX_DespawnMini");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * _range, 7f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(_color, new Color(0.8f, 0.8f, 1f));
        main.maxParticles = 60;
        main.gravityModifier = 0.15f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f * _range;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_color, 0.5f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = gradient;

        SetupRenderer(ps, false, true);
        return ps;
    }

    // ================================================================
    // 반짝이: 소멸 후 남는 반짝임
    // ================================================================
    ParticleSystem CreateDespawnSparkle()
    {
        var ps = CreatePS("FX_DespawnSparkle");
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * _range, 8f * _range);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.85f, 0.3f, 1f),
            Color.white
        );
        main.maxParticles = 100;
        main.gravityModifier = 0.6f;

        var emission = ps.emission;
        emission.rateOverTime = 0;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f * _range;

        // 깜빡이는 별
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0f), new Keyframe(0.1f, 1f),
            new Keyframe(0.3f, 0.15f), new Keyframe(0.5f, 0.7f),
            new Keyframe(0.7f, 0.1f), new Keyframe(1, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0.3f),
                new GradientColorKey(_color, 0.7f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) }
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

        var main = ps.main;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        return ps;
    }

    void SetupRenderer(ParticleSystem ps, bool useAdditive = false, bool isBurst = false)
    {
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2000f);

        if (isBurst || useAdditive)
        {
            if (_burstMat != null) psr.material = _burstMat;
            else if (_additiveMat != null) psr.material = _additiveMat;
        }
        else
        {
            if (_particleMat != null) psr.material = _particleMat;
        }
    }

    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (_fxRoot == null) return;

        if (_cachedRenderers == null)
            _cachedRenderers = _fxRoot.GetComponentsInChildren<ParticleSystemRenderer>();

        var largeBounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
        foreach (var psr in _cachedRenderers)
        {
            if (psr != null)
                psr.localBounds = largeBounds;
        }
    }

    /// <summary>
    /// 모델의 투명도 조절 (깜빡임 연출용)
    /// </summary>
    void SetModelAlpha(float alpha)
    {
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
            }
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

        var circleTex = CreateCircleTexture(64);
        mat.SetTexture("_BaseMap", circleTex);
        mat.SetTexture("_MainTex", circleTex);

        mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
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
                alpha *= alpha;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>
    /// EaseInBack: 뒤로 살짝 갔다가 빠르게 들어감 (빨려 들어가는 느낌)
    /// </summary>
    static float EaseInBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }
}
