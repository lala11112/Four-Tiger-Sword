using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace StudioBaekho.AutoHitTimerBootstrap
{
    // Bootstrap has no package references, so it can compile before the connection package exists.
    [InitializeOnLoad]
    public static class AutoHitTimerInstaller
    {
        private static AddRequest request;
        private static double nextPoll;
        private static double deadline;
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static string RequestPath => Path.Combine(Root, "Library/AutoHitTimerInstall.json");
        static AutoHitTimerInstaller() { EditorApplication.update += Update; }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            try
            {
                if (request != null)
                {
                    if (!request.IsCompleted)
                    {
                        if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Package Manager 설치 제한 시간을 초과했습니다.");
                        return;
                    }
                    if (request.Status != StatusCode.Success) throw new InvalidOperationException(request.Error?.message ?? "패키지 설치 실패");
                    request = null;
                    Finish();
                    return;
                }
                if (!File.Exists(RequestPath)) return;
                var packagePath = Path.Combine(Root, "AutoHitTimerIntegration/Package").Replace('\\', '/');
                if (!File.Exists(Path.Combine(packagePath, "package.json"))) throw new InvalidOperationException("AutoHitTimer 설치 패키지가 없습니다.");
                var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(item => item.name == "com.studiobaekho.auto-hit-timer");
                if (installed != null && string.Equals(Path.GetFullPath(installed.resolvedPath), Path.GetFullPath(packagePath), StringComparison.OrdinalIgnoreCase)) { Finish(); return; }
                deadline = EditorApplication.timeSinceStartup + 600;
                request = Client.Add("file:" + packagePath);
            }
            catch (Exception error)
            {
                request = null;
                WriteReceipt(false, error.Message);
                if (File.Exists(RequestPath)) File.Move(RequestPath, RequestPath + ".failed-" + DateTime.UtcNow.Ticks);
                Debug.LogError("[AutoHitTimer] " + error.Message);
            }
        }

        private static void Finish()
        {
            string source = Path.Combine(Root, "AutoHitTimerIntegration/Adapter");
            string destination = Path.Combine(Application.dataPath, "StudioBaekho/AutoHitTimer/Editor");
            if (!Directory.Exists(source)) throw new InvalidOperationException("사인검 어댑터 설치 파일이 없습니다.");
            AssetDatabase.StartAssetEditing();
            try
            {
                RemoveRetiredAdapterFile("FourTigerSlashMapping.cs");
                RemoveRetiredAdapterFile("FourTigerSlashMapping.cs.meta");
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, true);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            // Remove the request before domain reload, so the same installation cannot loop.
            File.Delete(RequestPath);
            WriteReceipt(true, "AutoHitTimer 패키지와 사인검 어댑터 설치 완료. 컴파일을 기다리세요.");
            AssetDatabase.Refresh();
        }

        private static void RemoveRetiredAdapterFile(string filename)
        {
            string root = Path.GetFullPath(Root);
            string target = Path.GetFullPath(Path.Combine(root, "Assets/StudioBaekho/AutoHitTimer/Editor", filename));
            if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("정리 경로가 프로젝트 밖입니다.");
            for (string current = target; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("연결된 경로의 파일을 정리할 수 없습니다: " + current);
                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            if (Directory.Exists(target)) throw new InvalidOperationException("정리 대상이 파일이 아닙니다: " + target);
            if (File.Exists(target)) File.Delete(target);
        }
        [Serializable] private class Receipt { public bool success; public string message; public string utc; }
        private static void WriteReceipt(bool success, string message)
        {
            string target = Path.Combine(Root, "Library/AutoHitTimerInstallResult.json");
            File.WriteAllText(target, JsonUtility.ToJson(new Receipt { success = success, message = message, utc = DateTime.UtcNow.ToString("O") }, true));
        }
    }
}
