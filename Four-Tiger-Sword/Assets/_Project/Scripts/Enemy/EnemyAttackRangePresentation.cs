using Shapes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Draws the current action's ground footprint without owning combat timing.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public sealed class EnemyAttackRangePresentation : MonoBehaviour
{
    private const float FloorLift = .045f;
    private static readonly Color AttackColor = new Color(1f, .15f, .06f);
    private Enemy _owner;
    private GameObject _root;
    private Disc _circleBackground, _circleFill, _circleOutline;
    private Rectangle _rectangleBackground, _rectangleFill, _rectangleOutline;

    public Disc WarningCircle => _circleOutline;
    public Rectangle WarningRectangle => _rectangleOutline;
    public float WarningFillProgress { get; private set; }
    public bool WarningVisible => isActiveAndEnabled && _root != null && _root.activeInHierarchy
        && (_circleOutline != null && _circleOutline.enabled
            || _rectangleOutline != null && _rectangleOutline.enabled);

    public static EnemyAttackRangePresentation Ensure(Enemy owner)
    {
        if (owner == null || owner is Boss) return null;
        var presentation = owner.GetComponent<EnemyAttackRangePresentation>()
            ?? owner.gameObject.AddComponent<EnemyAttackRangePresentation>();
        presentation._owner = owner;
        return presentation;
    }

    private void Awake() => _owner = GetComponent<Enemy>();
    private void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (_owner == null) _owner = GetComponent<Enemy>();
        var action = _owner != null ? _owner.CurrentAction : null;
        if (!isActiveAndEnabled || _owner == null || _owner is Boss || !_owner.isActiveAndEnabled
            || _owner.IsDead || _owner.IsSpawning || action == null
            || !action.TryGetAttackRangeWarning(out var warning) || !IsDrawable(warning))
        {
            Hide();
            return;
        }

        WarningFillProgress = Mathf.Clamp01(warning.Progress);
        if (warning.Kind == AttackRangeWarningKind.Circle) DrawCircle(warning);
        else DrawRectangle(warning);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsDrawable(AttackRangeWarning warning)
        => Finite(warning.Center.x) && Finite(warning.Center.y) && Finite(warning.Center.z)
            && Finite(warning.Progress)
            && (warning.Kind == AttackRangeWarningKind.Circle
                ? Finite(warning.Radius) && warning.Radius > 0f
                : Finite(warning.Size.x) && Finite(warning.Size.y) && warning.Size.x > 0f && warning.Size.y > 0f);

    private void DrawCircle(AttackRangeWarning warning)
    {
        if (_circleOutline == null)
        {
            _circleBackground = CreateShape<Disc>("AttackWarningCircleBackground", 0);
            _circleFill = CreateShape<Disc>("AttackWarningCircleFill", 1);
            _circleOutline = CreateShape<Disc>("AttackWarningCircleOutline", 2);
            _circleBackground.Type = _circleFill.Type = DiscType.Disc;
            _circleOutline.Type = DiscType.Ring;
            _circleOutline.Thickness = .055f;
        }
        SetVisible(_rectangleBackground, false);
        SetVisible(_rectangleFill, false);
        SetVisible(_rectangleOutline, false);
        Vector3 center = warning.Center + Vector3.up * FloorLift;
        Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        _circleBackground.transform.SetPositionAndRotation(center, rotation);
        _circleFill.transform.SetPositionAndRotation(center, rotation);
        _circleOutline.transform.SetPositionAndRotation(center, rotation);
        _circleBackground.Radius = _circleOutline.Radius = warning.Radius;
        _circleFill.Radius = warning.Radius * WarningFillProgress;
        _circleBackground.Color = WithAlpha(.055f);
        _circleFill.Color = WithAlpha(.25f);
        _circleOutline.Color = AttackColor;
        SetVisible(_circleBackground, true);
        SetVisible(_circleFill, WarningFillProgress > 0f);
        SetVisible(_circleOutline, true);
    }

    private void DrawRectangle(AttackRangeWarning warning)
    {
        if (_rectangleOutline == null)
        {
            _rectangleBackground = CreateShape<Rectangle>("AttackWarningRectangleBackground", 0);
            _rectangleFill = CreateShape<Rectangle>("AttackWarningRectangleFill", 1);
            _rectangleOutline = CreateShape<Rectangle>("AttackWarningRectangleOutline", 2);
            _rectangleOutline.Type = Rectangle.RectangleType.HardBorder;
            _rectangleOutline.Thickness = .055f;
        }
        SetVisible(_circleBackground, false);
        SetVisible(_circleFill, false);
        SetVisible(_circleOutline, false);
        Vector3 center = warning.Center + Vector3.up * FloorLift;
        Vector3 direction = warning.Rotation * Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(Vector3.up, direction);
        _rectangleBackground.transform.SetPositionAndRotation(center, rotation);
        _rectangleOutline.transform.SetPositionAndRotation(center, rotation);
        bool corridor = warning.Kind == AttackRangeWarningKind.Corridor;
        Vector3 fillCenter = corridor
            ? center - direction * (warning.Size.y * (1f - WarningFillProgress) * .5f) : center;
        _rectangleFill.transform.SetPositionAndRotation(fillCenter, rotation);
        _rectangleBackground.Width = _rectangleOutline.Width = warning.Size.x;
        _rectangleBackground.Height = _rectangleOutline.Height = warning.Size.y;
        _rectangleFill.Width = warning.Size.x * (corridor ? 1f : WarningFillProgress);
        _rectangleFill.Height = warning.Size.y * WarningFillProgress;
        _rectangleBackground.Color = WithAlpha(.055f);
        _rectangleFill.Color = WithAlpha(.25f);
        _rectangleOutline.Color = AttackColor;
        SetVisible(_rectangleBackground, true);
        SetVisible(_rectangleFill, WarningFillProgress > 0f);
        SetVisible(_rectangleOutline, true);
    }

    private T CreateShape<T>(string shapeName, int order) where T : ShapeRenderer
    {
        if (_root == null)
        {
            // A separate unit-scale root keeps hitbox metres independent of the model scale,
            // and keeps health-bar bounds, hit flashes and death dissolves off the warning.
            _root = new GameObject(name + " AttackRange")
                { hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.HideAndDontSave };
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                SceneManager.MoveGameObjectToScene(_root, gameObject.scene);
            if (Application.isPlaying) StageTransientObject.Track(_root);
        }
        var host = new GameObject(shapeName)
            { hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.HideAndDontSave };
        host.transform.SetParent(_root.transform, false);
        var shape = host.AddComponent<T>();
        shape.BlendMode = ShapesBlendMode.Transparent;
        shape.SortingOrder = order;
        shape.enabled = false;
        var renderer = host.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return shape;
    }

    private static Color WithAlpha(float alpha) => new Color(AttackColor.r, AttackColor.g, AttackColor.b, alpha);
    private static void SetVisible(ShapeRenderer shape, bool visible)
    {
        if (shape != null && shape.enabled != visible) shape.enabled = visible;
    }

    public void Hide()
    {
        SetVisible(_circleBackground, false);
        SetVisible(_circleFill, false);
        SetVisible(_circleOutline, false);
        SetVisible(_rectangleBackground, false);
        SetVisible(_rectangleFill, false);
        SetVisible(_rectangleOutline, false);
        WarningFillProgress = 0f;
    }

    private void OnDisable() => Hide();
    private void OnDestroy()
    {
        Hide();
        if (_root == null) return;
        if (Application.isPlaying) Destroy(_root);
        else DestroyImmediate(_root);
    }
}
