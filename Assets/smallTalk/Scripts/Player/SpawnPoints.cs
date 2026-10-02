using UnityEngine;

namespace SmallTalk
{
    /// <summary>Scene object listing where P1..P4 appear. The owning client places itself on spawn.</summary>
    public class SpawnPoints : MonoBehaviour
    {
        [SerializeField] Transform[] points;
        static SpawnPoints s_Instance;

        void Awake() => s_Instance = this;
        void OnDestroy() { if (s_Instance == this) s_Instance = null; }

        public static bool TryGet(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity;
            if (s_Instance == null || s_Instance.points == null || s_Instance.points.Length == 0) return false;
            var t = s_Instance.points[(int)(clientId % (ulong)s_Instance.points.Length)];
            if (!t) return false;
            position = t.position; rotation = t.rotation;
            return true;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform[] points) => this.points = points;
#endif
    }
}
