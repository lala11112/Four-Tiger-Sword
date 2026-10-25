using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StudioBaekho.CombatLab;
using UnityEngine;

public static class CombatLabReportRegression
{
    public static string Run()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "CombatLabResults",
            "report-io-check-" + Guid.NewGuid().ToString("N")));
        var report = new ExperimentReport { runId = "initial" };
        CombatLabReport.Write(folder, report);
        string path = Path.Combine(folder, "report.json");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(60); reader.Dispose(); });
        try
        {
            report.runId = "updated";
            report.notes.Add("</script><img src=x onerror=alert(1)>");
            CombatLabReport.Write(folder, report);
            if (JsonUtility.FromJson<ExperimentReport>(File.ReadAllText(path)).runId != "updated")
                throw new Exception("Report update lost during temporary read lock.");
            var html = File.ReadAllText(Path.Combine(folder, "index.html"));
            if (html.Contains("</script><img")) throw new Exception("Unsafe embedded report JSON.");
            if (Directory.GetFiles(folder, "*.tmp").Length != 0) throw new Exception("Leaked temporary files.");
            return "Report update passed with a temporary Windows reader lock; JSON/HTML match, script data escaped, no temp-file leak.";
        }
        finally { release.GetAwaiter().GetResult(); reader.Dispose(); }
    }
}
