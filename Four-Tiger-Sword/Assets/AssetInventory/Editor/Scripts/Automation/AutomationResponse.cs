using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AssetInventory.Automation
{
    internal sealed class AutomationResponse
    {
        public bool Succeeded { get; }
        public string Message { get; }
        public string ErrorCode { get; }
        public object Data { get; }

        private AutomationResponse(bool succeeded, string message, string errorCode, object data)
        {
            Succeeded = succeeded;
            Message = message;
            ErrorCode = errorCode;
            Data = data;
        }

        internal static AutomationResponse Success(string message, object data = null)
        {
            return new AutomationResponse(true, message, null, data);
        }

        internal static AutomationResponse Error(string message, object data = null, string errorCode = "operation_failed")
        {
            return new AutomationResponse(false, message, errorCode, data);
        }
    }

    internal sealed class MutationPreview
    {
        public bool dryRun { get; }
        public string operation { get; }
        public string confirmationToken { get; }
        public object preview { get; }

        internal MutationPreview(string operationName, string token, object previewData)
        {
            dryRun = true;
            operation = operationName;
            confirmationToken = token;
            preview = previewData;
        }
    }

    internal static class AutomationMutationGuard
    {
        internal static AutomationResponse RequireConfirmation(string operation, bool dryRun, string confirmationToken, object preview, params string[] scopeParts)
        {
            string expectedToken = CreateToken(operation, scopeParts);
            MutationPreview previewResult = new MutationPreview(operation, expectedToken, preview);

            if (dryRun)
            {
                return AutomationResponse.Success($"Dry run complete for '{operation}'. Re-submit with DryRun=false and the returned ConfirmationToken to execute.", previewResult);
            }

            if (string.IsNullOrWhiteSpace(confirmationToken))
            {
                return AutomationResponse.Error($"ConfirmationToken is required to execute '{operation}'.", previewResult, "confirmation_required");
            }

            if (!FixedTimeEquals(expectedToken, confirmationToken.Trim()))
            {
                return AutomationResponse.Error($"ConfirmationToken no longer matches the requested '{operation}' scope. Run a new dry run before executing.", previewResult, "confirmation_mismatch");
            }

            return null;
        }

        internal static string CreateToken(string operation, params string[] scopeParts)
        {
            string[] safeParts = scopeParts ?? Array.Empty<string>();
            string canonicalScope = string.Join("|", safeParts.Select(part =>
            {
                string value = part ?? string.Empty;
                return $"{value.Length}:{value}";
            }));
            byte[] bytes = Encoding.UTF8.GetBytes($"AssetInventoryAutomation:v1|{operation}|{canonicalScope}");
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            if (expected.Length != actual.Length) return false;

            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                difference |= expected[i] ^ actual[i];
            }
            return difference == 0;
        }
    }
}
