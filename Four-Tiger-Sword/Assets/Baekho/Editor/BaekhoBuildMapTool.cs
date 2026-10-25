// Optional adapter: copy to Assets/Baekho/Editor only when Unity MCP is installed.
// Kept outside Editor/ so the legacy ZIP remains independent of Unity MCP.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace StudioBaekho
{
    [InitializeOnLoad]
    [McpForUnityTool("baekho_build_map", Description = "Build a Baekho map prefab and baked NavMesh without modifying open scenes, or query a durable request receipt.")]
    public static class BaekhoBuildMapTool
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private static readonly string SessionId = Guid.NewGuid().ToString("D");
        private static readonly StringComparison PathComparison =
            Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static string activeRequest;

        // The public nested class is how Unity MCP discovers a custom tool's schema.
        public sealed class Parameters
        {
            [ToolParameter("build or status")]
            public string action { get; set; }
            [ToolParameter("UUID identifying this request; reuse it only to query the same build")]
            public string requestId { get; set; }
            [ToolParameter("Absolute project root expected by the caller; required before any asset access")]
            public string expectedProjectRoot { get; set; }
            [ToolParameter("Assets/Baekho/Generated/<map>/Map.baekhomap.json; required for build", Required = false)]
            public string manifestPath { get; set; }
            [ToolParameter("SHA-256 of the exact manifest bytes; required for build", Required = false)]
            public string inputHash { get; set; }
        }

        private sealed class Receipt
        {
            public int schemaVersion = 1;
            public string requestId;
            public string projectRoot;
            public string manifestPath;
            public string inputHash;
            public string sessionId;
            public string state;
            public string message;
            public string prefabPath;
            public string navMeshPath;
            public string error;
            public string updatedAt;
        }

        static BaekhoBuildMapTool() { }

        public static object HandleCommand(JObject parameters)
        {
            string requestId = null;
            string action = null;
            try
            {
                action = StringValue(parameters, "action");
                requestId = StringValue(parameters, "requestId");
                string projectRoot = RequireProject(StringValue(parameters, "expectedProjectRoot"));
                Guid uuid;
                if (!Guid.TryParseExact(requestId, "D", out uuid))
                    throw new InvalidDataException("requestId must be a UUID in dashed format.");
                requestId = uuid.ToString("D");
                if (action != "build" && action != "status")
                    throw new InvalidDataException("action must be build or status.");
                string receiptPath = ReceiptPath(projectRoot, requestId);
                Receipt existing = ReadReceipt(receiptPath);
                if (existing != null)
                {
                    VerifyIdentity(existing, projectRoot, parameters);
                    if (existing.state == "running" && (existing.sessionId != SessionId || activeRequest != existing.requestId))
                    {
                        existing.state = "unknown";
                        existing.message = "Unity restarted or reloaded while this build was running. Inspect the output; this request will never run again.";
                        WriteReceipt(receiptPath, existing);
                    }
                    return Respond(existing);
                }
                if (action == "status")
                    return Respond(requestId, "unknown", "No Unity receipt exists for this request. A lost build response must not be retried automatically.");
                if (activeRequest != null)
                    throw new InvalidOperationException("Another Baekho build is already running: " + activeRequest);
                RequireReadyEditor();
                string manifestPath = StringValue(parameters, "manifestPath");
                string absoluteManifest = RequireManifest(projectRoot, manifestPath);
                string inputHash = StringValue(parameters, "inputHash");
                if (inputHash == null || !Regex.IsMatch(inputHash, "\\A[0-9a-fA-F]{64}\\z"))
                    throw new InvalidDataException("inputHash must be a SHA-256 hex digest.");
                string digest = HashManifest(absoluteManifest);
                if (!string.Equals(digest, inputHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Manifest SHA-256 does not match inputHash. No prefab was generated.");
                ImportManifest(manifestPath);
                RequireReadyEditor();
                var receipt = new Receipt
                {
                    requestId = requestId, projectRoot = projectRoot, manifestPath = manifestPath,
                    inputHash = digest, sessionId = SessionId, state = "running",
                    message = "Assembling map, baking NavMesh and saving prefab."
                };
                // Record admission durably before invoking the importer. A second request ID can
                // produce a new version; this request ID can never execute a second build.
                WriteReceipt(receiptPath, receipt);
                activeRequest = requestId;
                bool importerCompleted = false;
                try
                {
                    receipt.prefabPath = BaekhoMapImporter.BuildFromPath(manifestPath, true);
                    importerCompleted = true;
                    receipt.navMeshPath = FindNavMesh(receipt.prefabPath);
                    receipt.state = "completed";
                    receipt.message = "Prefab and baked NavMesh saved.";
                    WriteReceipt(receiptPath, receipt);
                    GameObject output = AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefabPath);
                    if (output != null)
                    {
                        // Selection in the Project window never places the prefab in a scene.
                        Selection.activeObject = output;
                        EditorGUIUtility.PingObject(output);
                    }
                }
                catch (Exception exception)
                {
                    receipt.state = importerCompleted ? "unknown" : "failed";
                    receipt.message = importerCompleted
                        ? "The importer saved the prefab, but completion could not be confirmed. Inspect its output before starting a new request."
                        : "Unity could not generate this map prefab.";
                    receipt.error = exception.Message;
                    try { WriteReceipt(receiptPath, receipt); }
                    catch (Exception persistenceError) { Debug.LogError("Baekho request receipt could not be saved: " + persistenceError.Message); }
                }
                finally { activeRequest = null; }
                return Respond(receipt);
            }
            catch (Exception exception)
            {
                // Failure to read or identify a durable request does not establish
                // whether its admitted build completed. Keep the caller in recovery
                // until it can read a trustworthy receipt; never authorize a retry.
                return Respond(requestId, action == "status" ? "unknown" : "failed", exception.Message, exception.Message);
            }
        }

        private static string StringValue(JObject parameters, string name)
        {
            JToken token = parameters == null ? null : parameters[name];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String) throw new InvalidDataException(name + " must be a string.");
            return token.Value<string>();
        }

        private static string RequireProject(string expected)
        {
            if (string.IsNullOrWhiteSpace(expected) || !Path.IsPathRooted(expected))
                throw new InvalidDataException("expectedProjectRoot must be an absolute Unity project path.");
            string actual = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalized = Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(actual, normalized, PathComparison))
                throw new InvalidOperationException("Unity project mismatch. Expected " + normalized + "; connected to " + actual + ".");
            RequireNoReparseAncestors(actual);
            return actual;
        }

        private static string RequireManifest(string projectRoot, string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath) || manifestPath.IndexOf('\\') >= 0 ||
                !manifestPath.StartsWith("Assets/Baekho/Generated/", StringComparison.Ordinal) ||
                !manifestPath.EndsWith("/Map.baekhomap.json", StringComparison.Ordinal) ||
                manifestPath.IndexOf(':') >= 0)
                throw new InvalidDataException("manifestPath must be Assets/Baekho/Generated/<map>/Map.baekhomap.json.");
            foreach (string segment in manifestPath.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.TrimEnd(' ', '.') != segment)
                    throw new InvalidDataException("manifestPath contains an unsafe path segment.");
            string absolute = Path.GetFullPath(Path.Combine(projectRoot, manifestPath));
            string generated = Path.Combine(projectRoot, "Assets", "Baekho", "Generated") + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(generated, PathComparison))
                throw new InvalidDataException("Manifest must stay inside Assets/Baekho/Generated.");
            RequireNoReparseAncestors(absolute);
            // The importer writes siblings next to the manifest, so this also protects
            // every existing output directory before GenerateUniqueAssetPath is used.
            return absolute;
        }

        private static void RequireNoReparseAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symbolic links and junctions are not allowed in Unity build paths: " + current);
                string parent = Path.GetDirectoryName(current);
                if (parent == current) break;
                current = parent;
            }
        }

        private static void RequireReadyEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building a map prefab.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for Unity to finish compiling and importing assets.");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Fix Unity script compilation errors before building a map prefab.");
        }

        private static string HashManifest(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > MaxBytes)
                    throw new InvalidDataException("Manifest must contain between 1 byte and 8 MiB.");
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void ImportManifest(string manifestPath)
        {
            // Files delivered by Electron can arrive while Unity is unfocused and
            // auto-refresh is disabled. Register each new directory explicitly;
            // import only the JSON rather than refreshing unrelated project scripts.
            string[] segments = manifestPath.Split('/');
            string folder = segments[0];
            for (int index = 1; index < segments.Length - 1; index++)
            {
                folder += "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.ImportAsset(folder, ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.IsValidFolder(folder))
                throw new IOException("Unity could not import the generated map directory: " + folder);
        }

        private static string ReceiptPath(string projectRoot, string requestId)
        {
            string path = Path.Combine(projectRoot, "Library", "BaekhoMapForge", "requests", requestId + ".json");
            RequireNoReparseAncestors(path);
            return path;
        }

        private static Receipt ReadReceipt(string path)
        {
            if (!File.Exists(path)) return null;
            // A corrupt receipt is never treated as an unstarted request.
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Unity request receipt is oversized; inspect it before continuing.");
            Receipt receipt = JsonConvert.DeserializeObject<Receipt>(File.ReadAllText(path, Encoding.UTF8));
            if (receipt == null || receipt.schemaVersion != 1 || string.IsNullOrEmpty(receipt.requestId) ||
                (receipt.state != "running" && receipt.state != "completed" && receipt.state != "failed" && receipt.state != "unknown"))
                throw new InvalidDataException("Unity request receipt is invalid; inspect it before continuing.");
            return receipt;
        }

        private static void VerifyIdentity(Receipt receipt, string projectRoot, JObject parameters)
        {
            if (!string.Equals(receipt.projectRoot, projectRoot, PathComparison) ||
                !string.Equals(receipt.requestId, StringValue(parameters, "requestId"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unity receipt identity does not match this project and request.");
            string path = StringValue(parameters, "manifestPath");
            string hash = StringValue(parameters, "inputHash");
            if ((path != null && !string.Equals(receipt.manifestPath, path, StringComparison.Ordinal)) ||
                (hash != null && !string.Equals(receipt.inputHash, hash, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This requestId already belongs to a different manifest path or input hash. It will not be rebuilt.");
            if (StringValue(parameters, "action") == "build" && (path == null || hash == null))
                throw new InvalidDataException("build requires manifestPath and inputHash, including for a repeated request.");
        }

        private static void WriteReceipt(string path, Receipt receipt)
        {
            RequireNoReparseAncestors(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            receipt.updatedAt = DateTime.UtcNow.ToString("o");
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(receipt, Formatting.Indented));
            // Windows indexers can briefly deny ReplaceFile's delete sharing. Retry
            // the atomic replacement, never fall back to truncating the live receipt.
            for (int attempt = 0; ; attempt++)
            {
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    return;
                }
                catch (IOException) { if (attempt >= 4) throw; Thread.Sleep(25 << attempt); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        private static string FindNavMesh(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new IOException("Generated prefab could not be reloaded.");
            foreach (Component component in prefab.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != "Unity.AI.Navigation.NavMeshSurface") continue;
                var property = component.GetType().GetProperty("navMeshData");
                var data = property == null ? null : property.GetValue(component, null) as NavMeshData;
                string path = data == null ? null : AssetDatabase.GetAssetPath(data);
                if (!string.IsNullOrEmpty(path) && File.Exists(Path.GetFullPath(path))) return path;
            }
            throw new IOException("The generated prefab has no persisted NavMesh data.");
        }

        private static object Respond(Receipt receipt)
        {
            return new SuccessResponse(receipt.message, new
            {
                receipt.requestId, receipt.state, receipt.message, receipt.prefabPath,
                receipt.navMeshPath, receipt.error, receipt.manifestPath, receipt.inputHash
            });
        }

        private static object Respond(string requestId, string state, string message, string error = null)
        {
            return new SuccessResponse(message, new { requestId, state, message, error });
        }
    }
}
