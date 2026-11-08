using System;
using System.Collections.Generic;

/// <summary>Committed clear receipts survive temporarily unavailable reward composition.</summary>
public sealed class RunItemRewardInbox
{
    private readonly Queue<string> _pending = new();
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private bool _flushing;
    public int Count => _pending.Count;

    public bool Enqueue(string receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt) || !_known.Add(receipt)) return false;
        _pending.Enqueue(receipt);
        return true;
    }

    public void Flush(Func<string, bool> accept)
    {
        if (_flushing || accept == null) return;
        _flushing = true;
        try
        {
            // A failed acceptance retains the head and its order; reentrant callbacks cannot drain it twice.
            while (_pending.Count > 0 && accept(_pending.Peek())) _pending.Dequeue();
        }
        finally { _flushing = false; }
    }
}
