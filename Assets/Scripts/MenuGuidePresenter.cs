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
        private bool greeted;
        private bool canWave;
        private float nextWave;

        public bool Show(VisualElement container, GameObject replacement = null)
        {
            if (stage == null)
            {
                var prefab = replacement != null ? replacement : Resources.Load<GameObject>("MenuGuide");
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
                portraitCamera.backgroundColor = new Color(0.87f, 0.94f, 0.93f);
                portraitCamera.fieldOfView = 30; portraitCamera.nearClipPlane = 0.05f;
                var renderers = character.GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(stage.transform.position + Vector3.up, Vector3.one);
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                }
                Vector3 focus = bounds.center + Vector3.up * bounds.size.y * 0.20f;
                float distance = Mathf.Max(0.35f, bounds.size.y * 0.34f) / Mathf.Tan(15 * Mathf.Deg2Rad);
                portraitCamera.farClipPlane = Mathf.Max(15, distance * 4);
                portraitCamera.transform.position = focus + Vector3.forward * distance;
                portraitCamera.transform.LookAt(focus);
                portrait = new RenderTexture(768, 960, 24) { name = "Menu Guide Portrait" };
                portrait.Create(); portraitCamera.targetTexture = portrait;
                AddLight("Guide Key Light", new Vector3(30, 180, 0), 0.9f);
                AddLight("Guide Fill Light", new Vector3(15, 320, 0), 0.35f);
            }
            stage.SetActive(true);
            var image = new Image { image = portrait, scaleMode = ScaleMode.ScaleToFit };
            image.style.height = 340; image.style.flexShrink = 0;
            image.style.backgroundColor = portraitCamera.backgroundColor;
            container.Add(image);
            image.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (portrait == null || evt.newRect.width < 1 || evt.newRect.height < 1) return;
                int width = Mathf.Clamp(Mathf.RoundToInt(960 * evt.newRect.width / evt.newRect.height), 256, 1536);
                if (width == portrait.width) return;
                portraitCamera.targetTexture = null; portrait.Release(); portrait.width = width; portrait.Create();
                portraitCamera.targetTexture = portrait;
            });
            if (!greeted) { greeted = true; StartCoroutine(Greet()); }
            return true;
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
