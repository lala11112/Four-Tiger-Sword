using System.Collections.Generic;
using UnityEngine;

/// <summary>Immutable authoring asset; custom patterns return their own per-boss runtime.</summary>
public abstract class BossPatternSO : ScriptableObject
{
    public abstract IBossPattern CreateRuntime(Boss boss);
    public virtual IEnumerable<string> Validate() { yield break; }
}
