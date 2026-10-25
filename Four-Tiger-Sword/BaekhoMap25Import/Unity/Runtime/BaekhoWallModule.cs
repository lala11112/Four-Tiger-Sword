using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Authoring metadata exported in the bounds catalog. Does not move objects or create lights.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Baekho/Wall Module")]
    public sealed class BaekhoWallModule : MonoBehaviour
    {
        // Preserve existing serialized values when adding new module types.
        public enum ModuleKind { Single, Straight, InnerCorner, OuterCorner, EndCap, Elbow, Tee }
        public enum InstallSite { Wall, DoorTop, DoorSide, Upper, Lower, Corner }
        public enum HeightMode { Center, Floor, Top }
        public enum DoorSide { Left, Right, Both }

        [Tooltip("Single: wall/door fixture. Elbow/Tee: flat-wall horizontal/vertical joints. Other kinds: fixed-size pipe, cable or trim parts.")]
        public ModuleKind kind = ModuleKind.Single;
        [Tooltip("Connected parts must have the same non-empty family, e.g. FactoryPipe100.")]
        public string family = "";
        public InstallSite[] sites = { InstallSite.Wall };
        [Tooltip("Center: wall midpoint + height. Floor: floor + height. Top: wall top - height.")]
        public HeightMode heightMode = HeightMode.Center;
        public float height;
        [Min(0.05f), Tooltip("Preferred fixture spacing in metres (0.05..100). Connected parts use socket distances.")]
        public float spacing = 3;
        [Range(0, 100), Tooltip("Chance to use an eligible wall or doorway, in percent.")]
        public float probability = 100;
        public DoorSide doorSide = DoorSide.Both;

        [Tooltip("Root or child transforms on the wall contact plane. Blue +Z arrows point INTO the room. Corners need two perpendicular wall normals. Symmetric end caps may omit mounts.")]
        public Transform[] mounts = new Transform[0];
        [Tooltip("Pipe/cable end centres. Blue +Z arrows point OUT of the part, tangent to the wall. Elbow/Tee allow vertical socket arrows. Single fixtures have no sockets.")]
        public Transform[] sockets = new Transform[0];
        [Tooltip("Root-local axis-aligned occupancy boxes in metres at root scale 1. Corners/Elbow require at least two boxes; Tee requires three, one per arm. Empty single/straight boxes fall back to measured geometry.")]
        public Bounds[] collisionBoxes = new Bounds[0];

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Color color = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, .7f, .1f, .8f);
            if (collisionBoxes != null)
                foreach (Bounds bounds in collisionBoxes) Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.matrix = Matrix4x4.identity;
            DrawReferences(mounts, Color.cyan);
            DrawReferences(sockets, Color.magenta);
            Gizmos.matrix = previous;
            Gizmos.color = color;
        }

        private static void DrawReferences(Transform[] references, Color color)
        {
            if (references == null) return;
            Gizmos.color = color;
            foreach (Transform reference in references)
            {
                if (reference == null) continue;
                Gizmos.DrawWireSphere(reference.position, .035f);
                Vector3 tip = reference.position + reference.forward * .25f;
                Gizmos.DrawLine(reference.position, tip);
                Gizmos.DrawLine(tip, tip - reference.forward * .07f + reference.right * .04f);
                Gizmos.DrawLine(tip, tip - reference.forward * .07f - reference.right * .04f);
            }
        }
    }
}
