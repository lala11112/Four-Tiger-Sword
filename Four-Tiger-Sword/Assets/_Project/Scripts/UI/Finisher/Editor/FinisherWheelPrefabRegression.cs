using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks prefab presentation through disposable preview-scene instances without editing source assets.</summary>
public static class FinisherWheelPrefabRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Finisher/Run Prefab Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finisher wheel prefab regression requires Edit Mode.");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Finisher wheel prefab: " + message);
            checks++;
        }

        var resources = Resources.Load<FinisherWheelResources>(FinisherWheelResources.ResourceName);
        Check(resources != null && resources.Prefab != null, "Prepare FinisherWheelResources and its prefab first.");
        var prefab = resources.Prefab;
        Check(EditorUtility.IsPersistent(prefab) && prefab.ValidateBindings(out _), "Configured prefab bindings are invalid.");
        string resourcesBefore = EditorJsonUtility.ToJson(resources);
        string prefabBefore = Snapshot(prefab);
        string resourcesFileBefore = ReadAssetFile(resources);
        string prefabFileBefore = ReadAssetFile(prefab);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject owner = null;
        try
        {
            owner = new GameObject("__FinisherWheelPrefabRegression");
            SceneManager.MoveGameObjectToScene(owner, preview);
            var view = FinisherWheelView.Create(owner.transform);
            Check(view != prefab && !EditorUtility.IsPersistent(view) && view.transform.parent == owner.transform,
                "Create must return a child instance rather than the source prefab.");
            var canvas = Field<Canvas>(view, "canvas");
            var content = Field<RectTransform>(view, "content");
            Check(view.gameObject.activeSelf && !view.IsVisible && !canvas.enabled && !content.gameObject.activeSelf,
                "A created instance must start hidden while retaining an active root.");
            int hierarchyCount = AuthoredHierarchyCount(view);
            bool allSectorsMatch = true;
            for (int currentIndex = 0; currentIndex < 5; currentIndex++)
            {
                ElementType current = FinisherWheelSelection.ElementAt(currentIndex);
                view.Show(current);
                Check(view.IsVisible && canvas.enabled && content.gameObject.activeSelf
                    && view.SelectedElement == current && view.ScaleFactor > 0f,
                    "Show/center selection failed for " + current + ".");
                for (int selectedIndex = 0; selectedIndex < 5; selectedIndex++)
                {
                    Vector2 cursor = Point(selectedIndex * FinisherWheelSelection.SectorAngle, 180f);
                    view.UpdateSelection(cursor);
                    allSectorsMatch &= view.SelectedElement == FinisherWheelSelection.Resolve(cursor);
                }
            }
            Check(allSectorsMatch, "Prefab sector selection disagrees with the existing selector.");
            view.Hide();
            bool hidden = !view.IsVisible && !canvas.enabled && !content.gameObject.activeSelf;
            view.Show(ElementType.ELEMENT_WOOD);
            Check(hidden && view.IsVisible && view.SelectedElement == ElementType.ELEMENT_WOOD,
                "Hide followed by Show failed to reopen the same instance.");

            var sectors = Field<FinisherWheelView.SectorBinding[]>(view, "sectors");
            var water = Array.Find(sectors, sector => sector.Element == ElementType.ELEMENT_WATER);
            Color waterLabelColor = water.ElementLabel.color;
            Color waterArtworkColor = water.Artwork != null ? water.Artwork.color : default;
            view.Show(ElementType.ELEMENT_FIRE, element => element == ElementType.ELEMENT_FIRE);
            Check(view.SelectedElement == ElementType.ELEMENT_FIRE, "A restricted wheel must start with Fire selected.");
            foreach (var sector in sectors)
            {
                int index = FinisherWheelSelection.IndexOf(sector.Element);
                view.UpdateSelection(Point(index * FinisherWheelSelection.SectorAngle, 180f));
                if (sector.Element == ElementType.ELEMENT_FIRE)
                {
                    Check(view.SelectedElement == sector.Element, "The allowed Fire sector remains selectable.");
                    continue;
                }
                Check(view.SelectedElement == ElementType.ELEMENT_NONE && sector.RoleLabel.text == "잠금"
                    && (sector.SelectedVisual == null || !sector.SelectedVisual.activeSelf),
                    "A locked sector must show its lock state without becoming selected: " + sector.Element + ".");
            }
            Check(water.ElementLabel.color != waterLabelColor
                && (water.Artwork == null || water.Artwork.color.grayscale < waterArtworkColor.grayscale),
                "The locked Water label and bound icon must appear dimmed.");
            view.Hide();
            view.Show(ElementType.ELEMENT_WATER);
            Check(view.SelectedElement == ElementType.ELEMENT_WATER && water.RoleLabel.text != "잠금"
                && water.ElementLabel.color == waterLabelColor
                && (water.Artwork == null || water.Artwork.color == waterArtworkColor),
                "Reopening without restrictions must restore authored colors and selection.");
            Check(AuthoredHierarchyCount(view) == hierarchyCount,
                "Repeated Show/UpdateSelection/Hide constructed duplicate hierarchy objects.");

            // A template clone represents authored prefab changes before a fresh instance initializes its style cache.
            var template = Object.Instantiate(prefab, owner.transform, false);
            template.gameObject.SetActive(false);
            var templateSectors = Field<FinisherWheelView.SectorBinding[]>(template, "sectors");
            var authoredSector = templateSectors[0];
            var title = FindText(template, "Title");
            Check(authoredSector.ElementLabel != null && authoredSector.RoleLabel != null && title != null,
                "The prepared default prefab must contain editable element/role labels and a static Title.");
            var authoredColor = new Color(.21f, .37f, .65f, 1f);
            var authoredRoleColor = new Color(.48f, .29f, .62f, 1f);
            const FontStyles authoredStyle = FontStyles.Italic | FontStyles.Underline;
            const float authoredSize = 43f;
            Vector2 authoredPosition = new Vector2(31f, 17f);
            authoredSector.ElementLabel.color = authoredColor;
            authoredSector.ElementLabel.fontStyle = authoredStyle;
            authoredSector.ElementLabel.fontSize = authoredSize;
            authoredSector.ElementLabel.rectTransform.anchoredPosition = authoredPosition;
            authoredSector.RoleLabel.color = authoredRoleColor;
            title.text = "Authored static title";
            title.color = authoredColor;
            title.fontStyle = authoredStyle;
            title.fontSize = 39f;
            title.rectTransform.anchoredPosition = new Vector2(23f, 351f);

            var styled = Object.Instantiate(template, owner.transform, false);
            styled.gameObject.SetActive(true);
            var styledSector = Field<FinisherWheelView.SectorBinding[]>(styled, "sectors")[0];
            var styledTitle = FindText(styled, "Title");
            styled.Show(ElementType.ELEMENT_WATER);
            styled.UpdateSelection(Point(FinisherWheelSelection.IndexOf(styledSector.Element) * 72f, 180f));
            Check(styledSector.ElementLabel.color == authoredColor
                && (styledSector.ElementLabel.fontStyle & authoredStyle) == authoredStyle,
                "Selecting a sector overwrote its authored label color or font-style flags.");
            styled.UpdateSelection(Vector2.zero);
            Check(styledSector.ElementLabel.fontStyle == authoredStyle && styledSector.RoleLabel.color == authoredRoleColor,
                "Deselection did not restore the authored font style and role color.");
            Check(AuthoredPresentationMatches(styledSector, styledTitle, authoredColor, authoredStyle, authoredSize, authoredPosition),
                "Selection changed authored text size, static title, color, style or position.");
            styled.Hide();
            styled.Show(ElementType.ELEMENT_EARTH);
            styled.UpdateSelection(Vector2.zero);
            Check(AuthoredPresentationMatches(styledSector, styledTitle, authoredColor, authoredStyle, authoredSize, authoredPosition)
                && styledSector.RoleLabel.color == authoredRoleColor,
                "Reopening replaced authored presentation settings.");

            var iconOnly = Object.Instantiate(template, owner.transform, false);
            iconOnly.gameObject.SetActive(false);
            var iconSectors = Field<FinisherWheelView.SectorBinding[]>(iconOnly, "sectors");
            foreach (var sector in iconSectors)
            {
                sector.DisplayName = sector.Element.ToString();
                sector.ElementLabel = null;
                sector.RoleLabel = null;
            }
            SetField(iconOnly, "wheel", null);
            SetField(iconOnly, "centerName", null);
            SetField(iconOnly, "selectionLabel", null);
            foreach (var text in iconOnly.GetComponentsInChildren<TMP_Text>(true)) Object.DestroyImmediate(text.gameObject);
            foreach (var graphic in iconOnly.GetComponentsInChildren<FinisherWheelGraphic>(true)) Object.DestroyImmediate(graphic);
            Check(iconOnly.ValidateBindings(out _), "An icon-only presentation incorrectly requires optional ring or labels.");
            iconOnly.gameObject.SetActive(true);
            bool iconSelectionWorks = true;
            for (int i = 0; i < 5; i++)
            {
                ElementType current = FinisherWheelSelection.ElementAt(i);
                iconOnly.Show(current);
                iconSelectionWorks &= iconOnly.IsVisible && iconOnly.SelectedElement == current;
                Vector2 cursor = Point(((i + 1) % 5) * 72f, 180f);
                iconOnly.UpdateSelection(cursor);
                iconSelectionWorks &= iconOnly.SelectedElement == FinisherWheelSelection.Resolve(cursor);
                iconOnly.Hide();
                iconSelectionWorks &= !iconOnly.IsVisible;
            }
            Check(iconSelectionWorks, "Removing optional graphics changed Show/selection/Hide behavior.");
            Check(EditorJsonUtility.ToJson(resources) == resourcesBefore && Snapshot(prefab) == prefabBefore
                && ReadAssetFile(resources) == resourcesFileBefore && ReadAssetFile(prefab) == prefabFileBefore,
                "The regression or view runtime modified a source resource/prefab asset.");
            return $"PASS: finisher wheel prefab ({checks} checks: child creation, hidden/reopen lifecycle, five-form selection, stable hierarchy, authored styles/layout, icon-only bindings, and unchanged source assets).";
        }
        finally
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static bool AuthoredPresentationMatches(FinisherWheelView.SectorBinding sector, TMP_Text title,
        Color color, FontStyles style, float size, Vector2 position)
        => sector.ElementLabel.color == color && sector.ElementLabel.fontStyle == style
            && Mathf.Approximately(sector.ElementLabel.fontSize, size)
            && sector.ElementLabel.rectTransform.anchoredPosition == position
            && title != null && title.text == "Authored static title" && title.color == color && title.fontStyle == style
            && Mathf.Approximately(title.fontSize, 39f) && title.rectTransform.anchoredPosition == new Vector2(23f, 351f);

    private static TMP_Text FindText(FinisherWheelView view, string objectName)
    {
        var content = Field<RectTransform>(view, "content");
        foreach (var text in content.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == objectName) return text;
        return null;
    }

    private static int AuthoredHierarchyCount(FinisherWheelView view)
    {
        int count = 0;
        foreach (var transform in view.GetComponentsInChildren<Transform>(true))
        {
            // TMP creates fallback-material submesh objects lazily during its first text render.
            if (transform.GetComponent<TMP_SubMeshUI>() == null) count++;
        }
        return count;
    }

    private static Vector2 Point(float degrees, float radius)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * radius;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);

    private static string Snapshot(FinisherWheelView prefab)
    {
        var snapshot = new StringBuilder();
        foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            if (component != null) snapshot.AppendLine(EditorJsonUtility.ToJson(component));
        return snapshot.ToString();
    }

    private static string ReadAssetFile(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        string root = Directory.GetParent(Application.dataPath).FullName;
        return File.ReadAllText(Path.Combine(root, path));
    }
}
