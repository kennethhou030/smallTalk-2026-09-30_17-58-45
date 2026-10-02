using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>
    /// Reusable door. Open/closed state lives on the server and is synced to everyone.
    /// Ways to drive it:
    ///  1. Link one or more <see cref="NetworkSwitch"/>es (All / Any logic).
    ///  2. Manual: no switches + manualControl = players press E on the door.
    ///  3. From another script (e.g. a keypad): call <see cref="ServerSetOpen"/> on the server.
    /// The door panel slides by <see cref="openOffset"/>; its collider moves with it.
    /// </summary>
    public class NetworkDoor : Interactable
    {
        public enum Logic { All, Any }

        [Header("Behaviour")]
        [SerializeField] string label = "Door";
        [SerializeField] List<NetworkSwitch> switches = new List<NetworkSwitch>();
        [SerializeField] Logic logic = Logic.All;
        [Tooltip("Once opened, stay open and lock the linked switches (puzzle solved).")]
        [SerializeField] bool latchOpen;
        [Tooltip("Allow pressing E on the door itself (only used when no switches are linked).")]
        [SerializeField] bool manualControl;

        [Header("Visuals")]
        [SerializeField] Transform panel;
        [SerializeField] Vector3 openOffset = new Vector3(0f, 2.5f, 0f);
        [SerializeField] float speed = 3f;
        [SerializeField] Renderer lamp;
        [SerializeField] Color closedColor = new Color(0.75f, 0.12f, 0.1f);
        [SerializeField] Color openColor = new Color(0.15f, 0.9f, 0.25f);

        NetworkVariable<bool> m_IsOpen = new NetworkVariable<bool>(false);
        Vector3 m_ClosedLocalPos;

        public bool IsOpen => m_IsOpen.Value;
        public string Label => label;

        /// <summary>Raised on server AND clients whenever the door opens/closes.</summary>
        public event Action<NetworkDoor, bool> Changed;

        public override bool CanInteract => manualControl && switches.Count == 0 && !(latchOpen && IsOpen);
        public override string Prompt => IsOpen ? $"Close {label}" : $"Open {label}";

        void Awake()
        {
            if (panel) m_ClosedLocalPos = panel.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            m_IsOpen.OnValueChanged += OnIsOpenChanged;
            if (panel) panel.localPosition = TargetPos(); // late joiners snap to the current state

            if (IsServer)
            {
                foreach (var s in switches)
                    if (s) s.Changed += OnSwitchChanged;
                Evaluate();
            }
        }

        public override void OnNetworkDespawn()
        {
            m_IsOpen.OnValueChanged -= OnIsOpenChanged;
            foreach (var s in switches)
                if (s) s.Changed -= OnSwitchChanged;
        }

        void OnIsOpenChanged(bool previous, bool current) => Changed?.Invoke(this, current);

        void OnSwitchChanged(NetworkSwitch s, bool on)
        {
            if (IsServer) Evaluate();
        }

        void Evaluate()
        {
            if (switches.Count == 0) return;
            if (latchOpen && m_IsOpen.Value) return;

            bool all = true, any = false;
            foreach (var s in switches)
            {
                bool on = s && s.IsOn;
                all &= on;
                any |= on;
            }
            bool open = logic == Logic.All ? all : any;
            m_IsOpen.Value = open;

            if (open && latchOpen)
                foreach (var s in switches)
                    if (s) { s.ServerSetOn(true); s.ServerSetLocked(true); }
        }

        protected override void OnInteractServer(ulong clientId) => m_IsOpen.Value = !m_IsOpen.Value;

        /// <summary>Open/close from another puzzle script (server only).</summary>
        public void ServerSetOpen(bool open)
        {
            if (IsServer) m_IsOpen.Value = open;
        }

        void Update()
        {
            if (panel)
                panel.localPosition = Vector3.MoveTowards(panel.localPosition, TargetPos(), speed * Time.deltaTime);
            Tint(lamp, m_IsOpen.Value ? openColor : closedColor);
        }

        Vector3 TargetPos() => m_ClosedLocalPos + (m_IsOpen.Value ? openOffset : Vector3.zero);

#if UNITY_EDITOR
        public void EditorSetup(string label, List<NetworkSwitch> switches, Logic logic, bool latchOpen,
                                bool manualControl, Transform panel, Renderer lamp)
        {
            this.label = label; this.switches = switches; this.logic = logic; this.latchOpen = latchOpen;
            this.manualControl = manualControl; this.panel = panel; this.lamp = lamp;
        }
#endif
    }
}
