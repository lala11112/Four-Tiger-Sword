using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StudioBaekho.CombatLab;
using UnityEngine;

// Pure metadata regression. Does not enter Play Mode or change scene/assets.
public static class CombatLabFixtureConditionsRegression
{
    [Serializable] private sealed class Result { public int checks; public string[] passed; }
    public static string Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string description)
        { if (!condition) throw new InvalidOperationException(description); passed.Add(description); }
        var report = Report(true);
        var source = Sample("source-control"); source.candidate.isSourceControl = true;
        CombatLabRunner.ValidateFixtureConditions(report, source);
        Check(source.fixtureValidation.status == "baseline" && source.status == "completed", "Observed source control establishes the reference without changing execution status");
        report.controlResult = source;
        var match = Sample("matched");
        Set(source, "runtime.fixture.playerYaw", 359.98f); Set(match, "runtime.fixture.playerYaw", .02f);
        Set(match, "runtime.player.start.x", .01f); Set(match, "runtime.fixture.cameraFov", 60.09f);
        CombatLabRunner.ValidateFixtureConditions(report, match);
        Check(match.fixtureValidation.status == "matched" && match.status == "completed", "Position/FOV tolerances and wrapped yaw accept equivalent observed conditions");
        Check(match.fixtureValidation.referenceCandidateId == "source-control", "Source control remains the reference for all candidates");
        var position = Sample("position");
        Set(position, "runtime.fixture.target.x", 2.015f); Set(position, "runtime.fixture.target.z", 2.015f);
        CombatLabRunner.ValidateFixtureConditions(report, position);
        Check(position.status == "conditions-mismatch" && position.fixtureValidation.differences.Any(d => d.id == "target-position"),
            "Position comparison uses vector distance rather than accepting three individually small axis deviations");
        var target = Sample("target"); target.metrics.events[0].detail = "scene:Room[000001]/Enemy[000002]";
        CombatLabRunner.ValidateFixtureConditions(report, target);
        Check(target.status == "conditions-mismatch" && target.fixtureValidation.differences.Any(d => d.id == "fixed-target-identity"),
            "Different stable target identity is not presented as a normal same-condition result");
        var fov = Sample("fov"); Set(fov, "runtime.fixture.cameraFov", 61);
        CombatLabRunner.ValidateFixtureConditions(report, fov);
        Check(fov.status == "conditions-mismatch" && fov.fixtureValidation.differences.Any(d => d.id.EndsWith("cameraFov")), "Camera FOV mismatch is recorded with requested tolerance");
        var rotation = Sample("camera-rotation"); Set(rotation, "runtime.fixture.cameraPitch", 1);
        CombatLabRunner.ValidateFixtureConditions(report, rotation);
        Check(rotation.status == "conditions-mismatch" && rotation.fixtureValidation.differences.Any(d => d.id.EndsWith("cameraPitch")), "Camera pitch is compared rather than silently checking yaw alone");
        var roll = Sample("camera-roll"); Set(roll, "runtime.fixture.cameraRoll", 359.95f);
        CombatLabRunner.ValidateFixtureConditions(report, roll);
        Check(roll.fixtureValidation.status == "matched", "Camera roll comparison handles wrapped Euler angles");
        var missing = Sample("missing"); missing.metrics.appliedValues.RemoveAll(v => v.id == "runtime.fixture.camera.x");
        CombatLabRunner.ValidateFixtureConditions(report, missing);
        Check(missing.status == "conditions-unavailable", "Missing camera observation cannot pass as a matched condition");
        var duplicate = Sample("duplicate"); duplicate.metrics.appliedValues.Add(new ParameterValue("runtime.fixture.camera.x", 1));
        CombatLabRunner.ValidateFixtureConditions(report, duplicate);
        Check(duplicate.status == "conditions-unavailable" && duplicate.fixtureValidation.differences.Any(d => d.reason == "actual-duplicate"), "Ambiguous duplicate fixture metrics are rejected");
        var invalid = Sample("invalid"); Set(invalid, "runtime.fixture.cameraFov", float.NaN);
        CombatLabRunner.ValidateFixtureConditions(report, invalid);
        Check(invalid.status == "conditions-unavailable", "Nonfinite observed fixture values never pass condition verification");
        var noControl = Report(true); noControl.results.Add(Sample("unrelated-first"));
        var withoutReference = Sample("missing-control");
        CombatLabRunner.ValidateFixtureConditions(noControl, withoutReference);
        Check(withoutReference.status == "conditions-unavailable", "A requested but missing source control is not silently replaced by a candidate");
        var candidatesOnly = Report(false);
        var first = Sample("first"); CombatLabRunner.ValidateFixtureConditions(candidatesOnly, first); candidatesOnly.results.Add(first);
        var second = Sample("second"); second.repeatIndex = 3;
        CombatLabRunner.ValidateFixtureConditions(candidatesOnly, second);
        Check(first.fixtureValidation.status == "baseline" && second.fixtureValidation.referenceCandidateId == "first",
            "Without source control, the first candidate execution is the stable reference across repeats");
        var failed = Sample("failed"); failed.status = "failed"; Set(failed, "runtime.fixture.target.x", 9);
        CombatLabRunner.ValidateFixtureConditions(report, failed);
        Check(failed.status == "failed" && failed.fixtureValidation.status == "mismatch" && failed.fixtureValidation.executionStatus == "failed",
            "Condition reporting preserves a stronger original failure status");
        var unavailableReference = Report(true); unavailableReference.controlResult = Sample("broken-control");
        unavailableReference.controlResult.metrics.events.Clear();
        var candidate = Sample("candidate"); CombatLabRunner.ValidateFixtureConditions(unavailableReference, candidate);
        Check(candidate.status == "conditions-unavailable", "An unobserved reference identity invalidates comparison instead of choosing a later baseline");
        var live = Report(false); live.request.scenarioMode = "live";
        var liveResult = Sample("live"); CombatLabRunner.ValidateFixtureConditions(live, liveResult);
        Check(liveResult.status == "completed" && liveResult.fixtureValidation.status == "not-requested", "Live results retain their existing interpretation");
        var legacy = Report(false); legacy.request.schemaVersion = 1;
        var legacyResult = Sample("v1"); CombatLabRunner.ValidateFixtureConditions(legacy, legacyResult);
        Check(legacyResult.status == "completed" && legacyResult.fixtureValidation.status == "not-requested", "V1 requests are not reinterpreted as fixed v2 comparisons");

        string directory = Path.Combine(Path.GetTempPath(), "CombatLabContextRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string context = "{\"actionId\":\"normal-1\",\"note\":\"원본 컨텍스트\"}";
            typeof(CombatLabRunner).GetMethod("PersistSourceContext", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { report, directory, context });
            Check(File.ReadAllText(Path.Combine(directory, report.sourceContextPath)) == context && report.sourceContext == context,
                "The exact adapter source context is retained both on disk and in the report");
            Check(report.sourceContextHash.Length == 64 && DateTime.TryParse(report.sourceContextCapturedUtc, out _),
                "Source context provenance includes a SHA-256 and capture time");
            report.results.Add(position);
            var roundTrip = JsonUtility.FromJson<ExperimentReport>(JsonUtility.ToJson(report));
            Check(roundTrip.sourceContext == context && roundTrip.results[0].fixtureValidation.status == "mismatch"
                && roundTrip.results[0].fixtureValidation.differences.Count > 0,
                "JSON report round-trip preserves context, condition differences and reference metadata");
        }
        finally
        {
            string contextFile = Path.Combine(directory, "source-context.json");
            if (File.Exists(contextFile)) File.Delete(contextFile);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
        return JsonUtility.ToJson(new Result { checks = passed.Count, passed = passed.ToArray() });
    }

    private static ExperimentReport Report(bool control)
        => new ExperimentReport { request = new ExperimentRequest { schemaVersion = 2, scenarioMode = "fixed", includeSourceControl = control } };
    private static CandidateResult Sample(string id)
    {
        var result = new CandidateResult { candidate = new CombatCandidate { id = id, label = id }, status = "completed", metrics = new CombatMetrics() };
        foreach (var item in new[] {
            new ParameterValue("runtime.player.start.x", 0), new ParameterValue("runtime.player.start.y", 0), new ParameterValue("runtime.player.start.z", 0),
            new ParameterValue("runtime.fixture.playerYaw", 0), new ParameterValue("runtime.fixture.target.x", 2), new ParameterValue("runtime.fixture.target.y", 0), new ParameterValue("runtime.fixture.target.z", 2),
            new ParameterValue("runtime.fixture.camera.x", 0), new ParameterValue("runtime.fixture.camera.y", 3), new ParameterValue("runtime.fixture.camera.z", -5),
            new ParameterValue("runtime.fixture.cameraPitch", 0), new ParameterValue("runtime.fixture.cameraYaw", 0),
            new ParameterValue("runtime.fixture.cameraRoll", 0), new ParameterValue("runtime.fixture.cameraFov", 60) }) result.metrics.appliedValues.Add(item);
        result.metrics.events.Add(new CombatEvent { kind = "fixed-target-ready", detail = "scene:Room[000001]/Enemy[000001]" });
        return result;
    }
    private static void Set(CandidateResult result, string id, float value)
        => result.metrics.appliedValues.Single(item => item.id == id).value = value;
}
