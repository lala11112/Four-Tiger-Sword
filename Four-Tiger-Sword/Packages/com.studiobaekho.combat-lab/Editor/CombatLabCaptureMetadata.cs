using System;
using System.Collections.Generic;

namespace StudioBaekho.CombatLab
{
    [Serializable]
    public sealed class CaptureFrameMetadata
    {
        public int captureIndex;
        public int unityFrameCount;
        public double unityRealtimeUs;
        public double captureElapsedUs;
        public double mediaPtsUs;
        public string kind = "captured";
        public string imagePath;
        public float relativeTimeSeconds;
    }

    [Serializable]
    public sealed class CaptureMetadata
    {
        public int schemaVersion = 1;
        public int timebase = 1000000;
        public int width = CombatLabRecording.Width;
        public int height = CombatLabRecording.Height;
        public string framesPath;
        public string precisionStatus = "disabled";
        public string hitEventId;
        public double hitRealtimeUs;
        public int capturedFrameCount;
        public double medianIntervalUs;
        public double maxIntervalUs;
        public int gapsOver33333Us;
        // The report holds retained PNG samples only. The sidecar holds every video sample.
        public List<CaptureFrameMetadata> frames = new List<CaptureFrameMetadata>();
    }
}
