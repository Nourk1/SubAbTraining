using UnityEngine;
using UnityEngine.UIElements;

namespace RealtimePatient
{
    /// <summary>Renders the existing scene avatar without changing the scene camera.</summary>
    public sealed class PatientPreview : MonoBehaviour
    {
        [SerializeField] private Transform patient;
        private Camera previewCamera;
        private RenderTexture texture;
        private Image image;

        public bool Show(VisualElement container)
        {
            if (patient == null)
            {
                var avatar = GameObject.Find("Patient");
                if (avatar != null) patient = avatar.transform;
            }
            var source = Camera.main;
            if (patient == null || source == null) return false;

            if (previewCamera == null)
            {
                var cameraObject = new GameObject("Patient Preview Camera");
                cameraObject.transform.SetParent(transform, false);
                previewCamera = cameraObject.AddComponent<Camera>();
                previewCamera.CopyFrom(source);
                previewCamera.rect = new Rect(0, 0, 1, 1);
                previewCamera.orthographic = false;
                previewCamera.fieldOfView = 32;
                previewCamera.nearClipPlane = 0.05f;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0.84f, 0.91f, 0.92f);
                // Exclude the old scene UI from the avatar view.
                previewCamera.cullingMask = source.cullingMask & ~(1 << 5);
                texture = new RenderTexture(1280, 800, 24) { name = "Patient View" };
                texture.Create();
                previewCamera.targetTexture = texture;
            }

            var bounds = new Bounds(patient.position + Vector3.up, Vector3.one);
            bool found = false;
            foreach (var renderer in patient.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            // Frame the upper body using the original camera's viewing direction.
            Vector3 focus = bounds.center + Vector3.up * bounds.size.y * 0.20f;
            float halfHeight = Mathf.Max(0.32f, bounds.size.y * 0.28f);
            float distance = halfHeight / Mathf.Tan(previewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            previewCamera.transform.SetPositionAndRotation(
                focus - source.transform.forward * distance, source.transform.rotation);
            previewCamera.enabled = true;

            image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            image.style.flexGrow = 1;
            image.style.minHeight = 180;
            image.style.backgroundColor = previewCamera.backgroundColor;
            container.Add(image);
            image.RegisterCallback<GeometryChangedEvent>(Resize);
            return true;
        }

        private void Resize(GeometryChangedEvent evt)
        {
            if (evt.newRect.width < 1 || evt.newRect.height < 1 || texture == null) return;
            float aspect = Mathf.Clamp(evt.newRect.width / evt.newRect.height, 0.5f, 3f);
            int height = Mathf.Clamp(Mathf.RoundToInt(1280 / aspect), 256, 1536);
            if (texture.height == height) return;
            previewCamera.targetTexture = null;
            texture.Release();
            texture.height = height;
            texture.Create();
            previewCamera.targetTexture = texture;
        }

        public void Hide()
        {
            if (previewCamera != null) previewCamera.enabled = false;
            image = null;
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (previewCamera != null) previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
