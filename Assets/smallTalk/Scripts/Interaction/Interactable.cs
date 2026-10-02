using Unity.Netcode;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>
    /// Base class for anything a player can use with E.
    /// Any client may *request* an interaction; only the server decides what happens,
    /// so every player always sees the same puzzle state.
    /// </summary>
    public abstract class Interactable : NetworkBehaviour
    {
        [Tooltip("Server-side sanity check: how far (m) a player may be from this object to use it.")]
        [SerializeField] protected float maxUseDistance = 4f;

        /// <summary>Text shown next to the crosshair, e.g. "Pull lever".</summary>
        public abstract string Prompt { get; }

        /// <summary>Whether E currently does anything (checked on client and server).</summary>
        public virtual bool CanInteract => true;

        /// <summary>Called by the local player when E is pressed while looking at this object.</summary>
        public void RequestInteract()
        {
            if (!IsSpawned || !CanInteract) return;
            InteractRpc();
        }

        [Rpc(SendTo.Server)]
        void InteractRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!CanInteract || !IsSenderInRange(sender)) return;
            OnInteractServer(sender);
        }

        bool IsSenderInRange(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
                return false;
            return Vector3.Distance(client.PlayerObject.transform.position, transform.position) <= maxUseDistance;
        }

        /// <summary>Runs on the server only, after the request has been validated.</summary>
        protected abstract void OnInteractServer(ulong clientId);

        // Shared helper: tint a renderer without creating material copies.
        static MaterialPropertyBlock s_Block;
        protected static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            s_Block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(s_Block);
            s_Block.SetColor("_Color", c);      // Built-in Standard
            s_Block.SetColor("_BaseColor", c);  // URP Lit (if the project switches later)
            r.SetPropertyBlock(s_Block);
        }
    }
}
