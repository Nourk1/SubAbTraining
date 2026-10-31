using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace RealtimePatient
{
    /// <summary>Temporary guide portrait, separate from patient logic and replaceable with a prefab.</summary>
    public sealed class MenuGuidePresenter : MonoBehaviour
    {
        private GameObject stage;
        private Camera portraitCamera;
        private Animator animator;
        private RenderTexture portrait;
        private Bounds characterBounds;
        [SerializeField, Range(0f, 0.9f), Tooltip("Fraction of the model height cropped below the menu portrait.")]
        private float shoulderCrop = 0.15f;
        private bool greeted;
        private bool canWave;
        private float nextWave;

        public bool Show(VisualElement container, GameObject replacement = null)
        {
            if (stage == null)
            {
                var prefab = replacement != null ? replacement :
                    Resources.Load<GameObject>("MenuAvatar/MenuAvatar") ?? Resources.Load<GameObject>("MenuGuide");
                if (prefab == null) return false;
                stage = new GameObject("Menu Guide Stage");
                stage.transform.SetParent(transform, false);
                stage.transform.position = new Vector3(1000, 0, 1000);
                var character = Instantiate(prefab, stage.transform);
                foreach (var node in character.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
                animator = character.GetComponentInChildren<Animator>();
                canWave = animator != null && animator.runtimeAnimatorController != null && animator.parameters.Any(p =>
                    p.name == "Wave" && p.type == AnimatorControllerParameterType.Trigger);
                var cameraObject = new GameObject("Guide Portrait Camera");
                cameraObject.transform.SetParent(stage.transform, false);
                portraitCamera = cameraObject.AddComponent<Camera>();
                portraitCamera.cullingMask = 1 << 31;
                portraitCamera.clearFlags = CameraClearFlags.SolidColor;
                portraitCamera.backgroundColor = Color.clear;
                portraitCamera.fieldOfView = 30; portraitCamera.nearClipPlane = 0.05f;
                var renderers = character.GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(stage.transform.position + Vector3.up, Vector3.one);
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                }
                characterBounds = bounds;
                FrameCharacter(768f / 960f);
                portrait = new RenderTexture(768, 960, 24, RenderTextureFormat.ARGB32) { name = "Menu Guide Portrait" };
                portrait.Create(); portraitCamera.targetTexture = portrait;
                AddLight("Guide Key Light", new Vector3(30, 180, 0), 0.9f);
                AddLight("Guide Fill Light", new Vector3(15, 320, 0), 0.35f);
            }
            stage.SetActive(true);
            var image = new Image { image = portrait, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.style.flexGrow = 1; image.style.flexBasis = 0; image.style.minHeight = 0;
            image.style.backgroundColor = portraitCamera.backgroundColor;
            container.Add(image);
            image.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (portrait == null || evt.newRect.width < 1 || evt.newRect.height < 1) return;
                float aspect = evt.newRect.width / evt.newRect.height;
                FrameCharacter(aspect);
                float resolutionScale = Mathf.Min(1f, 2048f / Mathf.Max(evt.newRect.width, evt.newRect.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(evt.newRect.width * resolutionScale));
                int height = Mathf.Max(1, Mathf.RoundToInt(evt.newRect.height * resolutionScale));
                if (width == portrait.width && height == portrait.height) return;
                portraitCamera.targetTexture = null;
                portrait.Release(); portrait.width = width; portrait.height = height; portrait.Create();
                portraitCamera.targetTexture = portrait;
            });
            if (!greeted) { greeted = true; StartCoroutine(Greet()); }
            return true;
        }
        private void FrameCharacter(float aspect)
        {
            // Anchor a cropped bust to the bottom edge rather than fitting the whole model.
            // Orthographic projection keeps that edge fixed regardless of mesh depth.
            float cropY = characterBounds.min.y + characterBounds.size.y * shoulderCrop;
            float portraitHeight = Mathf.Max(0.01f, characterBounds.max.y - cropY);
            float halfHeight = portraitHeight * 0.56f;
            halfHeight = Mathf.Max(halfHeight, characterBounds.extents.x * 0.65f / Mathf.Max(0.01f, aspect));
            portraitCamera.orthographic = true;
            portraitCamera.orthographicSize = halfHeight;
            float distance = Mathf.Max(1f, characterBounds.size.magnitude * 2f);
            portraitCamera.nearClipPlane = 0.001f;
            portraitCamera.farClipPlane = distance + characterBounds.size.magnitude * 2f + 1f;
            var focus = characterBounds.center;
            focus.y = cropY + halfHeight;
            portraitCamera.transform.position = focus + Vector3.forward * distance;
            portraitCamera.transform.LookAt(focus);
        }
        private void AddLight(string title, Vector3 rotation, float intensity)
        {
            var lightObject = new GameObject(title); lightObject.transform.SetParent(stage.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(rotation);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.cullingMask = 1 << 31; light.intensity = intensity; light.shadows = LightShadows.None;
        }
        private IEnumerator Greet() { yield return null; Wave(); }
        public void Wave()
        {
            if (stage == null || !stage.activeSelf || !canWave || Time.unscaledTime < nextWave) return;
            animator.SetTrigger("Wave"); nextWave = Time.unscaledTime + 4;
        }
        public void Hide() { StopAllCoroutines(); if (stage != null) stage.SetActive(false); }
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            if (portraitCamera != null) portraitCamera.targetTexture = null;
            if (portrait != null) { portrait.Release(); Destroy(portrait); }
        }
    }
}
