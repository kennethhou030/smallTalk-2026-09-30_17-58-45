using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SmallTalk
{
    /// <summary>
    /// Temporary connect screen (IMGUI) for the prototype.
    /// Host = server + player. Others type the host's IP and Join.
    /// Direct IP works on the same machine / same Wi-Fi. Over the internet we will swap this
    /// for Unity Relay join codes (next step) — the rest of the game does not change.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SmallTalkNetworkUI : MonoBehaviour
    {
        [SerializeField] string address = "127.0.0.1";
        [SerializeField] ushort port = 7777;
        [SerializeField] int maxPlayers = 4;

        NetworkManager m_Net;
        string m_PortText;
        string m_Status = "";
        List<string> m_LocalIps;

        void Awake()
        {
            m_Net = GetComponent<NetworkManager>();
            m_PortText = port.ToString();
        }

        void OnEnable()
        {
            m_Net = GetComponent<NetworkManager>();
            m_Net.OnClientDisconnectCallback += OnDisconnect;
        }

        void OnDisable()
        {
            if (!m_Net) return;
            m_Net.OnClientDisconnectCallback -= OnDisconnect;
        }

        // Server: refuse a 5th player.
        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            bool full = m_Net.ConnectedClientsIds.Count >= maxPlayers;
            res.Approved = !full;
            res.CreatePlayerObject = !full;
            res.Reason = full ? "Room is full (4 players)." : null;
        }

        void OnDisconnect(ulong clientId)
        {
            if (!m_Net.IsServer && clientId == m_Net.LocalClientId)
                m_Status = string.IsNullOrEmpty(m_Net.DisconnectReason) ? "Disconnected." : m_Net.DisconnectReason;
        }

        void StartHost()
        {
            if (!ApplyTransport("127.0.0.1", "0.0.0.0")) return;
            m_Net.NetworkConfig.ConnectionApproval = true;
            m_Net.ConnectionApprovalCallback = Approve; // only the server runs approval
            m_Status = m_Net.StartHost() ? "" : "Could not start host (port in use?).";
        }

        void StartClient()
        {
            if (!ApplyTransport(address.Trim(), null)) return;
            m_Net.NetworkConfig.ConnectionApproval = true;
            m_Status = m_Net.StartClient() ? "Connecting..." : "Could not start client.";
        }

        bool ApplyTransport(string ip, string listen)
        {
            if (!ushort.TryParse(m_PortText, out port)) { m_Status = "Port must be a number."; return false; }
            var utp = m_Net.GetComponent<UnityTransport>();
            utp.SetConnectionData(ip, port, listen);
            return true;
        }

        void OnGUI()
        {
            float s = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUILayout.BeginArea(new Rect(12, 12, 320, 400), GUI.skin.box);

            if (!m_Net.IsClient && !m_Net.IsServer)
            {
                GUILayout.Label("SMALL TALK — prototype");
                if (GUILayout.Button("Host (server + player)", GUILayout.Height(32))) StartHost();
                GUILayout.Space(8);
                GUILayout.Label("Host IP");
                address = GUILayout.TextField(address);
                GUILayout.Label("Port");
                m_PortText = GUILayout.TextField(m_PortText);
                if (GUILayout.Button("Join", GUILayout.Height(32))) StartClient();
                if (!string.IsNullOrEmpty(m_Status)) GUILayout.Label(m_Status);
            }
            else
            {
                string mode = m_Net.IsHost ? "Host" : m_Net.IsServer ? "Server" : "Client";
                GUILayout.Label($"{mode}  ·  players: {FirstPersonPlayer.All.Count}/{maxPlayers}");
                if (m_Net.IsHost)
                {
                    GUILayout.Label("Friends on the same Wi-Fi join with:");
                    m_LocalIps ??= GetLocalIPv4();
                    foreach (var ip in m_LocalIps) GUILayout.Label($"  {ip} : {port}");
                }
                else if (!m_Net.IsConnectedClient)
                {
                    GUILayout.Label(m_Status);
                }
                GUILayout.Label("WASD · Shift · Space · E use · Esc mouse");
                if (GUILayout.Button("Leave")) { m_Net.Shutdown(); m_Status = ""; }
            }

            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        static List<string> GetLocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
                }
            }
            catch { /* not available on every platform */ }
            if (list.Count == 0) list.Add("(could not read local IP)");
            return list;
        }
    }
}
