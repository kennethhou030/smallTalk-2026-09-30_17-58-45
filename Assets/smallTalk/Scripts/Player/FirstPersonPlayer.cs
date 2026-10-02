using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>
    /// First-person controller for the capsule player.
    /// The owning client moves itself (owner-authoritative NetworkTransform syncs it to others).
    /// Controls: WASD move, Shift sprint, Space jump, mouse look, E interact,
    ///           Esc frees the mouse, left click re-locks it.
    /// Uses the legacy Input Manager (the project's current input setting).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonPlayer : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraRoot;
        [Tooltip("Renderers the local player should not see (own body) — they still cast shadows.")]
        [SerializeField] Renderer[] hideForOwner;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.5f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;

        [Header("Look & interact")]
        [SerializeField] float mouseSensitivity = 2f;
        [SerializeField] float interactRange = 3f;

        public static FirstPersonPlayer Local { get; private set; }
        public static readonly List<FirstPersonPlayer> All = new List<FirstPersonPlayer>();

        CharacterController m_Controller;
        Camera m_Camera;
        float m_Pitch;
        float m_VerticalVelocity;
        Interactable m_Focused;
        static readonly List<GameObject> s_DisabledSceneCameras = new List<GameObject>();

        public Interactable Focused => m_Focused;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            if (cameraRoot) m_Camera = cameraRoot.GetComponentInChildren<Camera>(true);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            bool mine = IsOwner;
            if (m_Camera)
            {
                m_Camera.enabled = mine;
                var listener = m_Camera.GetComponent<AudioListener>();
                if (listener) listener.enabled = mine;
            }
            if (!mine) return;

            Local = this;
            foreach (var r in hideForOwner)
                if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            // Turn off the scene's overview camera while we are playing.
            foreach (var cam in Camera.allCameras)
            {
                if (cam == m_Camera || cam.GetComponentInParent<FirstPersonPlayer>() != null) continue;
                s_DisabledSceneCameras.Add(cam.gameObject);
                cam.gameObject.SetActive(false);
            }

            if (SpawnPoints.TryGet(OwnerClientId, out var pos, out var rot))
                Teleport(pos, rot);

            SetCursorLocked(true);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (!IsOwner) return;
            Local = null;
            SetCursorLocked(false);
            foreach (var go in s_DisabledSceneCameras)
                if (go) go.SetActive(true);
            s_DisabledSceneCameras.Clear();
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            m_Controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            m_Controller.enabled = true;
            m_VerticalVelocity = 0f;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked) Look();
            Move(locked);
            UpdateFocus();

            if (locked && m_Focused != null && Input.GetKeyDown(KeyCode.E))
                m_Focused.RequestInteract();
        }

        void Look()
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, mx, 0f);
            m_Pitch = Mathf.Clamp(m_Pitch - my, -85f, 85f);
            if (cameraRoot) cameraRoot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        void Move(bool acceptInput)
        {
            Vector3 input = Vector3.zero;
            bool sprint = false, jump = false;
            if (acceptInput)
            {
                input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
                sprint = Input.GetKey(KeyCode.LeftShift);
                jump = Input.GetButtonDown("Jump");
            }
            input = Vector3.ClampMagnitude(input, 1f);
            Vector3 move = (transform.right * input.x + transform.forward * input.z) * (sprint ? sprintSpeed : walkSpeed);

            if (m_Controller.isGrounded)
            {
                if (m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
                if (jump) m_VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            m_VerticalVelocity += gravity * Time.deltaTime;
            move.y = m_VerticalVelocity;
            m_Controller.Move(move * Time.deltaTime);

            // Safety net: fell out of the level -> back to spawn.
            if (transform.position.y < -20f && SpawnPoints.TryGet(OwnerClientId, out var p, out var r))
                Teleport(p, r);
        }

        void UpdateFocus()
        {
            m_Focused = null;
            if (!m_Camera) return;
            var t = m_Camera.transform;
            if (Physics.Raycast(t.position, t.forward, out var hit, interactRange, ~0, QueryTriggerInteraction.Ignore))
            {
                var target = hit.collider.GetComponentInParent<Interactable>();
                if (target != null && target.IsSpawned && target.CanInteract) m_Focused = target;
            }
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnGUI()
        {
            if (!IsSpawned || !IsOwner) return;
            float s = Screen.height / 720f;
            var cx = Screen.width / 2f; var cy = Screen.height / 2f;
            GUI.Label(new Rect(cx - 5 * s, cy - 10 * s, 20 * s, 20 * s), "+", Styles.Center(s, 18));
            if (m_Focused != null)
                GUI.Label(new Rect(cx - 300 * s, cy + 18 * s, 600 * s, 30 * s), $"[E] {m_Focused.Prompt}", Styles.Center(s, 18));
            if (Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(cx - 300 * s, cy - 60 * s, 600 * s, 30 * s), "Click to control", Styles.Center(s, 18));
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform cameraRoot, Renderer[] hideForOwner)
        {
            this.cameraRoot = cameraRoot; this.hideForOwner = hideForOwner;
        }
#endif
    }

    /// <summary>Tiny IMGUI style cache shared by the prototype UIs.</summary>
    internal static class Styles
    {
        static GUIStyle s_Center;
        public static GUIStyle Center(float scale, int size)
        {
            s_Center ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            s_Center.fontSize = Mathf.RoundToInt(size * scale);
            s_Center.normal.textColor = Color.white;
            return s_Center;
        }
    }
}
