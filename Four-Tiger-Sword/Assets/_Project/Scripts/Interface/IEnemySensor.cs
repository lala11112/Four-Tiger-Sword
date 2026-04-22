using UnityEngine;

public interface IEnemySensor
{
    bool DetectTarget();
    Transform DetectedTarget { get; }
}
