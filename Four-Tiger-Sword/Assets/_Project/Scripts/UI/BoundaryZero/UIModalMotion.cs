using System;
using System.Collections;
using UnityEngine;

/// <summary>Visual-only, unscaled window motion. The modal owner retains input and pause ownership.</summary>
[DisallowMultipleComponent]
public sealed class UIModalMotion : MonoBehaviour
{
    [SerializeField] private RectTransform visual;
    [SerializeField, Min(0)] private float enterDuration = .24f;
    [SerializeField, Min(0)] private float exitDuration = .16f;
    [SerializeField, Min(0)] private float rise = 20f;
    private CanvasGroup _group;
    private Vector2 _origin;
    private Coroutine _routine;
    private Action _completed;
    private int _version;
    private bool _bound;
    public bool IsAnimating { get; private set; }
    public bool IsClosing { get; private set; }
    public bool IsShown { get; private set; }

    public static UIModalMotion Ensure(GameObject host, RectTransform target,
        float enter = .24f, float exit = .16f, float rise = 20f)
    {
        if (host == null || target == null) return null;
        var motion = host.GetComponent<UIModalMotion>();
        if (motion == null) motion = host.AddComponent<UIModalMotion>();
        if (motion.visual == null)
        {
            motion.visual = target;
            motion.enterDuration = enter;
            motion.exitDuration = exit;
            motion.rise = rise;
        }
        motion.Bind();
        return motion;
    }

    private void Bind()
    {
        if (_bound || visual == null) return;
        _origin = visual.anchoredPosition;
        _group = visual.GetComponent<CanvasGroup>();
        if (_group == null) _group = visual.gameObject.AddComponent<CanvasGroup>();
        _bound = true;
    }

    public void PlayShow(Action completed = null) => Play(true, completed);
    public void PlayClose(Action completed = null) => Play(false, completed);

    private void Play(bool show, Action completed)
    {
        Bind();
        Cancel();
        if (!_bound || !Application.isPlaying || !isActiveAndEnabled)
        {
            if (show) ShowImmediate(); else HideImmediate();
            completed?.Invoke();
            return;
        }
        IsAnimating = true;
        IsClosing = !show;
        IsShown = false;
        _completed = completed;
        int version = _version;
        float from = show ? 0f : _group.alpha;
        _group.alpha = from;
        visual.anchoredPosition = _origin + (show ? Vector2.down * rise : Vector2.zero);
        _routine = StartCoroutine(Animate(show, from, version, completed));
    }

    private IEnumerator Animate(bool show, float from, int version, Action completed)
    {
        float duration = show ? enterDuration : exitDuration;
        float elapsed = 0;
        // Defer completion so even zero-duration calls cannot retain a stale Coroutine handle.
        yield return null;
        while (elapsed < duration && version == _version)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0 ? 1 : Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _group.alpha = Mathf.Lerp(from, show ? 1f : 0f, show ? eased : t);
            visual.anchoredPosition = _origin + Vector2.down * (show ? rise * (1f - eased) : rise * .4f * eased);
            yield return null;
        }
        if (version != _version) yield break;
        _routine = null;
        _group.alpha = show ? 1 : 0;
        visual.anchoredPosition = _origin;
        IsAnimating = IsClosing = false;
        IsShown = show;
        _completed = null;
        completed?.Invoke();
    }

    public void Cancel()
    {
        ++_version;
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _completed = null;
        IsAnimating = IsClosing = false;
        if (_bound && visual != null) visual.anchoredPosition = _origin;
    }

    public void HideImmediate()
    {
        Bind(); Cancel(); IsShown = false;
        if (_group != null) _group.alpha = 0;
    }

    public void ShowImmediate()
    {
        Bind(); Cancel(); IsShown = true;
        if (_group != null) _group.alpha = 1;
    }

    private void OnDisable()
    {
        // Disabling just the animation component snaps to the requested state. Disabling
        // its whole owner instead cancels; that owner's forced-close path releases the modal.
        bool finish = IsAnimating && gameObject.activeInHierarchy;
        bool closing = IsClosing;
        var completed = _completed;
        Cancel();
        if (!finish) return;
        if (closing) HideImmediate(); else ShowImmediate();
        completed?.Invoke();
    }
}
