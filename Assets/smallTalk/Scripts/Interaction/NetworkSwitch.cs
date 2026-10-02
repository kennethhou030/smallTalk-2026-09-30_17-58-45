using System;
using Unity.Netcode;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>
    /// Reusable lever / switch. State lives on the server and is synced to everyone.
    ///  - Toggle: each use flips ON/OFF.
    ///  - Timed:  a use turns it ON, it snaps back OFF after <see cref="onDuration"/> seconds.
    ///            Several Timed switches on one door = players must act together.
    /// Doors (or any other script) listen to <see cref="Changed"/>.
    /// </summary>
    public class NetworkSwitch : Interactable
    {
        public enum Mode { Toggle, Timed }

        [Header("Behaviour")]
        [SerializeField] string label = "Switch";
        [SerializeField] Mode mode = Mode.Toggle;
        [Tooltip("Timed mode only: seconds the switch stays ON.")]
        [SerializeField, Min(0.1f)] float onDuration = 3f;
        [Tooltip("Start locked (cannot be used until something unlocks it).")]
        [SerializeField] bool startLocked;

        [Header("Visuals (optional)")]
        [SerializeField] Transform lever;
        [SerializeField] float leverOffAngle = -35f;
        [SerializeField] float leverOnAngle = 35f;
        [SerializeField] float leverSpeed = 360f;
        [SerializeField] Renderer lamp;
        [SerializeField] Color offColor = new Color(0.75f, 0.12f, 0.1f);
        [SerializeField] Color onColor = new Color(0.15f, 0.9f, 0.25f);
        [SerializeField] Color lockedColor = new Color(0.25f, 0.25f, 0.25f);

        NetworkVariable<bool> m_IsOn = new NetworkVariable<bool>(false);
        NetworkVariable<bool> m_IsLocked = new NetworkVariable<bool>(false);
        float m_ServerOffAt;

        public bool IsOn => m_IsOn.Value;
        public bool IsLocked => m_IsLocked.Value;
        public string Label => label;

        /// <summary>Raised on server AND clients whenever the switch turns on/off.</summary>
        public event Action<NetworkSwitch, bool> Changed;

        public override bool CanInteract => !m_IsLocked.Value;

        public override string Prompt =>
            mode == Mode.Toggle
                ? $"{label}: turn {(IsOn ? "OFF" : "ON")}"
                : $"{label}: pull (stays on {onDuration:0.#}s)";

        public override void OnNetworkSpawn()
        {
            if (IsServer) m_IsLocked.Value = startLocked;
            m_IsOn.OnValueChanged += OnIsOnChanged;
            if (lever) lever.localRotation = Quaternion.Euler(TargetAngle(), 0f, 0f);
        }

        public override void OnNetworkDespawn()
        {
            m_IsOn.OnValueChanged -= OnIsOnChanged;
        }

        void OnIsOnChanged(bool previous, bool current) => Changed?.Invoke(this, current);

        protected override void OnInteractServer(ulong clientId)
        {
            if (mode == Mode.Toggle)
            {
                m_IsOn.Value = !m_IsOn.Value;
            }
            else
            {
                m_ServerOffAt = Time.time + onDuration; // re-pulling refreshes the timer
                m_IsOn.Value = true;
            }
        }

        // ---------- Server API for puzzle scripts ----------
        public void ServerSetOn(bool on)
        {
            if (!IsServer) return;
            if (on && mode == Mode.Timed) m_ServerOffAt = Time.time + onDuration;
            m_IsOn.Value = on;
        }

        public void ServerSetLocked(bool locked)
        {
            if (IsServer) m_IsLocked.Value = locked;
        }

        void Update()
        {
            if (IsServer && mode == Mode.Timed && m_IsOn.Value && !m_IsLocked.Value && Time.time >= m_ServerOffAt)
                m_IsOn.Value = false;

            if (lever)
            {
                var target = Quaternion.Euler(TargetAngle(), 0f, 0f);
                lever.localRotation = Quaternion.RotateTowards(lever.localRotation, target, leverSpeed * Time.deltaTime);
            }
            Tint(lamp, m_IsLocked.Value && !m_IsOn.Value ? lockedColor : (m_IsOn.Value ? onColor : offColor));
        }

        float TargetAngle() => m_IsOn.Value ? leverOnAngle : leverOffAngle;

#if UNITY_EDITOR
        /// <summary>Used by the SmallTalk builder menu to configure the component from code.</summary>
        public void EditorSetup(string label, Mode mode, float onDuration, Transform lever, Renderer lamp)
        {
            this.label = label; this.mode = mode; this.onDuration = onDuration; this.lever = lever; this.lamp = lamp;
        }
#endif
    }
}
