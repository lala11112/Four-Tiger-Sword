using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    internal enum APIResponseStatus
    {
        Success,
        NotModified,
        AuthenticationFailure,
        Failure
    }

    internal sealed class APIResponse<T>
    {
        internal T Data;
        internal APIResponseStatus Status = APIResponseStatus.Failure;
        internal bool Succeeded => Status == APIResponseStatus.Success || Status == APIResponseStatus.NotModified;
    }

    // Owned by the Editor session, including domain reloads. Credentials are never stored in SessionState.
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal sealed class AssetStoreAuthentication
    {
        private const string RejectedTokenKey = "AssetInventory.RejectedStoreToken";
        private const string WarningKey = "AssetInventory.StoreAuthenticationWarning";
        private readonly Func<string> _token;
        private readonly Func<string, string> _read;
        private readonly Action<string, string> _write;
        private readonly Func<bool> _logWarnings;
        private readonly Action<string> _warn;

        internal static readonly AssetStoreAuthentication Current = new AssetStoreAuthentication(
            () => CloudProjectSettings.accessToken, key => SessionState.GetString(key, string.Empty),
            SessionState.SetString, () => AI.Config.logAssetStoreAuthenticationWarnings, Debug.LogWarning);

        internal AssetStoreAuthentication(Func<string> token, Func<string, string> read, Action<string, string> write,
            Func<bool> logWarnings, Action<string> warn)
        {
            _token = token;
            _read = read;
            _write = write;
            _logWarnings = logWarnings;
            _warn = warn;
        }

        internal bool CanRequest(bool suppressErrors = false)
        {
            return CanRequestToken(_token(), suppressErrors);
        }

        internal bool CanAutoRefresh(bool isBatchMode, bool suppressErrors = false)
        {
            return !isBatchMode && CanRequest(suppressErrors);
        }

        private bool CanRequestToken(string token, bool suppressErrors)
        {
            if (!string.IsNullOrEmpty(token) && Fingerprint(token) != _read(RejectedTokenKey)) return true;
            Warn(suppressErrors);
            return false;
        }

        internal async Task<APIResponse<T>> Request<T>(Func<string, Task<APIResponse<T>>> send, bool suppressErrors = false)
        {
            string token = _token();
            if (!CanRequestToken(token, suppressErrors)) return new APIResponse<T> {Status = APIResponseStatus.AuthenticationFailure};
            APIResponse<T> response = await send(token);
            if (response.Status == APIResponseStatus.AuthenticationFailure)
            {
                // An in-flight response for old credentials must not poison a newly authenticated session.
                if (token == _token())
                {
                    _write(RejectedTokenKey, Fingerprint(token));
                    Warn(suppressErrors);
                }
            }
            return response;
        }

        private void Warn(bool suppressErrors)
        {
            if (suppressErrors || !_logWarnings() || _read(WarningKey) == "shown") return;
            _write(WarningKey, "shown");
            _warn("Asset Inventory: Asset Store refresh is paused because Unity authentication is missing or expired. Sign in to Unity Hub and reopen the Editor if needed. Disable 'Asset Store Authentication Warnings' in Settings > Advanced > Diagnostics to suppress this warning.");
        }

        private static string Fingerprint(string token)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(token)));
            }
        }
    }
}
