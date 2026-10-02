using Unity.Netcode;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>Gives each capsule a colour and a floating "P1..P4" label based on its client id.</summary>
    public class PlayerIdentity : NetworkBehaviour
    {
        public static readonly Color[] Palette =
        {
            new Color(0.95f, 0.75f, 0.2f),  // P1 yellow (host)
            new Color(0.25f, 0.6f, 1f),     // P2 blue
            new Color(0.95f, 0.35f, 0.6f),  // P3 pink
            new Color(0.4f, 0.9f, 0.5f),    // P4 green
        };

        [SerializeField] Renderer[] tinted;
        [SerializeField] TextMesh nameLabel;

        public int PlayerNumber => (int)OwnerClientId + 1;
        public Color PlayerColor => Palette[(int)(OwnerClientId % (ulong)Palette.Length)];

        public override void OnNetworkSpawn()
        {
            foreach (var r in tinted)
                if (r) r.material.color = PlayerColor; // per-instance material is fine for 4 players
            if (nameLabel)
            {
                nameLabel.text = $"P{PlayerNumber}";
                nameLabel.color = PlayerColor;
                nameLabel.gameObject.SetActive(!IsOwner); // you don't need to see your own label
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Renderer[] tinted, TextMesh nameLabel)
        {
            this.tinted = tinted; this.nameLabel = nameLabel;
        }
#endif
    }
}
