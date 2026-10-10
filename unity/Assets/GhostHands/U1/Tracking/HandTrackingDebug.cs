using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHands
{
    public sealed class HandTrackingDebug : MonoBehaviour
    {
        public MonoBehaviour handJointSource;
        public bool drawJoints;
        [Min(0.001f)] public float diameter = 0.006f;
        private GameObject root;
        private readonly Transform[][] joints = new Transform[2][];
        private Material material;

        private void LateUpdate() => Refresh();
        public void Refresh()
        {
            var source = handJointSource as IHandJointSource;
            if (!drawJoints || source == null) { if (root != null) root.SetActive(false); return; }
            if (root == null)
            {
                root = new GameObject("Live Hand Debug Joints"); root.transform.SetParent(transform, false);
                material = new Material(Resources.Load<Material>("GhostHands/GhostHand"));
                material.SetColor("_BaseColor", new Color(1, 0.4f, 1, 0.8f));
                for (int side = 0; side < 2; side++)
                {
                    joints[side] = new Transform[21];
                    for (int i = 0; i < 21; i++)
                    {
                        var point = GameObject.CreatePrimitive(PrimitiveType.Sphere); point.name = (side == 0 ? "Left " : "Right ") + i;
                        point.transform.SetParent(root.transform, false); joints[side][i] = point.transform;
                        var collider = point.GetComponent<Collider>(); collider.enabled = false; GhostHandRenderer.Release(collider);
                        var renderer = point.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    }
                }
            }
            root.SetActive(true);
            for (int side = 0; side < 2; side++)
            {
                bool tracked = source.TryGetJoints(side == 1, out var points) && HandConventions.ValidJoints(points);
                for (int i = 0; i < 21; i++)
                {
                    joints[side][i].gameObject.SetActive(tracked);
                    if (!tracked) continue;
                    joints[side][i].position = points[i]; joints[side][i].localScale = Vector3.one * diameter;
                }
            }
        }
        private void OnDisable() { if (root != null) root.SetActive(false); }
        private void OnDestroy()
        {
            if (root != null) GhostHandRenderer.Release(root);
            if (material != null) GhostHandRenderer.Release(material);
        }
    }
}
