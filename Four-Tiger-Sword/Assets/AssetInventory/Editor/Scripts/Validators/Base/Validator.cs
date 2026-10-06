using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Base contract for inspecting catalog and filesystem consistency and optionally applying reported repairs.</summary>
    public abstract class Validator
    {
        public enum ValidatorType
        {
            DB,
            FileSystem
        }

        public enum ValidatorSpeed
        {
            Fast,
            Slow
        }

        public enum State
        {
            Idle,
            Scanning,
            Completed,
            Fixing
        }

        public ValidatorType Type { get; protected set; }
        public ValidatorSpeed Speed { get; protected set; } = ValidatorSpeed.Fast;
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public bool Fixable { get; protected set; } = true;
        public string FixCaption { get; protected set; } = "Fix";
        public List<AssetInfo> DBIssues { get; set; } = new List<AssetInfo>();
        public List<string> FileIssues { get; set; } = new List<string>();

        // runtime properties
        public State CurrentState { get; protected set; }
        public bool CancellationRequested { get; set; }
        public int Progress { get; set; }
        public int MaxProgress { get; set; }
        protected int ProgressId { get; set; }
        public bool IsRunning => CurrentState == State.Scanning || CurrentState == State.Fixing;

        public int IssueCount => Type == ValidatorType.DB ? DBIssues.Count : FileIssues.Count;
        public virtual string ResultText => null;

        /// <summary>Reports whether this validator applies to the active database and project configuration.</summary>
        public virtual bool IsVisible() => true;
        /// <summary>Scans the active catalog or storage for this validator's issue type and records findings without modifying customer data.</summary>
        public abstract Task Validate();
        /// <summary>Repairs findings recorded by the most recent validation pass, then refreshes the validator state and results.</summary>
        public abstract Task Fix();
    }
}
