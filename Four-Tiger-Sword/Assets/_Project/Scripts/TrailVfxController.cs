using UnityEngine;

/// <summary>Controls all trail layers and optional audio beneath a Trail VFX root.</summary>
[DisallowMultipleComponent]
public sealed class TrailVfxController : MonoBehaviour
{
    [Header("Form Selection")]
    [SerializeField, Tooltip("이 효과를 사용할 폼. ELEMENT_NONE은 모든 폼에서 사용하는 공통 효과입니다. 금 폼은 ELEMENT_GOLD를 선택하세요.")]
    private ElementType form = ElementType.ELEMENT_NONE;

    [SerializeField, Tooltip("Trail VFX root. Leave empty when this component is on the VFX prefab root.")]
    private Transform effectRoot;

    [SerializeField, Tooltip("Play the prefab's audio while the trail is emitting.")]
    private bool playAudio = true;

    private TrailRenderer[] trails;
    private AudioSource[] audioSources;
    private bool isEmitting;

    public bool MatchesForm(ElementType currentForm)
        => form == ElementType.ELEMENT_NONE || form == currentForm;

    private void Awake()
    {
        Initialize();
        ClearTrail();
    }

    private void Initialize()
    {
        if (trails != null) return;

        Transform root = effectRoot != null ? effectRoot : transform;
        trails = root.GetComponentsInChildren<TrailRenderer>(true);
        audioSources = root.GetComponentsInChildren<AudioSource>(true);

        foreach (AudioSource source in audioSources)
        {
            source.playOnAwake = false;
            source.Stop();
        }
    }

    /// <summary>Call at the start of an attack, after positioning the effect.</summary>
    public void BeginTrail()
    {
        if (!isActiveAndEnabled) return;
        Initialize();
        isEmitting = true;

        foreach (TrailRenderer trail in trails)
        {
            if (trail == null) continue;
            trail.Clear();
            trail.emitting = true;
        }

        if (!playAudio) return;
        foreach (AudioSource source in audioSources)
        {
            if (source != null && source.isActiveAndEnabled) source.Play();
        }
    }

    /// <summary>Stop emitting; existing trails fade according to their Time settings.</summary>
    public void EndTrail()
    {
        Initialize();
        isEmitting = false;
        foreach (TrailRenderer trail in trails)
        {
            if (trail != null) trail.emitting = false;
        }

        foreach (AudioSource source in audioSources)
        {
            if (source != null) source.Stop();
        }
    }

    /// <summary>Safe to call every frame without restarting the trail or its audio.</summary>
    public void SetEmitting(bool emitting)
    {
        if (emitting == isEmitting) return;
        if (emitting) BeginTrail();
        else EndTrail();
    }

    /// <summary>Stop and immediately remove trails, e.g. after teleporting or before reuse.</summary>
    public void ClearTrail()
    {
        EndTrail();
        foreach (TrailRenderer trail in trails)
        {
            if (trail != null) trail.Clear();
        }
    }

    private void OnDisable()
    {
        ClearTrail();
    }
}
