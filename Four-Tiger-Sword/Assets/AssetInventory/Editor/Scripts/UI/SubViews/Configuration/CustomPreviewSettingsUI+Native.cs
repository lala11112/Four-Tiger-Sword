using System;
using System.Collections.Generic;
using ImpossibleRobert.Common;
using UnityEditor;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.PackageManager;
#endif
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public partial class CustomPreviewSettingsUI
    {
        private const string PreviewSettingsTitleClass = "ai-custom-preview-title";
        private const string PreviewSettingsLayoutClass = "ai-custom-preview-layout";
        private const string PreviewSettingsPanelClass = "ai-custom-preview-panel";
        private const string PreviewSettingsPanelLeftClass = "ai-custom-preview-panel-left";
        private const string PreviewSettingsPanelRightClass = "ai-custom-preview-panel-right";
        private const string PreviewSettingsScrollClass = "ai-custom-preview-scroll";
        private const string PreviewSettingsGroupClass = "ai-custom-preview-group";
        private const string PreviewSettingsFooterClass = "ai-custom-preview-footer";
        private const string PreviewInlineRowClass = "ai-custom-preview-inline-row";
        private const string PreviewControlWithUnitClass = "ai-custom-preview-control-with-unit";
        private const string PreviewUnitLabelClass = "ai-custom-preview-unit-label";
        private const string PreviewHintClass = "ai-custom-preview-hint";
        private const string PreviewGalleryHeaderClass = "ai-custom-preview-gallery-header";
        private const string PreviewGalleryTitleClass = "ai-custom-preview-gallery-title";
        private const string PreviewGallerySharedScrollClass = "ai-custom-preview-gallery-shared-scroll";
        private const string PreviewGalleryRowsClass = "ai-custom-preview-gallery-rows";
        private const string PreviewGalleryRowTitleClass = "ai-custom-preview-gallery-row-title";
        private const string PreviewGalleryRowClass = "ai-custom-preview-gallery-row";
        private const string PreviewCardClass = "ai-custom-preview-card";
        private const string PreviewCardTitleClass = "ai-custom-preview-card-title";
        private const string PreviewFrameClass = "ai-custom-preview-frame";
        private const string PreviewFrameImageClass = "ai-custom-preview-frame-image";
        private const string PreviewFrameFallbackClass = "ai-custom-preview-frame-fallback";

#if UNITY_6000_7_OR_NEWER
        // Form builders are immutable configuration shared by every preview settings view.
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        private static readonly CommonFormBuilder NativePreviewFieldFormBuilder = AssetInventoryUITK.CreateFormBuilder(
            inlineClass: PreviewControlWithUnitClass,
            suffixClass: PreviewUnitLabelClass);
#if UNITY_6000_7_OR_NEWER
        // Form builders are immutable configuration shared by every preview settings view.
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        private static readonly CommonFormBuilder NativePreviewToggleFormBuilder = AssetInventoryUITK.CreateFormBuilder(
            labelTogglesControl: true);

        private readonly List<NativePreviewCard> _nativePreviewCards = new List<NativePreviewCard>();
        private bool _nativePreviewTypesExpanded = true;
        private bool _nativeAppearanceExpanded;
        private bool _nativeOutputExpanded;

        private void BuildNativeContent(VisualElement root)
        {
            _nativePreviewCards.Clear();

            if (_preview3DObject == null)
            {
                InitializePreviewObjects();
                if (_preview3DObject == null)
                {
                    root.Add(AssetInventoryUITK.CreateHelpBox("Initializing preview settings...", MessageType.Info));
                    return;
                }
            }

            Label title = AssetInventoryUITK.CreateCopyLabel("Custom Preview Pipeline Settings");
            title.AddToClassList(PreviewSettingsTitleClass);
            root.Add(title);

            VisualElement layout = CommonUITK.CreateContainer(PreviewSettingsLayoutClass);
            root.Add(layout);

            VisualElement settingsPanel = CommonUITK.CreateContainer(PreviewSettingsPanelClass, PreviewSettingsPanelLeftClass);
            layout.Add(settingsPanel);
            BuildNativeSettingsPanel(settingsPanel);

            VisualElement previewPanel = CommonUITK.CreateContainer(PreviewSettingsPanelClass, PreviewSettingsPanelRightClass);
            layout.Add(previewPanel);
            BuildNativePreviewPanel(previewPanel);

            UpdateNativePreviewImages();
            UpdatePreviewGenerationIfNeeded();
        }

        private void BuildNativeSettingsPanel(VisualElement parent)
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.AddToClassList(PreviewSettingsScrollClass);
            parent.Add(scroll);

            scroll.Add(AssetInventoryUITK.CreateHelpBox(
                "The defaults are recommended for most projects. Expand a group only when you need to adjust its preview behavior.",
                MessageType.Info));

            Foldout previewTypes = AssetInventoryUITK.CreateFoldout(
                "Preview Types",
                _nativePreviewTypesExpanded,
                value => _nativePreviewTypesExpanded = value,
                "Choose which asset types receive custom previews and how those previews behave.",
                PreviewSettingsGroupClass);
            previewTypes.Add(BuildNativeModelSection());
            previewTypes.Add(BuildNativeFbxSection());
            previewTypes.Add(BuildNativeAnimSection());
            previewTypes.Add(BuildNativeMaterialSection());
            previewTypes.Add(BuildNativeUiSection());
            previewTypes.Add(BuildNativeFontSection());
            previewTypes.Add(BuildNativeParticleSection());
            previewTypes.Add(BuildNativeVfxSection());
            previewTypes.Add(BuildNativeVideoSection());
            previewTypes.Add(BuildNativeSceneSection());
            scroll.Add(previewTypes);

            Foldout appearance = AssetInventoryUITK.CreateFoldout(
                "Appearance & Lighting",
                _nativeAppearanceExpanded,
                value => _nativeAppearanceExpanded = value,
                "Adjust the shared background, lights, and ambient environment used for rendered previews.",
                PreviewSettingsGroupClass);
            appearance.Add(BuildNativeBackgroundSection());
            appearance.Add(BuildNativeLightingSection());
            appearance.Add(BuildNativeEnvironmentSection());
            scroll.Add(appearance);

            Foldout output = AssetInventoryUITK.CreateFoldout(
                "Output Quality & Animation",
                _nativeOutputExpanded,
                value => _nativeOutputExpanded = value,
                "Adjust preview output size, rendering quality, and animated spritesheet behavior.",
                PreviewSettingsGroupClass);
            output.Add(BuildNativeUpscaleSection());
            output.Add(BuildNativeQualitySection());
            output.Add(BuildNativeAnimationSection());
            scroll.Add(output);

            VisualElement footer = CommonUITK.CreateContainer(PreviewSettingsFooterClass);
            Button reset = AssetInventoryUITK.CreateSecondaryButton("Reset to Defaults", ResetToDefaults);
            reset.tooltip = "Restore the recommended preview settings. This does not add or select a preset.";
            footer.Add(reset);
            parent.Add(footer);
        }

        private VisualElement BuildNativeUpscaleSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Upscaling");
            section.Add(CreateNativeToggleRow(
                "Upscale Preview Images",
                AI.Config.upscalePreviews,
                value =>
                {
                    AI.Config.upscalePreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.None, true);
                },
                "Resize preview images to make them fill a bigger area of the tiles."));

            if (AI.Config.upscalePreviews)
            {
                if (ShowAdvanced())
                {
                    section.Add(CreateNativeToggleRow(
                        "Lossless" + (Application.platform == RuntimePlatform.WindowsEditor ? " (Windows)" : ""),
                        AI.Config.upscaleLossless,
                        value =>
                        {
                            AI.Config.upscaleLossless = value;
                            ApplyNativePreviewSetting(PreviewTypeFlags.None, true);
                        },
                        "Only create upscaled versions if base resolution is bigger."));
                }

                section.Add(CreateNativeIntegerRow(
                    AI.Config.upscaleLossless ? "Target Size" : "Minimum Size",
                    AI.Config.upscaleSize,
                    value =>
                    {
                        AI.Config.upscaleSize = Mathf.Max(1, value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.None, false);
                    },
                    "pixels",
                    "Minimum size the preview image should have. Bigger images are not changed."));
            }

            return section;
        }

        private VisualElement BuildNativeModelSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("3D Models");
            section.Add(CreateNativeToggleRow(
                "Enable Custom Previews",
                AI.Config.generateCustomModelPreviews,
                value =>
                {
                    AI.Config.generateCustomModelPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Model3D | PreviewTypeFlags.Material, true);
                },
                "Use custom preview pipeline for 3D models and prefabs."));

            if (AI.Config.generateCustomModelPreviews)
            {
                section.Add(CreateNativeToggleRow(
                    "Animated (360°)",
                    AI.Config.generateAnimatedModelPreviews,
                    value =>
                    {
                        AI.Config.generateAnimatedModelPreviews = value;
                        ApplyNativePreviewSetting(PreviewTypeFlags.Model3D, true);
                    },
                    "Creates an animated preview rotating around the object."));

                if (AI.Config.generateAnimatedModelPreviews)
                {
                    section.Add(CreateNativeToggleRow(
                        "Rotate Lights",
                        AI.Config.cpRotateLightWith360,
                        value =>
                        {
                            AI.Config.cpRotateLightWith360 = value;
                            ApplyNativePreviewSetting(PreviewTypeFlags.Model3D, false);
                        },
                        "Keep lighting consistent by rotating the light source with the camera."));
                }

                section.Add(CreateNativeSliderRow("Field of View", AI.Config.cpCameraFOV, 0f, 120f, value =>
                {
                    AI.Config.cpCameraFOV = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Model3D | PreviewTypeFlags.Material, false);
                }, "Camera FOV in degrees."));
                section.Add(CreateNativeSliderRow("Vertical Angle", AI.Config.cpCameraAngleX, 0f, 90f, value =>
                {
                    AI.Config.cpCameraAngleX = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Model3D | PreviewTypeFlags.Material, false);
                }, "Camera pitch angle for 3D models."));
                section.Add(CreateNativeSliderRow("Horizontal Angle", AI.Config.cpCameraAngleY, 0f, 360f, value =>
                {
                    AI.Config.cpCameraAngleY = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Model3D | PreviewTypeFlags.Material, false);
                }, "Camera rotation around 3D models."));
                section.Add(CreateNativeSliderRow("Framing Padding", AI.Config.cpFramingPadding, 0f, 20f, value =>
                {
                    AI.Config.cpFramingPadding = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Model3D | PreviewTypeFlags.Material, false);
                }, "Padding around 3D models in percent of preview size."));
            }

            return section;
        }

        private VisualElement BuildNativeFbxSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("FBX Models & Animations");
            section.Add(CreateNativeToggleRow(
                "Enable FBX Previews",
                AI.Config.generateFBXPreviews,
                value =>
                {
                    AI.Config.generateFBXPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.FBX, true);
                },
                "Generate previews for FBX files including animations and models."));

            if (AI.Config.generateFBXPreviews)
            {
                section.Add(CreateNativeToggleRow("Animated (Playback)", AI.Config.generateAnimatedFBXPreviews, value =>
                {
                    AI.Config.generateAnimatedFBXPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.FBX, true);
                }, "Create animated preview when FBX contains animation clips."));
                section.Add(CreateNativeToggleRow("360° Rotation", AI.Config.generate360FBXPreviews, value =>
                {
                    AI.Config.generate360FBXPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.FBX, false);
                }, "Rotate camera around FBX for animated previews."));
                section.Add(CreateNativeEnumRow("Without Avatar", AI.Config.fbxAnimationPreviewMode, value =>
                {
                    AI.Config.fbxAnimationPreviewMode = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.FBX, false);
                }, "How to visualize animation-only FBX files without geometry or avatar."));
            }

            return section;
        }

        private VisualElement BuildNativeAnimSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Animation Files (.anim)");
            section.Add(CreateNativeToggleRow("Enable Anim Previews", AI.Config.generateAnimPreviews, value =>
            {
                AI.Config.generateAnimPreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.Anim, true);
            }, "Generate previews for standalone .anim files."));

            if (AI.Config.generateAnimPreviews)
            {
                section.Add(CreateNativeToggleRow("Animated (Playback)", AI.Config.generateAnimatedAnimPreviews, value =>
                {
                    AI.Config.generateAnimatedAnimPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Anim, true);
                }, "Create animated preview spritesheets for .anim files."));
            }

            return section;
        }

        private VisualElement BuildNativeMaterialSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Materials");
            section.Add(CreateNativeToggleRow("Enable Custom Previews", AI.Config.generateMaterialPreviews, value =>
            {
                AI.Config.generateMaterialPreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.Material, true);
            }, "Generate custom previews for material files using a 3D mesh."));

            if (AI.Config.generateMaterialPreviews)
            {
                CustomMaterialPreviewGenerator.PreviewMeshType meshType = (CustomMaterialPreviewGenerator.PreviewMeshType)AI.Config.materialPreviewMesh;
                section.Add(CreateNativeEnumRow("Preview Mesh", meshType, value =>
                {
                    AI.Config.materialPreviewMesh = (int)value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Material, false);
                }, "The mesh shape to use for rendering material previews."));
            }

            return section;
        }

        private VisualElement BuildNativeUiSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("UI Previews");
            section.Add(CreateNativeToggleRow("Enable UI Previews", AI.Config.generateUIPreviews, value =>
            {
                AI.Config.generateUIPreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.UI, true);
            }, "Generate custom previews for UI and Canvas prefabs."));
            return section;
        }

        private VisualElement BuildNativeFontSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Fonts");
            section.Add(CreateNativeToggleRow("Enable Font Previews", AI.Config.generateFontPreviews, value =>
            {
                AI.Config.generateFontPreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.Font, true);
            }, "Generate custom previews for font files."));

            if (AI.Config.generateFontPreviews)
            {
                section.Add(CreateNativeColorRow("Font Color", ParseNativeColor(AI.Config.cpFontColor, Color.black), value =>
                {
                    AI.Config.cpFontColor = ColorUtility.ToHtmlStringRGBA(value);
                    ApplyNativePreviewSetting(PreviewTypeFlags.Font, false);
                }, "Color of the text in font previews."));
            }

            return section;
        }

        private VisualElement BuildNativeParticleSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Particle Systems");
            section.Add(CreateNativeToggleRow("Enable Custom Previews", AI.Config.generateParticlePreviews, value =>
            {
                AI.Config.generateParticlePreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.Particle, true);
            }, "Generate custom previews for Particle System prefabs."));

            if (AI.Config.generateParticlePreviews)
            {
                section.Add(CreateNativeToggleRow("Animated", AI.Config.generateAnimatedParticlePreviews, value =>
                {
                    AI.Config.generateAnimatedParticlePreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Particle, true);
                }, "Create animated preview showing particle system over time."));
                section.Add(CreateNativeFloatRow("Min Visible Duration", AI.Config.cpParticleMinimumVisibleDuration, value =>
                {
                    AI.Config.cpParticleMinimumVisibleDuration = Mathf.Max(0f, value);
                    ApplyNativePreviewSetting(PreviewTypeFlags.Particle, false);
                }, "s", "Animated particle previews play for at least this long."));
            }

            return section;
        }

        private VisualElement BuildNativeVfxSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("VFX Graph");
#if USE_VFX
            if (AssetUtils.IsOnURP() || AssetUtils.IsOnHDRP())
            {
                section.Add(CreateNativeToggleRow("Enable Custom Previews", AI.Config.generateVFXPreviews, value =>
                {
                    AI.Config.generateVFXPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.VFX, true);
                }, "Generate custom previews for VFX Graph assets."));

                if (AI.Config.generateVFXPreviews)
                {
                    section.Add(CreateNativeToggleRow("Animated", AI.Config.generateAnimatedVFXPreviews, value =>
                    {
                        AI.Config.generateAnimatedVFXPreviews = value;
                        ApplyNativePreviewSetting(PreviewTypeFlags.VFX, true);
                    }, "Create animated preview showing VFX over time."));
                    section.Add(CreateNativeFloatRow("Min Visible Duration", AI.Config.cpVFXMinimumVisibleDuration, value =>
                    {
                        AI.Config.cpVFXMinimumVisibleDuration = Mathf.Max(0f, value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.VFX, false);
                    }, "s", "Animated VFX previews play for at least this long."));
                }
            }
            else
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("VFX Graph requires URP or HDRP to function.", MessageType.Info));
            }
#elif UNITY_6000_0_OR_NEWER
            section.Add(AssetInventoryUITK.CreateHelpBox("VFX Graph package is not installed.", MessageType.Info));
            section.Add(AssetInventoryUITK.CreateSecondaryButton("Install Visual Effects Graph Package", () => Client.Add("com.unity.visualeffectgraph")));
#else
            section.Add(AssetInventoryUITK.CreateHelpBox("VFX Graph previews require Unity 6 and above.", MessageType.Info));
#endif
            return section;
        }

        private VisualElement BuildNativeVideoSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Videos");
#if UNITY_EDITOR_WIN
            section.Add(CreateNativeToggleRow("Enable Video Previews", AI.Config.generateVideoPreviews, value =>
            {
                AI.Config.generateVideoPreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.Video, true);
            }, "Generate previews for video files."));

            if (AI.Config.generateVideoPreviews)
            {
                section.Add(CreateNativeToggleRow("Animated", AI.Config.generateAnimatedVideoPreviews, value =>
                {
                    AI.Config.generateAnimatedVideoPreviews = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.Video, true);
                }, "Create multi-frame preview showing video frames over time."));
            }
#else
            section.Add(AssetInventoryUITK.CreateHelpBox("Video preview generation is only available on Windows.", MessageType.Info));
#endif
            return section;
        }

        private VisualElement BuildNativeSceneSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Scenes (Experimental)");
            section.Add(CreateNativeToggleRow("Enable Scene Previews", AI.Config.generateScenePreviews, value =>
            {
                AI.Config.generateScenePreviews = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.None, false);
            }, "Generate custom previews for Unity scene files."));
            return section;
        }

        private VisualElement BuildNativeBackgroundSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Background");
            section.Add(CreateNativeEnumRow("Type", AI.Config.cpBackgroundType, value =>
            {
                AI.Config.cpBackgroundType = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, true);
            }, "Background type for the preview."));

            switch (AI.Config.cpBackgroundType)
            {
                case CustomPreviewBackgroundType.SolidColor:
                    section.Add(CreateNativeColorRow("Color (BiRP/URP)", ParseNativeColor(AI.Config.cpBackgroundColor, new Color(88f / 255, 88f / 255, 88f / 255)), value =>
                    {
                        AI.Config.cpBackgroundColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Background color used by the Built-in Render Pipeline and URP."));
                    section.Add(CreateNativeColorRow("Color (HDRP)", ParseNativeColor(AI.Config.cpBackgroundColorHDRP, new Color(34f / 255, 34f / 255, 34f / 255)), value =>
                    {
                        AI.Config.cpBackgroundColorHDRP = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Background color used by HDRP."));
                    break;

                case CustomPreviewBackgroundType.TwoColorGradient:
                    section.Add(CreateNativeColorRow("Top Color", ParseNativeColor(AI.Config.cpGradient2TopColor, new Color(0.5f, 0.5f, 0.5f)), value =>
                    {
                        AI.Config.cpGradient2TopColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the top of the two-color gradient."));
                    section.Add(CreateNativeColorRow("Bottom Color", ParseNativeColor(AI.Config.cpGradient2BottomColor, new Color(0.25f, 0.25f, 0.25f)), value =>
                    {
                        AI.Config.cpGradient2BottomColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the bottom of the two-color gradient."));
                    section.Add(CreateNativeSliderRow("Rotation", AI.Config.cpGradientRotation, 0f, 360f, value =>
                    {
                        AI.Config.cpGradientRotation = value;
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Rotate the gradient around the preview in degrees."));
                    break;

                case CustomPreviewBackgroundType.FourColorGradient:
                    section.Add(CreateNativeColorRow("Top-Left", ParseNativeColor(AI.Config.cpGradient4TopLeftColor, new Color(0.5f, 0.5f, 0.5f)), value =>
                    {
                        AI.Config.cpGradient4TopLeftColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the top-left of the four-color gradient."));
                    section.Add(CreateNativeColorRow("Top-Right", ParseNativeColor(AI.Config.cpGradient4TopRightColor, new Color(0.375f, 0.375f, 0.375f)), value =>
                    {
                        AI.Config.cpGradient4TopRightColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the top-right of the four-color gradient."));
                    section.Add(CreateNativeColorRow("Bottom-Left", ParseNativeColor(AI.Config.cpGradient4BottomLeftColor, new Color(0.25f, 0.25f, 0.25f)), value =>
                    {
                        AI.Config.cpGradient4BottomLeftColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the bottom-left of the four-color gradient."));
                    section.Add(CreateNativeColorRow("Bottom-Right", ParseNativeColor(AI.Config.cpGradient4BottomRightColor, new Color(0.1875f, 0.1875f, 0.1875f)), value =>
                    {
                        AI.Config.cpGradient4BottomRightColor = ColorUtility.ToHtmlStringRGBA(value);
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Color at the bottom-right of the four-color gradient."));
                    section.Add(CreateNativeSliderRow("Rotation", AI.Config.cpGradientRotation, 0f, 360f, value =>
                    {
                        AI.Config.cpGradientRotation = value;
                        ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                    }, "Rotate the gradient around the preview in degrees."));
                    break;
            }

            return section;
        }

        private VisualElement BuildNativeQualitySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Rendering Quality");
            section.Add(CreateNativeSliderIntRow("Super-sampling", AI.Config.cpSuperSamplingMultiplier, 1, 4, value =>
            {
                AI.Config.cpSuperSamplingMultiplier = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Render multiplier for higher quality."));

            if (ShowAdvanced())
            {
                Label nativeSize = AssetInventoryUITK.CreateCopyLabel($"Native render size: {AI.Config.upscaleSize * AI.Config.cpSuperSamplingMultiplier} pixels");
                nativeSize.AddToClassList(PreviewHintClass);
                section.Add(nativeSize);
                section.Add(CreateNativeDepthRow());
            }

            return section;
        }

        private VisualElement BuildNativeLightingSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Lighting");
            section.Add(CreateNativeToggleRow("Directional Light", AI.Config.cpUseDirectionalLight, value =>
            {
                AI.Config.cpUseDirectionalLight = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Use directional light. If off, uses point light."));
            section.Add(CreateNativeColorRow("Light Color", ParseNativeColor(AI.Config.cpLightColor, Color.white), value =>
            {
                AI.Config.cpLightColor = ColorUtility.ToHtmlStringRGBA(value);
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Color tint of the light source."));

            section.Add(CreateNativeHint("Light Intensity"));
            section.Add(CreateNativeSliderRow("Built-in RP", AI.Config.cpLightIntensity, 0f, 2f, value =>
            {
                AI.Config.cpLightIntensity = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Directional or point-light intensity for the Built-in Render Pipeline."));
            section.Add(CreateNativeSliderRow("URP", AI.Config.cpLightIntensityURP, 0f, 2f, value =>
            {
                AI.Config.cpLightIntensityURP = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Directional or point-light intensity for URP."));
            section.Add(CreateNativeSliderRow("HDRP (Lux)", AI.Config.cpLightIntensityHDRP, 0f, 20000f, value =>
            {
                AI.Config.cpLightIntensityHDRP = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Directional light intensity in lux for HDRP."));

            section.Add(CreateNativeHint("Light Rotation"));
            section.Add(CreateNativeSliderRow("Pitch", AI.Config.cpLightRotationX, 0f, 360f, value =>
            {
                AI.Config.cpLightRotationX = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Vertical rotation of the primary light in degrees."));
            section.Add(CreateNativeSliderRow("Yaw", AI.Config.cpLightRotationY, 0f, 360f, value =>
            {
                AI.Config.cpLightRotationY = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Horizontal rotation of the primary light in degrees."));

            section.Add(CreateNativeToggleRow("Use Secondary Light", AI.Config.cpUseSecondaryLight, value =>
            {
                AI.Config.cpUseSecondaryLight = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, true);
            }, "Enable a secondary rim/fill light for better depth perception."));

            if (AI.Config.cpUseSecondaryLight)
            {
                section.Add(CreateNativeColorRow("Secondary Color", ParseNativeColor(AI.Config.cpSecondaryLightColor, new Color(0.4f, 0.4f, 0.45f)), value =>
                {
                    AI.Config.cpSecondaryLightColor = ColorUtility.ToHtmlStringRGBA(value);
                    ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                }, "Color tint of the secondary rim or fill light."));
                section.Add(CreateNativeSliderRow("Intensity Multiplier", AI.Config.cpSecondaryLightIntensityMultiplier, 0f, 2f, value =>
                {
                    AI.Config.cpSecondaryLightIntensityMultiplier = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                }, "Secondary-light intensity relative to the primary light."));
                section.Add(CreateNativeHint("Secondary Light Rotation"));
                section.Add(CreateNativeSliderRow("Pitch", AI.Config.cpSecondaryLightRotationX, 0f, 360f, value =>
                {
                    AI.Config.cpSecondaryLightRotationX = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                }, "Vertical rotation of the secondary light in degrees."));
                section.Add(CreateNativeSliderRow("Yaw", AI.Config.cpSecondaryLightRotationY, 0f, 360f, value =>
                {
                    AI.Config.cpSecondaryLightRotationY = value;
                    ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
                }, "Horizontal rotation of the secondary light in degrees."));
            }

            return section;
        }

        private VisualElement BuildNativeEnvironmentSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Environment");
            section.Add(CreateNativeSliderRow("Ambient Intensity", AI.Config.cpAmbientIntensity, 0f, 2f, value =>
            {
                AI.Config.cpAmbientIntensity = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            }, "Global ambient light intensity multiplier."));
            return section;
        }

        private VisualElement BuildNativeAnimationSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Animated Previews");
            section.Add(CreateNativeSliderIntRow("Animation Frames", AI.Config.animationGrid, 2, 6, value =>
            {
                AI.Config.animationGrid = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.AllAnimated, false);
            }, "Number of frames to create per side of the preview spritesheet."));
            section.Add(CreateNativeHint("will be squared, e.g. 4 = 16 frames"));
            section.Add(CreateNativeFloatRow("Animation Speed", AI.Config.animationSpeed, value =>
            {
                AI.Config.animationSpeed = Mathf.Max(0.01f, value);
                ApplyNativePreviewSetting(PreviewTypeFlags.None, false);
            }, "s", "Time interval until a new frame of the animation is shown."));
            section.Add(CreateNativeToggleRow("Embed Indicator", AI.Config.embedAnimatedPreviewIndicator, value =>
            {
                AI.Config.embedAnimatedPreviewIndicator = value;
                ApplyNativePreviewSetting(PreviewTypeFlags.None, false);
            }, "Embed a small play icon when an animated preview exists."));
            return section;
        }

        private VisualElement CreateNativeDepthRow()
        {
            int[] depthOptions = {0, 16, 24, 32};
            List<string> depthLabels = new List<string> {"None (0-bit)", "Low (16-bit)", "Standard (24-bit)", "High (32-bit)"};
            int currentDepthIndex = Array.IndexOf(depthOptions, AI.Config.cpDepth);
            if (currentDepthIndex < 0) currentDepthIndex = 2;

            PopupField<string> popup = new PopupField<string>(depthLabels, currentDepthIndex);
            popup.tooltip = "Depth buffer precision. 24-bit is recommended.";
            popup.RegisterValueChangedCallback(evt =>
            {
                int newIndex = Mathf.Max(0, depthLabels.IndexOf(evt.newValue));
                AI.Config.cpDepth = depthOptions[newIndex];
                ApplyNativePreviewSetting(PreviewTypeFlags.AllRendered, false);
            });
            return AssetInventoryUITK.CreateFieldRow("Depth Buffer", popup);
        }

        private void BuildNativePreviewPanel(VisualElement parent)
        {
            VisualElement header = CommonUITK.CreateContainer(PreviewGalleryHeaderClass);
            Label title = AssetInventoryUITK.CreateCopyLabel("Preview Gallery");
            title.AddToClassList(PreviewGalleryTitleClass);
            header.Add(title);
            header.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            Button playback = AssetInventoryUITK.CreateSecondaryButton(_playAnimatedPreviews ? "Playing" : "Sprite Sheet", ToggleNativeAnimatedPreviewMode);
            playback.tooltip = _playAnimatedPreviews
                ? "Animated previews are playing. Click to show their full sprite sheets instead."
                : "Full sprite sheets are shown. Click to play animated previews.";
            header.Add(playback);
            parent.Add(header);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.AddToClassList(PreviewSettingsScrollClass);
            parent.Add(scroll);

            ScrollView galleryScroll = new ScrollView(ScrollViewMode.Horizontal);
            galleryScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            galleryScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            galleryScroll.AddToClassList(PreviewGallerySharedScrollClass);

            VisualElement rows = CommonUITK.CreateContainer(PreviewGalleryRowsClass);
            AddNativePreviewRow(rows, "Static Previews", BuildNativeStaticPreviewCards());
            AddNativePreviewRow(rows, "Animated Previews", BuildNativeAnimatedPreviewCards());
            galleryScroll.Add(rows);
            scroll.Add(galleryScroll);
        }

        private List<VisualElement> BuildNativeStaticPreviewCards()
        {
            List<VisualElement> cards = new List<VisualElement>
            {
                CreateNativePreviewCard("3D Model", () => _static3DTexture, () => AI.Config.generateCustomModelPreviews ? null : "Custom previews disabled"),
                CreateNativePreviewCard("FBX Model", () => _staticFBXTexture, () => !AI.Config.generateFBXPreviews ? "FBX previews disabled" : string.IsNullOrEmpty(_sampleFBXPath) ? "No sample FBX" : null)
            };

            if (AI.DEBUG_MODE)
            {
                cards.Add(CreateNativePreviewCard("Anim File", () => _staticAnimTexture, () => !AI.Config.generateAnimPreviews ? "Anim previews disabled" : string.IsNullOrEmpty(_sampleFBXPath) ? "No sample" : null));
            }

            cards.Add(CreateNativePreviewCard("Material", () => _staticMaterialTexture, () => AI.Config.generateMaterialPreviews ? null : "Material previews disabled"));
            cards.Add(CreateNativePreviewCard("Particle System", () => _staticParticleTexture, () => AI.Config.generateParticlePreviews ? null : "Particle previews disabled"));
            cards.Add(CreateNativePreviewCard(GetNativeVfxStaticTitle(), () => _staticVFXTexture, GetNativeVfxStaticFallback));
            cards.Add(CreateNativePreviewCard("UI Canvas", () => _staticUITexture, () => AI.Config.generateUIPreviews ? null : "UI previews disabled"));
#if UNITY_2022_3_OR_NEWER
            cards.Add(CreateNativePreviewCard("Font (LegacyRuntime)", () => _previewFontTexture, () => AI.Config.generateFontPreviews ? null : "Font previews disabled"));
#else
            cards.Add(CreateNativePreviewCard("Font (Arial)", () => _previewFontTexture, () => AI.Config.generateFontPreviews ? null : "Font previews disabled"));
#endif
#if UNITY_EDITOR_WIN
            cards.Add(CreateNativePreviewCard("Video", () => _staticVideoTexture, () => AI.Config.generateVideoPreviews ? null : "Video previews disabled"));
#endif
            return cards;
        }

        private List<VisualElement> BuildNativeAnimatedPreviewCards()
        {
            List<VisualElement> cards = new List<VisualElement>
            {
                CreateNativePreviewCard("3D (360°)", () => _animated3DTexture, () =>
                {
                    if (!AI.Config.generateCustomModelPreviews) return "Custom previews disabled";
                    return AI.Config.generateAnimatedModelPreviews ? null : "Enable 360° rotation";
                }, () => _anim3DPlayer),
                CreateNativePreviewCard("FBX (Anim)", () => _animatedFBXTexture, () =>
                {
                    if (!AI.Config.generateFBXPreviews) return "FBX previews disabled";
                    if (string.IsNullOrEmpty(_sampleFBXPath)) return "No sample FBX";
                    return AI.Config.generateAnimatedFBXPreviews ? null : "Enable animated";
                }, () => _animFBXPlayer)
            };

            if (AI.DEBUG_MODE)
            {
                cards.Add(CreateNativePreviewCard("Anim (Anim)", () => _animatedAnimTexture, () =>
                {
                    if (!AI.Config.generateAnimPreviews) return "Anim previews disabled";
                    if (string.IsNullOrEmpty(_sampleFBXPath)) return "No sample";
                    return AI.Config.generateAnimatedAnimPreviews ? null : "Enable animated";
                }, () => _animAnimPlayer));
            }

            cards.Add(CreateNativePreviewCard("-", () => null, () => "Static only"));
            cards.Add(CreateNativePreviewCard("Particles (Anim)", () => _animatedParticleTexture, () =>
            {
                if (!AI.Config.generateParticlePreviews) return "Particle previews disabled";
                return AI.Config.generateAnimatedParticlePreviews ? null : "Enable animated";
            }, () => _animParticlePlayer));
            cards.Add(CreateNativePreviewCard(GetNativeVfxAnimatedTitle(), () => _animatedVFXTexture, GetNativeVfxAnimatedFallback, () => _animVFXPlayer));
            cards.Add(CreateNativePreviewCard("-", () => null, () => "UI has no animation"));
            cards.Add(CreateNativePreviewCard("-", () => null, () => "Fonts have no animation"));
#if UNITY_EDITOR_WIN
            cards.Add(CreateNativePreviewCard("Video (Anim)", () => _animatedVideoTexture, () =>
            {
                if (!AI.Config.generateVideoPreviews) return "Video previews disabled";
                return AI.Config.generateAnimatedVideoPreviews ? null : "Enable multi-frame";
            }, () => _animVideoPlayer));
#endif
            return cards;
        }

        private void AddNativePreviewRow(VisualElement parent, string title, List<VisualElement> cards)
        {
            Label rowTitle = AssetInventoryUITK.CreateCopyLabel(title);
            rowTitle.AddToClassList(PreviewGalleryRowTitleClass);
            parent.Add(rowTitle);

            VisualElement row = CommonUITK.CreateContainer(PreviewGalleryRowClass);
            for (int i = 0; i < cards.Count; i++)
            {
                row.Add(cards[i]);
            }
            parent.Add(row);
        }

        private VisualElement CreateNativePreviewCard(string title, Func<Texture2D> textureFactory, Func<string> fallbackFactory, Func<AnimationPlayer> playerFactory = null)
        {
            VisualElement card = CommonUITK.CreateContainer(PreviewCardClass);
            Label label = AssetInventoryUITK.CreateCopyLabel(title);
            label.AddToClassList(PreviewCardTitleClass);
            card.Add(label);

            VisualElement frame = CommonUITK.CreateContainer(PreviewFrameClass);
            Image image = new Image {scaleMode = ScaleMode.ScaleToFit};
            image.AddToClassList(PreviewFrameImageClass);
            frame.Add(image);

            Label fallback = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            fallback.AddToClassList(PreviewFrameFallbackClass);
            frame.Add(fallback);
            card.Add(frame);

            _nativePreviewCards.Add(new NativePreviewCard(image, fallback, textureFactory, fallbackFactory, playerFactory));
            return card;
        }

        private void UpdateNativePreviewImages()
        {
            if (_nativePreviewCards == null || _nativePreviewCards.Count == 0) return;

            for (int i = 0; i < _nativePreviewCards.Count; i++)
            {
                NativePreviewCard card = _nativePreviewCards[i];
                Texture2D texture = null;
                if (_playAnimatedPreviews && card.PlayerFactory != null)
                {
                    AnimationPlayer player = card.PlayerFactory();
                    if (player != null && player.IsLoaded)
                    {
                        texture = player.GetCurrentFrame();
                    }
                }
                if (texture == null)
                {
                    texture = card.TextureFactory();
                }

                string fallback = card.FallbackFactory();
                if (texture != null)
                {
                    card.Image.image = texture;
                    card.Image.style.display = DisplayStyle.Flex;
                    card.Fallback.style.display = DisplayStyle.None;
                }
                else
                {
                    card.Image.image = null;
                    card.Image.style.display = DisplayStyle.None;
                    card.Fallback.text = _isGeneratingPreview && string.IsNullOrEmpty(fallback) ? "Generating..." : fallback ?? string.Empty;
                    card.Fallback.style.display = DisplayStyle.Flex;
                }
            }
        }

        private void ToggleNativeAnimatedPreviewMode()
        {
            _playAnimatedPreviews = !_playAnimatedPreviews;
            if (_playAnimatedPreviews)
            {
                InitializeAnimationPlayers();
            }
            else
            {
                DisposeAnimationPlayers();
            }
            BuildContent();
        }

        private VisualElement CreateNativeToggleRow(string label, bool value, Action<bool> onChange, string tooltip = null)
        {
            return NativePreviewToggleFormBuilder.CreateToggleRow(label, value, onChange, tooltip);
        }

        private VisualElement CreateNativeIntegerRow(string label, int value, Action<int> onChange, string suffix = null, string tooltip = null)
        {
            return NativePreviewFieldFormBuilder.CreateIntegerRow(label, value, onChange, suffix, tooltip);
        }

        private VisualElement CreateNativeFloatRow(string label, float value, Action<float> onChange, string suffix = null, string tooltip = null)
        {
            return NativePreviewFieldFormBuilder.CreateFloatRow(label, value, onChange, suffix, tooltip);
        }

        private VisualElement CreateNativeSliderRow(string label, float value, float min, float max, Action<float> onChange, string tooltip = null)
        {
            return NativePreviewFieldFormBuilder.CreateSliderRow(label, value, min, max, onChange, tooltip);
        }

        private VisualElement CreateNativeSliderIntRow(string label, int value, int min, int max, Action<int> onChange, string tooltip = null)
        {
            return NativePreviewFieldFormBuilder.CreateSliderIntRow(label, value, min, max, onChange, tooltip);
        }

        private VisualElement CreateNativeEnumRow<TEnum>(string label, TEnum value, Action<TEnum> onChange, string tooltip = null) where TEnum : Enum
        {
            return NativePreviewFieldFormBuilder.CreateEnumRow(label, value, onChange, tooltip);
        }

        private VisualElement CreateNativeColorRow(string label, Color value, Action<Color> onChange, string tooltip = null)
        {
            return NativePreviewFieldFormBuilder.CreateColorRow(label, value, onChange, tooltip);
        }

        private Label CreateNativeHint(string text)
        {
            Label hint = AssetInventoryUITK.CreateCopyLabel(text);
            hint.AddToClassList(PreviewHintClass);
            return hint;
        }

        private void ApplyNativePreviewSetting(PreviewTypeFlags dirtyFlags, bool rebuild)
        {
            if (dirtyFlags != PreviewTypeFlags.None)
            {
                MarkPreviewsDirty(dirtyFlags);
            }

            AI.SaveConfig();
            if (rebuild)
            {
                BuildContent();
            }
            else
            {
                UpdateNativePreviewImages();
            }
        }

        private static Color ParseNativeColor(string htmlColor, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(htmlColor) && ColorUtility.TryParseHtmlString("#" + htmlColor.TrimStart('#'), out Color parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private static string GetNativeVfxStaticTitle()
        {
#if USE_VFX
            return AssetUtils.IsOnURP() || AssetUtils.IsOnHDRP() ? "VFX Graph" : "VFX Graph (N/A)";
#else
            return "VFX Graph (N/A)";
#endif
        }

        private static string GetNativeVfxAnimatedTitle()
        {
#if USE_VFX
            return AssetUtils.IsOnURP() || AssetUtils.IsOnHDRP() ? "VFX (Anim)" : "VFX (N/A)";
#else
            return "VFX (N/A)";
#endif
        }

        private string GetNativeVfxStaticFallback()
        {
#if USE_VFX
            if (!(AssetUtils.IsOnURP() || AssetUtils.IsOnHDRP())) return "VFX requires URP/HDRP";
            if (!AI.Config.generateVFXPreviews) return "VFX previews disabled";
            if (!HasPreviewVFXAsset()) return "No sample VFX";
            return null;
#else
            return "VFX Graph not available";
#endif
        }

        private string GetNativeVfxAnimatedFallback()
        {
#if USE_VFX
            if (!(AssetUtils.IsOnURP() || AssetUtils.IsOnHDRP())) return "VFX requires URP/HDRP";
            if (!AI.Config.generateVFXPreviews) return "VFX previews disabled";
            if (!AI.Config.generateAnimatedVFXPreviews) return "Enable animated";
            if (!HasPreviewVFXAsset()) return "No sample VFX";
            return null;
#else
            return "VFX Graph not available";
#endif
        }

        private sealed class NativePreviewCard
        {
            internal NativePreviewCard(Image image, Label fallback, Func<Texture2D> textureFactory, Func<string> fallbackFactory, Func<AnimationPlayer> playerFactory)
            {
                Image = image;
                Fallback = fallback;
                TextureFactory = textureFactory;
                FallbackFactory = fallbackFactory;
                PlayerFactory = playerFactory;
            }

            internal Image Image { get; }
            internal Label Fallback { get; }
            internal Func<Texture2D> TextureFactory { get; }
            internal Func<string> FallbackFactory { get; }
            internal Func<AnimationPlayer> PlayerFactory { get; }
        }
    }
}
