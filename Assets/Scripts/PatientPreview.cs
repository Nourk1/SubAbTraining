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
        private readonly System.Collections.Generic.Dictionary<GameObject, int> originalLayers =
            new System.Collections.Generic.Dictionary<GameObject, int>();

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
                previewCamera.backgroundColor = Color.clear;
                // Exclude the old scene UI from the avatar view.
                // Render only the patient; the hospital image fills the UI behind this texture.
                previewCamera.cullingMask = 1 << 31;
                texture = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32) { name = "Patient View" };
                texture.Create();
                previewCamera.targetTexture = texture;
            }

            foreach (var node in patient.GetComponentsInChildren<Transform>(true))
            {
                if (!originalLayers.ContainsKey(node.gameObject)) originalLayers.Add(node.gameObject, node.gameObject.layer);
                node.gameObject.layer = 31;
            }
            var bounds = new Bounds(patient.position + Vector3.up, Vector3.one);
            bool found = false;
            foreach (var renderer in patient.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            // Anchor the hips at the lower screen edge and leave a little room above the head.
            float hipsY = bounds.min.y + bounds.size.y * 0.5f;
            var animator = patient.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) hipsY = hips.position.y;
            }
            float upperBodyHeight = Mathf.Max(0.1f, bounds.max.y - hipsY);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = upperBodyHeight * 0.54f;
            Vector3 focus = bounds.center;
            focus.y = hipsY + previewCamera.orthographicSize;
            Vector3 forward = Vector3.ProjectOnPlane(source.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            float distance = Mathf.Max(2f, bounds.size.magnitude * 2f);
            previewCamera.farClipPlane = distance + bounds.size.magnitude * 2f + 1f;
            previewCamera.transform.SetPositionAndRotation(
                focus - forward * distance, Quaternion.LookRotation(forward, Vector3.up));
            previewCamera.enabled = true;

            image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.style.flexGrow = 1;
            image.style.minHeight = 0;
            image.style.backgroundColor = previewCamera.backgroundColor;
            container.Add(image);
            image.RegisterCallback<GeometryChangedEvent>(Resize);
            return true;
        }

        private void Resize(GeometryChangedEvent evt)
        {
            if (evt.newRect.width < 1 || evt.newRect.height < 1 || texture == null) return;
            float aspect = evt.newRect.width / evt.newRect.height;
            float scale = Mathf.Min(1f, 2048f / Mathf.Max(evt.newRect.width, evt.newRect.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(evt.newRect.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(evt.newRect.height * scale));
            previewCamera.aspect = aspect;
            if (texture.width == width && texture.height == height) return;
            previewCamera.targetTexture = null;
            texture.Release();
            texture.width = width;
            texture.height = height;
            texture.Create();
            previewCamera.targetTexture = texture;
        }

        public void Hide()
        {
            if (previewCamera != null) previewCamera.enabled = false;
            image = null;
            foreach (var entry in originalLayers)
                if (entry.Key != null) entry.Key.layer = entry.Value;
            originalLayers.Clear();
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (previewCamera != null) previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
