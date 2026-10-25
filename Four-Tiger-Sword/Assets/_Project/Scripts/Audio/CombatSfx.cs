using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A bounded voice pool shared by attacks and confirmed impacts.</summary>
[DisallowMultipleComponent]
public sealed class CombatSfx : MonoBehaviour
{
    public const int Capacity = 24;
    private const double LoadTimeout = 2d;
    private const int MaxLoadAttempts = 3;
    private static CombatSfx instance;
    private static System.Random pitchRandom = new System.Random();
    private static readonly HashSet<ImpactKey> frameImpacts = new HashSet<ImpactKey>();
    private static int impactFrame = -1;
    private static bool quitting;

    private Voice[] voices;
    private long nextSequence;

    public static int PooledVoiceCount => instance != null && instance.voices != null ? instance.voices.Length : 0;
    public static int ActiveVoiceCount => instance != null ? instance.CountVoices(false) : 0;
    public static int PendingVoiceCount => instance != null ? instance.CountVoices(true) : 0;
    public static int PlayedCueCount { get; private set; }
    public static int PlayedVoiceCount { get; private set; }
    public static int SuppressedImpactCount { get; private set; }
    public static int DroppedVoiceCount { get; private set; }
    public static int FailedLoadCount { get; private set; }

    private sealed class Voice
    {
        public AudioSource Source;
        public AudioClip Clip;
        public bool Pending;
        public int LoadAttempts;
        public double RetryAt;
        public double Deadline;
        public double EndsAt;
        public int StartedFrame;
        public long Sequence;
    }

    private readonly struct ImpactKey : System.IEquatable<ImpactKey>
    {
        private readonly object source;
        private readonly AudioClip clip;
        private readonly AudioClip accent;

        public ImpactKey(object source, CombatSoundCue cue)
        {
            this.source = source;
            clip = cue.Clip;
            accent = cue.Accent;
        }

        public bool Equals(ImpactKey other)
            => ReferenceEquals(source, other.source) && ReferenceEquals(clip, other.clip)
                && ReferenceEquals(accent, other.accent);

        public override bool Equals(object other) => other is ImpactKey key && Equals(key);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = source == null ? 0 : RuntimeHelpers.GetHashCode(source);
                hash = hash * 397 ^ (ReferenceEquals(clip, null) ? 0 : RuntimeHelpers.GetHashCode(clip));
                return hash * 397 ^ (ReferenceEquals(accent, null) ? 0 : RuntimeHelpers.GetHashCode(accent));
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        if (instance != null)
        {
            instance.ClearVoices();
            SceneManager.sceneUnloaded -= instance.OnSceneUnloaded;
        }
        instance = null;
        pitchRandom = new System.Random();
        frameImpacts.Clear();
        impactFrame = -1;
        quitting = false;
        PlayedCueCount = PlayedVoiceCount = SuppressedImpactCount = DroppedVoiceCount = FailedLoadCount = 0;
    }

    public static void Play(CombatSoundCue cue, Vector3 position, object source = null, bool impact = false)
    {
        if (cue.IsEmpty || !Application.isPlaying || quitting) return;
        if (impactFrame != Time.frameCount)
        {
            frameImpacts.Clear();
            impactFrame = Time.frameCount;
        }
        if (impact && !frameImpacts.Add(new ImpactKey(source, cue)))
        {
            SuppressedImpactCount++;
            return;
        }

        CombatSfx pool = GetPool();
        PlayedCueCount++;
        pool.QueueVoice(cue.Clip, cue.Volume, cue.PitchRange, position);
        pool.QueueVoice(cue.Accent, cue.AccentVolume, cue.PitchRange, position);
    }

    private static CombatSfx GetPool()
    {
        if (instance == null)
        {
            // Reuse a surviving runner when Enter Play Mode has domain/scene reload disabled.
            instance = Object.FindFirstObjectByType<CombatSfx>(FindObjectsInactive.Include);
            if (instance == null)
            {
                var root = new GameObject("Combat SFX Pool");
                instance = root.AddComponent<CombatSfx>();
            }
            instance.Initialize();
        }
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Initialize();
    }

    private void Initialize()
    {
        DontDestroyOnLoad(gameObject);
        gameObject.hideFlags = HideFlags.HideInHierarchy;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        if (voices != null) return;

        var existing = GetComponentsInChildren<AudioSource>(true);
        voices = new Voice[Capacity];
        for (int i = 0; i < Capacity; i++)
        {
            AudioSource audio;
            if (i < existing.Length) audio = existing[i];
            else
            {
                var child = new GameObject("Voice " + i);
                child.transform.SetParent(transform, false);
                audio = child.AddComponent<AudioSource>();
            }
            audio.Stop();
            audio.clip = null;
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0.35f;
            audio.minDistance = 4f;
            audio.maxDistance = 35f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.dopplerLevel = 0f;
            audio.ignoreListenerPause = true;
            voices[i] = new Voice { Source = audio };
        }
    }

    private void QueueVoice(AudioClip clip, float volume, Vector2 pitchRange, Vector3 position)
    {
        if (clip == null || volume <= 0f) return;
        double now = Time.realtimeSinceStartupAsDouble;
        UpdateVoices(now);
        Voice voice = null;
        foreach (Voice candidate in voices)
        {
            if (candidate.Clip == null) { voice = candidate; break; }
            if (voice == null || candidate.Sequence < voice.Sequence) voice = candidate;
        }
        if (voice.Clip != null) DroppedVoiceCount++;
        Release(voice);
        voice.Clip = clip;
        voice.Pending = true;
        voice.Sequence = ++nextSequence;
        voice.Deadline = now + LoadTimeout;
        voice.Source.transform.position = position;
        voice.Source.clip = clip;
        voice.Source.volume = volume;
        voice.Source.pitch = Mathf.Lerp(pitchRange.x, pitchRange.y, (float)pitchRandom.NextDouble());
        TryStart(voice, now);
    }

    private void TryStart(Voice voice, double now)
    {
        if (voice.Clip == null) { Release(voice); return; }
        if (voice.Clip.loadState == AudioDataLoadState.Loaded)
        {
            voice.Pending = false;
            voice.Source.Play();
            voice.StartedFrame = Time.frameCount;
            voice.EndsAt = now + voice.Clip.length / Mathf.Max(0.1f, voice.Source.pitch) + 0.1d;
            PlayedVoiceCount++;
            return;
        }
        if (now >= voice.Deadline)
        {
            FailedLoadCount++;
            Release(voice);
            return;
        }
        if (voice.Clip.loadState == AudioDataLoadState.Loading || now < voice.RetryAt) return;
        if (voice.LoadAttempts >= MaxLoadAttempts)
        {
            FailedLoadCount++;
            Release(voice);
            return;
        }

        voice.LoadAttempts++;
        voice.RetryAt = now + 0.1d;
        voice.Clip.LoadAudioData();
        // Decompress-on-load clips normally finish synchronously; do not delay their first sound.
        if (voice.Clip.loadState == AudioDataLoadState.Loaded) TryStart(voice, now);
    }

    private void Update()
    {
        UpdateVoices(Time.realtimeSinceStartupAsDouble);
        if (impactFrame != Time.frameCount) frameImpacts.Clear();
    }

    private void UpdateVoices(double now)
    {
        if (voices == null) return;
        foreach (Voice voice in voices)
        {
            if (voice.Clip == null) continue;
            if (voice.Pending) TryStart(voice, now);
            // Play can precede the audio thread's isPlaying update; retain every new voice for its first frame.
            else if (now >= voice.EndsAt || (Time.frameCount > voice.StartedFrame && !voice.Source.isPlaying))
                Release(voice);
        }
    }

    private static void Release(Voice voice)
    {
        if (voice.Source != null)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
        }
        voice.Clip = null;
        voice.Pending = false;
        voice.LoadAttempts = 0;
        voice.StartedFrame = -1;
        voice.RetryAt = voice.Deadline = voice.EndsAt = 0d;
    }

    private int CountVoices(bool pending)
    {
        int count = 0;
        if (voices != null)
            foreach (Voice voice in voices)
                if (voice.Clip != null && voice.Pending == pending) count++;
        return count;
    }

    private void ClearVoices() => ClearVoicesInternal(true);

    private void ClearVoicesInternal(bool clearImpactHistory)
    {
        if (voices != null)
            foreach (Voice voice in voices) Release(voice);
        nextSequence = 0;
        if (clearImpactHistory)
        {
            frameImpacts.Clear();
            impactFrame = -1;
        }
    }

    private void OnSceneUnloaded(Scene scene) => ClearVoices();

    private void OnApplicationQuit()
    {
        quitting = true;
        ClearVoices();
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        ClearVoicesInternal(instance == this);
        if (instance == this) instance = null;
    }
}
