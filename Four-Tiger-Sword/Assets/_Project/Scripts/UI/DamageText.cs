using UnityEngine;
using TMPro;
using System;
using System.Collections;

[DefaultExecutionOrder(100)]
public class DamageText : MonoBehaviour
{
    public TextMeshProUGUI textMesh;
    public float speed = 0.05f;
    public float fadeSpeed = 1.5f;
    public float lifeTime = 0.7f;

    public float normalFontSize = 36f;
    public float criticalFontSize = 54f;

    private Color originalColor;
    private Action _returnToPool;
    private Canvas _overlayCanvas;
    private Camera _worldCamera;
    private Vector3 _worldPosition;
    private Vector3 _worldScale;
    private RectTransform _rectTransform;
    private EnemyAction _warningAction;
    private Transform _warningHead;
    private Collider _warningBody;
    private Renderer[] _warningRenderers;
    private Vector3 _warningLocalTop;
    private const float WarningHeadOffset = 0.35f;

    public void ConfigureOverlay(Canvas canvas, Camera worldCamera, Vector3 worldScale)
    {
        _overlayCanvas = canvas;
        _worldCamera = worldCamera;
        _worldScale = worldScale;
        _rectTransform = transform as RectTransform;
        var billboard = GetComponent<Billboard>();
        if (billboard != null) billboard.enabled = false;
        if (textMesh != null) textMesh.raycastTarget = false;
    }

    public void SetWorldPosition(Vector3 position)
    {
        _worldPosition = position;
        if (_overlayCanvas == null) transform.position = position;
        else UpdateScreenPosition();
    }

    private void LateUpdate()
    {
        if (_warningAction != null)
        {
            UpdateAttackWarning();
            return;
        }
        UpdateScreenPosition();
    }

    private void UpdateScreenPosition()
    {
        if (_overlayCanvas == null || _rectTransform == null || textMesh == null) return;
        if (_worldCamera == null || !_worldCamera.isActiveAndEnabled) _worldCamera = Camera.main;
        if (_worldCamera == null)
        {
            textMesh.enabled = false;
            return;
        }

        Vector3 screen = _worldCamera.WorldToScreenPoint(_worldPosition);
        bool visible = screen.z >= _worldCamera.nearClipPlane && screen.z <= _worldCamera.farClipPlane;
        textMesh.enabled = visible;
        if (!visible) return;

        var canvasRect = (RectTransform)_overlayCanvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
        Vector3 screenUp = _worldCamera.WorldToScreenPoint(_worldPosition + _worldCamera.transform.up);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenUp, null, out var localUp);
        float unitsToPixels = Vector2.Distance(local, localUp);
        _rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
        _rectTransform.localRotation = Quaternion.identity;
        _rectTransform.localScale = new Vector3(_worldScale.x * unitsToPixels, _worldScale.y * unitsToPixels, 1f);
    }

    public void Init(int damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Action returnToPool = null)
    {
        ResetForPool();
        _returnToPool = returnToPool;

        textMesh.text = isCritical ? $"{damage}!" : damage.ToString();
        textMesh.fontSize = isCritical ? criticalFontSize : normalFontSize;
        textMesh.color = GetDamageColor(damageType);
        originalColor = textMesh.color;

        StartCoroutine(AnimateText());
    }

    public void InitAttackWarning(EnemyAction action, Action returnToPool)
    {
        ResetForPool();
        _returnToPool = returnToPool;
        _warningAction = action;
        if (action?.Owner == null || textMesh == null)
        {
            Complete();
            return;
        }

        textMesh.text = "!";
        textMesh.fontSize = criticalFontSize;
        textMesh.color = new Color(1f, 1f, 0f, 1f);
        textMesh.raycastTarget = false;

        Enemy owner = action.Owner;
        var animator = owner.Animator;
        if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman)
            _warningHead = animator.GetBoneTransform(HumanBodyBones.Head);
        // Generic rigs (including the Hover enemy) can still have an authored head bone.
        if (_warningHead == null && animator != null)
        {
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (!bone.name.Equals("Head", StringComparison.OrdinalIgnoreCase)
                    && !bone.name.EndsWith(":Head", StringComparison.OrdinalIgnoreCase)
                    && !bone.name.EndsWith("_Head", StringComparison.OrdinalIgnoreCase)) continue;
                _warningHead = bone;
                break;
            }
        }
        if (_warningHead == null)
        {
            _warningBody = owner.GetComponent<Collider>();
            if (_warningBody != null && _warningBody.isTrigger) _warningBody = null;
            if (_warningBody == null) _warningRenderers = owner.GetComponentsInChildren<Renderer>(true);
        }
        // Retain a useful anchor if a jumping enemy temporarily disables its body collider.
        _warningLocalTop = Vector3.up * 2f;
        UpdateAttackWarning();
    }

    private void UpdateAttackWarning()
    {
        Enemy owner = _warningAction?.Owner;
        if (owner == null || !owner.isActiveAndEnabled || owner.IsDead
            || owner.CurrentAction != _warningAction || !_warningAction.IsAttackWarningActive
            || !_warningAction.IsActive || _warningAction.IsFinished || _warningAction.WasParried)
        {
            Complete();
            return;
        }

        Vector3 top = owner.transform.TransformPoint(_warningLocalTop);
        if (_warningHead != null) top = _warningHead.position;
        else if (_warningBody != null && _warningBody.enabled && _warningBody.gameObject.activeInHierarchy)
        {
            var bounds = _warningBody.bounds;
            top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        else if (_warningRenderers != null)
        {
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var renderer in _warningRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    || renderer.GetComponentInParent<Enemy>() != owner) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (hasBounds) top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        _warningLocalTop = owner.transform.InverseTransformPoint(top);
        SetWorldPosition(top + Vector3.up * WarningHeadOffset);
    }

    public void ResetForPool()
    {
        StopAllCoroutines();
        _returnToPool = null;
        _warningAction = null;
        _warningHead = null;
        _warningBody = null;
        _warningRenderers = null;
        _warningLocalTop = Vector3.zero;
    }

    private void Complete()
    {
        var returnToPool = _returnToPool;
        ResetForPool();
        returnToPool?.Invoke();
    }

    private Color GetDamageColor(ElementType damageType)
    {
        return damageType switch
        {
            ElementType.ELEMENT_FIRE  => new Color(1f, 0.35f, 0f),
            ElementType.ELEMENT_WATER => new Color(0.2f, 0.6f, 1f),
            ElementType.ELEMENT_WOOD  => new Color(0.2f, 0.85f, 0.2f),
            ElementType.ELEMENT_EARTH => new Color(0.85f, 0.65f, 0.15f),
            ElementType.ELEMENT_GOLD  => new Color(0.75f, 0.75f, 0.85f),
            _                => Color.white,
        };
    }

    private IEnumerator AnimateText()
    {
        float elapsed = 0f;

        while (elapsed < lifeTime)
        {
            elapsed += Time.deltaTime;

            if (_overlayCanvas != null) _worldPosition += Vector3.down * speed * Time.deltaTime;
            else transform.position += Vector3.down * speed * Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / lifeTime);
            textMesh.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);

            yield return null;
        }

        Complete();
    }
}
