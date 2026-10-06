using System;

namespace AudioTool
{
    internal sealed class AudioWaveformPeakCache
    {
        private const int BucketCount = 8192;

        private float[][] _minimum;
        private float[][] _maximum;

        internal int Channels { get; private set; }
        internal int SampleFrames { get; private set; }

        internal void Build(float[] samples, int channels)
        {
            Channels = Math.Max(1, channels);
            SampleFrames = samples == null ? 0 : samples.Length / Channels;
            int count = Math.Min(BucketCount, Math.Max(1, SampleFrames));
            _minimum = new float[Channels][];
            _maximum = new float[Channels][];

            for (int channel = 0; channel < Channels; channel++)
            {
                _minimum[channel] = new float[count];
                _maximum[channel] = new float[count];
                for (int bucket = 0; bucket < count; bucket++)
                {
                    int start = (int)((long)bucket * SampleFrames / count);
                    int end = Math.Max(start + 1, (int)((long)(bucket + 1) * SampleFrames / count));
                    float minimum = 0f;
                    float maximum = 0f;
                    for (int frame = start; frame < end && frame < SampleFrames; frame++)
                    {
                        float sample = samples[frame * Channels + channel];
                        minimum = Math.Min(minimum, sample);
                        maximum = Math.Max(maximum, sample);
                    }

                    _minimum[channel][bucket] = minimum;
                    _maximum[channel][bucket] = maximum;
                }
            }
        }

        internal void GetRange(int channel, float normalizedStart, float normalizedEnd, out float minimum, out float maximum)
        {
            minimum = 0f;
            maximum = 0f;
            if (_minimum == null || channel < 0 || channel >= Channels) return;

            int count = _minimum[channel].Length;
            int start = Clamp((int)Math.Floor(normalizedStart * count), 0, count - 1);
            int end = Clamp((int)Math.Ceiling(normalizedEnd * count), start + 1, count);
            for (int i = start; i < end; i++)
            {
                minimum = Math.Min(minimum, _minimum[channel][i]);
                maximum = Math.Max(maximum, _maximum[channel][i]);
            }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
