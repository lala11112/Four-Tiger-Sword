using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class PlayerDashAfterimage : IDisposable
{
    private const int Capacity = 6;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private readonly struct Source
    {
        public readonly Renderer Renderer;
        public readonly MeshFilter Filter;

        public Source(Renderer renderer, MeshFilter filter = null)
        {
            Renderer = renderer;
            Filter = filter;
        }
    }

    private struct Piece
    {
        public Mesh BakedMesh;
        public Mesh Mesh;
        public Matrix4x4 WorldMatrix;
        public int Layer;
    }

    private sealed class Snapshot
    {
        public readonly Piece[] Pieces;
        public readonly MaterialPropertyBlock Properties = new();
        public int PieceCount;
        public Color Color;
        public float Lifetime;
        public float Remaining;
        public ulong Sequence;

        public Snapshot(int sourceCount) => Pieces = new Piece[sourceCount];
    }

    private readonly Source[] _sources;
    private readonly Snapshot[] _snapshots = new Snapshot[Capacity];
    private readonly Material _material;
    private ulong _sequence;
    private bool _disposed;

    internal int ActiveCount
    {
        get
        {
            int count = 0;
            foreach (var snapshot in _snapshots)
                if (snapshot.Remaining > 0f) count++;
            return count;
        }
    }

    public PlayerDashAfterimage(PlayerController owner, Material material)
    {
        _material = material;
        var sources = new List<Source>();
        if (owner.Animator != null)
            foreach (var renderer in owner.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                sources.Add(new Source(renderer));

        if (owner.WeaponManager != null)
        {
            AddWeapon(sources, owner.WeaponManager.FireWeapon);
            AddWeapon(sources, owner.WeaponManager.WaterWeapon);
            AddWeapon(sources, owner.WeaponManager.WaterOffhandWeapon);
        }

        _sources = sources.ToArray();
        for (int i = 0; i < _snapshots.Length; i++)
            _snapshots[i] = new Snapshot(_sources.Length);
    }

    public void Capture(Color color, float lifetime)
    {
        if (_disposed || lifetime <= 0f) return;

        Snapshot snapshot = null;
        int pieceCount = 0;
        foreach (var source in _sources)
        {
            var renderer = source.Renderer;
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy
                || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;

            var skinned = renderer as SkinnedMeshRenderer;
            var mesh = source.Filter != null ? source.Filter.sharedMesh : skinned?.sharedMesh;
            if (mesh == null) continue;

            snapshot ??= FindSnapshot();
            ref Piece piece = ref snapshot.Pieces[pieceCount++];
            if (skinned != null)
            {
                if (piece.BakedMesh == null)
                {
                    piece.BakedMesh = new Mesh { name = "Dash Afterimage Pose" };
                    piece.BakedMesh.MarkDynamic();
                }

                // Compensate the renderer's scale in the baked vertices; the saved world matrix applies it once.
                skinned.BakeMesh(piece.BakedMesh, true);
                mesh = piece.BakedMesh;
            }

            piece.Mesh = mesh;
            piece.WorldMatrix = renderer.transform.localToWorldMatrix;
            piece.Layer = renderer.gameObject.layer;
        }

        if (snapshot == null) return;
        snapshot.PieceCount = pieceCount;
        snapshot.Color = color;
        snapshot.Lifetime = lifetime;
        snapshot.Remaining = lifetime;
        snapshot.Sequence = ++_sequence;
    }

    public void Advance(float deltaTime)
    {
        foreach (var snapshot in _snapshots)
            if (snapshot.Remaining > 0f)
                snapshot.Remaining = Mathf.Max(0f, snapshot.Remaining - deltaTime);
    }

    public void Draw()
    {
        if (_material == null) return;

        foreach (var snapshot in _snapshots)
        {
            if (snapshot.Remaining <= 0f) continue;

            Color color = snapshot.Color;
            color.a *= snapshot.Remaining / snapshot.Lifetime;
            snapshot.Properties.SetColor(BaseColor, color);

            for (int i = 0; i < snapshot.PieceCount; i++)
            {
                ref Piece piece = ref snapshot.Pieces[i];
                if (piece.Mesh == null) continue;
                for (int submesh = 0; submesh < piece.Mesh.subMeshCount; submesh++)
                    Graphics.DrawMesh(piece.Mesh, piece.WorldMatrix, _material, piece.Layer, null, submesh,
                        snapshot.Properties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }
    }

    public void Clear()
    {
        foreach (var snapshot in _snapshots)
            snapshot.Remaining = 0f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();

        foreach (var snapshot in _snapshots)
            for (int i = 0; i < snapshot.Pieces.Length; i++)
            {
                ref Piece piece = ref snapshot.Pieces[i];
                if (piece.BakedMesh == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(piece.BakedMesh);
                else UnityEngine.Object.DestroyImmediate(piece.BakedMesh);
                piece = default;
            }
    }

    private Snapshot FindSnapshot()
    {
        Snapshot oldest = _snapshots[0];
        foreach (var snapshot in _snapshots)
        {
            if (snapshot.Remaining <= 0f) return snapshot;
            if (snapshot.Sequence < oldest.Sequence) oldest = snapshot;
        }

        return oldest;
    }

    private static void AddWeapon(List<Source> sources, GameObject weapon)
    {
        if (weapon != null && weapon.TryGetComponent<MeshRenderer>(out var renderer)
            && weapon.TryGetComponent<MeshFilter>(out var filter))
            sources.Add(new Source(renderer, filter));
    }
}
