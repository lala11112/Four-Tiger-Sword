using System.Collections;
using UnityEngine;

/// <summary>One captured, blurred game-and-HUD frame behind an independently sharp modal.</summary>
[DisallowMultipleComponent]
public sealed class BoundaryZeroBackdrop : MonoBehaviour
{
    public static event System.Action<bool> CaptureVisibilityChanged;
    private static int _activeCaptures;
    public static bool IsCapturing => _activeCaptures > 0;
    [SerializeField] private CanvasGroup modalContent;
    [SerializeField] private UnityEngine.UI.RawImage background;
    private RenderTexture _snapshot;
    private Material _blur;
    private Coroutine _capture;
    private bool _shown;
    private bool _hiddenForCapture;
    private float _alpha;
    private bool _interactable, _blocksRaycasts;

    public bool IsShown => _shown;
    public bool HasBindings => modalContent != null && background != null;

    public void Configure(CanvasGroup content, UnityEngine.UI.RawImage image)
    {
        if (content == null || image == null)
            throw new System.InvalidOperationException("BoundaryZERO backdrop requires a sharp-content CanvasGroup and background RawImage.");
        Hide();
        modalContent = content;
        background = image;
        if (background != null)
        {
            background.raycastTarget = true;
            background.enabled = false;
        }
    }

    public void Show()
    {
        if (_shown || modalContent == null || background == null || !isActiveAndEnabled) return;
        _shown = true;
        // Edit Mode prefab previews and wiring regressions have no rendered game frame to capture.
        if (!Application.isPlaying)
        {
            background.enabled = false;
            return;
        }
        _alpha = modalContent.alpha;
        _interactable = modalContent.interactable;
        _blocksRaycasts = modalContent.blocksRaycasts;
        modalContent.alpha = 0;
        modalContent.interactable = false;
        modalContent.blocksRaycasts = false;
        _hiddenForCapture = true;
        if (++_activeCaptures == 1) NotifyCapture(true);
        background.texture = null;
        background.color = Color.clear;
        background.enabled = true;
        _capture = StartCoroutine(Capture());
    }

    public void Hide()
    {
        _shown = false;
        if (_capture != null) { StopCoroutine(_capture); _capture = null; }
        RestoreContent();
        if (background != null)
        {
            background.texture = null;
            background.enabled = false;
        }
        ReleaseSnapshot();
    }

    private IEnumerator Capture()
    {
        // Screen capture includes Overlay canvases. Hide only this modal during the capture frame.
        yield return new WaitForEndOfFrame();
        if (!_shown || !isActiveAndEnabled) yield break;
        RenderTexture screen = null;
        RenderTexture ping = null;
        var previousTarget = RenderTexture.active;
        try
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            screen = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
            float scale = Mathf.Min(1f, 1280f / Mathf.Max(width, height));
            int sampleWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int sampleHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            _snapshot = new RenderTexture(sampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "BoundaryZERO Modal Snapshot", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            _snapshot.Create();
            Graphics.Blit(screen, _snapshot);
            var shader = Resources.Load<Shader>("BoundaryZeroSnapshotBlur");
            if (shader != null && shader.isSupported)
            {
                if (_blur == null) _blur = new Material(shader) { hideFlags = HideFlags.DontSave };
                ping = RenderTexture.GetTemporary(sampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32);
                ping.filterMode = FilterMode.Bilinear;
                for (int i = 0; i < 3; i++)
                {
                    _blur.SetVector("_Direction", new Vector4(2.5f + i, 0, 0, 0));
                    Graphics.Blit(_snapshot, ping, _blur);
                    _blur.SetVector("_Direction", new Vector4(0, 2.5f + i, 0, 0));
                    Graphics.Blit(ping, _snapshot, _blur);
                }
            }
            background.texture = _snapshot;
            // ScreenCapture's render texture follows the graphics API's vertical UV origin.
            // RawImage must undo that orientation when compositing it back over the game.
            background.uvRect = SystemInfo.graphicsUVStartsAtTop
                ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            background.color = Color.white;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (screen != null) RenderTexture.ReleaseTemporary(screen);
            if (ping != null) RenderTexture.ReleaseTemporary(ping);
            RestoreContent();
            _capture = null;
        }
    }

    private void RestoreContent()
    {
        if (!_hiddenForCapture) return;
        if (modalContent != null)
        {
            modalContent.alpha = _alpha;
            modalContent.interactable = _interactable;
            modalContent.blocksRaycasts = _blocksRaycasts;
        }
        _hiddenForCapture = false;
        _activeCaptures = Mathf.Max(0, _activeCaptures - 1);
        if (_activeCaptures == 0) NotifyCapture(false);
    }

    private static void NotifyCapture(bool capturing)
    {
        if (CaptureVisibilityChanged == null) return;
        foreach (System.Action<bool> listener in CaptureVisibilityChanged.GetInvocationList())
        {
            try { listener(capturing); }
            catch (System.Exception error) { Debug.LogException(error); }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCaptureState() { _activeCaptures = 0; CaptureVisibilityChanged = null; }

    private void ReleaseSnapshot()
    {
        if (_snapshot == null) return;
        _snapshot.Release();
        Destroy(_snapshot);
        _snapshot = null;
    }

    private void OnDisable() => Hide();
    private void OnDestroy()
    {
        Hide();
        if (_blur != null) Destroy(_blur);
    }
}
