using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    [Serializable] public sealed class CombatSource
    {
        public string guid;
        public string label;
    }

    [Serializable] public sealed class CombatAction
    {
        public string id;
        public string label;
        public bool available = true;
        public string unavailableReason = "";
    }

    [Serializable] public sealed class ParameterSpec
    {
        public string id;
        public string label;
        public string unit;
        public string description;
        public float minimum;
        public float maximum;
        public float baseline;
        // v2: baseline is the observed absolute value, not a search boundary.
        public string targetId;
        public string group;
        public string kind = "number";
        public string[] options = new string[0];
        public bool useSuggestedRange;
        public float suggestedMinimum;
        public float suggestedMaximum;
    }

    [Serializable] public sealed class CurveKey
    {
        public float time;
        public float value;
        public CurveKey() { }
        public CurveKey(float time, float value) { this.time = time; this.value = value; }
    }
    [Serializable] public sealed class CurveSpec
    {
        public string id, label, targetId, group, description;
        public string timeUnit = "progress";
        public float minimum = -float.MaxValue, maximum = float.MaxValue, maximumTime = 1f;
        public int maximumKeys = 16;
        public List<CurveKey> keys = new List<CurveKey>();
    }
    [Serializable] public sealed class CurveValue
    {
        public string id;
        public List<CurveKey> keys = new List<CurveKey>();
    }

    [Serializable] public sealed class ParameterValue
    {
        public string id;
        public float value;
        public ParameterValue() { }
        public ParameterValue(string id, float value) { this.id = id; this.value = value; }
    }

    [Serializable] public sealed class CombatCandidate
    {
        public string id;
        public string label;
        public string rationale;
        public List<ParameterValue> values = new List<ParameterValue>();
        public List<CurveValue> curves = new List<CurveValue>();
        public string origin;
        public int revision;
        public bool isSourceControl;
    }

    [Serializable] public sealed class CandidateGeneration
    {
        public string provider;
        public string model;
        public string goal;
        public int count;
        public string createdUtc;
        public string mode, strategy, recipeId, referenceCandidateId, feedback;
        public string guideVersion, guideHash, guideSnapshot, recipesSnapshot;
        public string observationsJson;
        public CombatCandidate referenceCandidate;
        public string[] lockedIds = new string[0];
    }

    [Serializable] public sealed class ExperimentRequest
    {
        public int schemaVersion = 1;
        public string adapterId;
        public string sourceGuid;
        // Omitted in older requests: the adapter's original default action.
        public string actionId;
        public int seed = 1701;
        public float durationSeconds = 10f;
        public bool recordVideo = true;
        public CandidateGeneration generation;
        public List<CombatCandidate> candidates = new List<CombatCandidate>();
        public bool includeSourceControl;
        public string scenarioMode = "live";
        public bool precisionCapture;
        public string precisionTargetId;
        public int repeatCount = 1;
        public string sourceHash;
    }

    [Serializable] public sealed class CombatActionSelection
    {
        public ExperimentRequest request;
        public string candidateId;
    }

    [Serializable] public sealed class CombinedExportRequest
    {
        public int schemaVersion = 1;
        public string adapterId;
        public string sourceGuid;
        public List<CombatActionSelection> selections = new List<CombatActionSelection>();
    }

    [Serializable] public sealed class CombatEvent
    {
        public float realTime;
        public float gameTime;
        public string kind;
        public string detail;
        public float value;
        public string eventId;
        public string targetId;
        public int unityFrameCount;
        public int ownerInstanceId, targetInstanceId;
        public double unityRealtimeUs;
        public Vector3 position, rotation;
        public float fov;
    }

    [Serializable] public sealed class CombatMetrics
    {
        public int attackRequests;
        public int hits;
        public float damageDealt;
        public float damageTaken;
        public float firstHitSeconds = -1f;
        public float elapsedSeconds;
        public List<CombatEvent> events = new List<CombatEvent>();
        public List<ParameterValue> appliedValues = new List<ParameterValue>();
        public string validationStatus;
        public int expectedHitEvents, confirmedHitEvents;
        public List<string> notes = new List<string>();
    }

    public interface ICombatLabAdapter
    {
        string Id { get; }
        string DisplayName { get; }
        List<CombatSource> DiscoverSources();
        List<ParameterSpec> Describe(string sourceGuid);
        void Validate(ExperimentRequest request);
        ICombatLabSession Begin(ExperimentRequest request, CombatCandidate candidate);
        // Write a new asset only. Never overwrite the source.
        UnityEngine.Object Export(ExperimentRequest request, CombatCandidate candidate, string assetPath);
    }

    public interface ICombatLabSession : IDisposable
    {
        void Tick(float unscaledDeltaTime);
        bool Complete { get; }
        CombatMetrics Metrics { get; }
    }

    // Optional capability: existing project adapters remain source-compatible.
    public interface ICombatLabActionAdapter : ICombatLabAdapter
    {
        List<CombatAction> DiscoverActions(string sourceGuid);
        List<ParameterSpec> Describe(string sourceGuid, string actionId);
    }

    // Optional capability: combine one chosen candidate per action into a single new form asset.
    // Each candidate is relative to the same original source; preserve all unselected actions.
    public interface ICombatLabMultiActionAdapter : ICombatLabAdapter
    {
        UnityEngine.Object ExportCombined(CombinedExportRequest request, string assetPath);
    }

    // Optional v2 capability: v1 adapters and numeric JSON keep their original semantics.
    public interface ICombatLabFeedbackAdapter : ICombatLabActionAdapter
    {
        List<ParameterSpec> DescribeFeedback(string sourceGuid, string actionId);
        List<CurveSpec> DescribeCurves(string sourceGuid, string actionId);
        string DescribeContext(string sourceGuid, string actionId);
    }

    // Recording is armed before any automated action is allowed to begin.
    public interface ICombatLabArmedSession : ICombatLabSession
    {
        void Arm();
    }

    public interface ICombatLabReadyAdapter
    {
        bool IsReady(ExperimentRequest request, out string reason);
    }

    // Runtime-only preparation, called from sceneLoaded before scene Start methods.
    public interface ICombatLabEarlyPlayAdapter
    {
        void PrepareBeforeSceneStart(ExperimentRequest request);
    }

    // May wait for procedural startup. Once true, the runner does not prepare again.
    public interface ICombatLabScenePreparation
    {
        bool PrepareScenario(ExperimentRequest request, out string reason);
    }

    // Optional settling gate between runtime fixture setup and recording/input Arm.
    public interface ICombatLabReadySession
    {
        bool IsReadyToArm(out string reason);
    }

}
