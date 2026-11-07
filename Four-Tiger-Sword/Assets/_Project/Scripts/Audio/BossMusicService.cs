using System;
using System.Collections.Generic;
using UnityEngine;

public enum BossMusicMode { Keep, Play, Stop }

/// <summary>Owner-scoped boss music. Releasing one boss restores the most recent surviving request.</summary>
[DisallowMultipleComponent]
public sealed class BossMusicService : MonoBehaviour
{
    private sealed class Request
    {
        public UnityEngine.Object Owner;
        public AudioClip Clip;
        public float Volume;
        public int ResumeSample;
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public Request Request;
        public float From, To;
    }

    private static BossMusicService instance;
    private static bool quitting;
    private readonly List<Request> requests = new List<Request>();
    private Voice[] voices;
    private Request current;
    private float fadeElapsed, fadeDuration;

    public static int ActiveOwnerCount => instance != null ? instance.requests.Count : 0;
    public static UnityEngine.Object CurrentOwner => instance != null ? instance.current?.Owner : null;
    public static AudioClip CurrentClip => instance != null ? instance.current?.Clip : null;
    public static float PlaybackTime
    {
        get
        {
            if (instance?.voices == null || instance.current?.Clip == null) return 0f;
            foreach (var voice in instance.voices)
                if (voice.Request == instance.current && voice.Source.clip != null) return voice.Source.time;
            return 0f;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        if (instance != null) instance.Clear();
        instance = null;
        quitting = false;
    }

    public static void Set(UnityEngine.Object owner, BossMusicMode mode, AudioClip clip,
        float volume = 1f, float fadeSeconds = 0.5f)
    {
        if (!Application.isPlaying || quitting || !Available(owner) || !Enum.IsDefined(typeof(BossMusicMode), mode)) return;
        if (mode == BossMusicMode.Keep) return;
        if (!Finite(volume) || volume < 0f || volume > 1f || !Finite(fadeSeconds) || fadeSeconds < 0f) return;
        if (mode == BossMusicMode.Play && clip == null) return;
        var service = GetService();
        service.CapturePositions();
        int resume = 0;
        for (int i = service.requests.Count - 1; i >= 0; i--)
        {
            var previous = service.requests[i];
            if (previous.Owner != owner) continue;
            if (previous.Clip == clip) resume = previous.ResumeSample;
            service.requests.RemoveAt(i);
        }
        service.requests.Add(new Request
        {
            Owner = owner, Clip = mode == BossMusicMode.Play ? clip : null,
            Volume = volume, ResumeSample = resume
        });
        service.Prune();
        service.Transition(service.Latest(), fadeSeconds);
    }

    public static void Release(UnityEngine.Object owner, float fadeSeconds = 0.5f)
    {
        if (instance == null) return;
        instance.CapturePositions();
        bool removed = false;
        for (int i = instance.requests.Count - 1; i >= 0; i--)
            if (ReferenceEquals(instance.requests[i].Owner, owner))
            { instance.requests.RemoveAt(i); removed = true; }
        removed |= instance.Prune();
        if (removed) instance.Transition(instance.Latest(), Finite(fadeSeconds) ? Mathf.Max(0f, fadeSeconds) : 0.5f);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Available(UnityEngine.Object owner)
        => owner != null && (!(owner is Behaviour behavior) || behavior.isActiveAndEnabled)
            && (!(owner is GameObject host) || host.activeInHierarchy);

    private static BossMusicService GetService()
    {
        if (instance != null) return instance;
        instance = FindFirstObjectByType<BossMusicService>(FindObjectsInactive.Include);
        if (instance == null) instance = new GameObject("Boss Music").AddComponent<BossMusicService>();
        instance.Initialize();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        Initialize();
    }

    private void Initialize()
    {
        DontDestroyOnLoad(gameObject);
        gameObject.hideFlags = HideFlags.HideInHierarchy;
        if (voices != null) return;
        voices = new Voice[2];
        var existing = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource source;
            if (i < existing.Length) source = existing[i];
            else
            {
                var child = new GameObject("Music " + i);
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
            }
            source.Stop(); source.clip = null; source.volume = 0f;
            source.loop = true; source.playOnAwake = false; source.spatialBlend = 0f;
            source.pitch = 1f; source.dopplerLevel = 0f; source.ignoreListenerPause = true;
            voices[i] = new Voice { Source = source };
        }
    }

    private Request Latest() => requests.Count > 0 ? requests[requests.Count - 1] : null;

    private bool Prune()
    {
        bool changed = false;
        for (int i = requests.Count - 1; i >= 0; i--)
            if (!Available(requests[i].Owner)) { requests.RemoveAt(i); changed = true; }
        return changed;
    }

    private void CapturePosition(Voice voice)
    {
        if (voice.Request != null && voice.Source.clip != null && voice.Request.Clip == voice.Source.clip)
            voice.Request.ResumeSample = voice.Source.timeSamples;
    }

    private void CapturePositions()
    {
        if (voices == null) return;
        foreach (var voice in voices) CapturePosition(voice);
    }

    private void Transition(Request next, float duration)
    {
        if (current == next) return;
        CapturePositions();
        current = next;
        Voice destination = null;
        foreach (var voice in voices)
        {
            voice.From = voice.Source.volume;
            voice.To = 0f;
            if (next?.Clip != null && voice.Source.clip == next.Clip) destination = voice;
        }
        if (next?.Clip != null)
        {
            if (destination == null)
            {
                destination = voices[0].Source.volume <= voices[1].Source.volume ? voices[0] : voices[1];
                CapturePosition(destination);
                destination.Source.Stop();
                destination.Source.clip = next.Clip;
                destination.Source.volume = destination.From = 0f;
                if (next.Clip.samples > 0)
                    destination.Source.timeSamples = Mathf.Clamp(next.ResumeSample, 0, next.Clip.samples - 1);
                destination.Source.Play();
            }
            // A different owner or phase requesting the same song keeps the live playback position.
            destination.Request = next;
            destination.To = next.Volume;
        }
        fadeElapsed = 0f;
        fadeDuration = duration;
        ApplyFade(duration <= 0f ? 1f : 0f);
    }

    private void Update()
    {
        if (Prune()) Transition(Latest(), 0.5f);
        if (voices == null) return;
        fadeElapsed += Time.unscaledDeltaTime;
        ApplyFade(fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeDuration));
    }

    private void ApplyFade(float progress)
    {
        foreach (var voice in voices)
        {
            voice.Source.volume = Mathf.Lerp(voice.From, voice.To, progress);
            if (progress < 1f || voice.To > 0f || voice.Source.clip == null) continue;
            CapturePosition(voice);
            voice.Source.Stop(); voice.Source.clip = null; voice.Request = null;
        }
    }

    private void Clear()
    {
        requests.Clear(); current = null;
        if (voices == null) return;
        foreach (var voice in voices)
        {
            if (voice.Source == null) continue;
            voice.Source.Stop(); voice.Source.clip = null; voice.Source.volume = 0f;
            voice.Request = null; voice.From = voice.To = 0f;
        }
    }

    private void OnApplicationQuit() { quitting = true; Clear(); }
    private void OnDestroy() { Clear(); if (instance == this) instance = null; }
}
