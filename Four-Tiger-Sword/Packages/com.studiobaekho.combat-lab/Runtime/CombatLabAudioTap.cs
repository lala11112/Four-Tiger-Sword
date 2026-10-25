#if UNITY_EDITOR
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    /// <summary>Copies listener samples without calling Unity APIs on the audio thread.</summary>
    public sealed class CombatLabAudioTap : MonoBehaviour
    {
        internal sealed class Block { internal long firstFrame; internal float[] samples; }
        internal readonly ConcurrentQueue<Block> Blocks = new ConcurrentQueue<Block>();
        internal int DroppedBlocks => droppedBlocks;
        internal float PeakAmplitude => peakAmplitude;
        volatile float peakAmplitude;
        volatile bool recording;
        Stopwatch clock;
        int sampleRate, channels, droppedBlocks;
        long nextFrame = -1;

        internal void Begin(Stopwatch timeline, int rate, int channelCount)
        {
            clock = timeline; sampleRate = rate; channels = channelCount;
            nextFrame = -1; droppedBlocks = 0; peakAmplitude = 0f; recording = true;
        }
        internal void End() { recording = false; }

        void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (!recording) return;
            if (channelCount != channels || data == null || data.Length == 0)
            { Interlocked.Increment(ref droppedBlocks); return; }
            float peak = peakAmplitude;
            for (int i = 0; i < data.Length; i++) peak = System.Math.Max(peak, System.Math.Abs(data[i]));
            peakAmplitude = peak;
            int frames = data.Length / channels;
            long observedStart = (long)(clock.Elapsed.TotalSeconds * sampleRate) - frames;
            if (nextFrame < 0 || System.Math.Abs(observedStart - nextFrame) > sampleRate / 4)
                nextFrame = observedStart;
            long start = nextFrame;
            nextFrame += frames;
            if (Blocks.Count >= 256) { Interlocked.Increment(ref droppedBlocks); return; }
            Blocks.Enqueue(new Block { firstFrame = start, samples = (float[])data.Clone() });
        }
    }
}
#endif
