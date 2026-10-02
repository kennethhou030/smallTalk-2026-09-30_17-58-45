using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTalk.EditorTools
{
    /// <summary>
    /// Menu: SmallTalk > Build Test Room.
    /// Re-creates the capsule player prefab and the grey-box test room inside Assets/smallTalk.unity.
    /// Safe to run again at any time: it only replaces objects it created
    /// ("SmallTalk_TestRoom" root + "NetworkManager").
    /// </summary>
    public static class SmallTalkBuilder
    {
        const string Root = "Assets/smallTalk";
        const string ScenePath = "Assets/smallTalk.unity";
        const string PrefabDir = Root + "/Prefabs";
        const string MatDir = Root + "/Materials";
        const string PlayerPrefabPath = PrefabDir + "/Player.prefab";
        const string LevelRootName = "SmallTalk_TestRoom";

        const float WallH = 3f, WallT = 0.2f, DoorW = 2f, DoorH = 2.4f;

        // ------------------------------------------------------------------ entry point
        [MenuItem("SmallTalk/Build Test Room", priority = 0)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SmallTalk", "Exit Play Mode first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(PrefabDir);
            EnsureFolder(MatDir);

            var player = BuildPlayerPrefab();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemovePrevious(scene);
            BuildNetworkManager(player);
            BuildLevel();
            EnsureLightAndOverviewCamera(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RefreshNetworkObjectIds(scene);   // in-scene NetworkObjects need ids generated after the first save
            EditorSceneManager.SaveScene(scene);
            AddSceneToBuildSettings();

            Debug.Log("[SmallTalk] Test room built. Press Play → Host. For more players: Window > Multiplayer > Multiplayer Play Mode.");
        }

        // ------------------------------------------------------------------ materials
        static Material Mat(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material M_Floor => Mat("Floor", new Color(0.55f, 0.57f, 0.6f));
        static Material M_Wall => Mat("Wall", new Color(0.82f, 0.82f, 0.8f));
        static Material M_Door => Mat("Door", new Color(0.45f, 0.3f, 0.2f));
        static Material M_Metal => Mat("Metal", new Color(0.25f, 0.27f, 0.3f));
        static Material M_Lamp => Mat("Lamp", Color.white);
        static Material M_Player => Mat("Player", Color.white);
        static Material M_Visor => Mat("Visor", new Color(0.08f, 0.08f, 0.1f));
        static Material M_Goal => Mat("Goal", new Color(0.2f, 0.85f, 0.35f));
        static Material M_Spawn => Mat("Spawn", new Color(0.3f, 0.5f, 0.9f));

        // ------------------------------------------------------------------ player prefab
        static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f; cc.skinWidth = 0.04f;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // each player moves itself
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), M_Player, false);
            var visor = Prim(PrimitiveType.Cube, "Visor (front)", root.transform, new Vector3(0f, 1.5f, 0.3f), new Vector3(0.45f, 0.14f, 0.15f), M_Visor, false);

            var camRoot = new GameObject("CameraRoot");
            camRoot.transform.SetParent(root.transform, false);
            camRoot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camRoot.tag = "MainCamera";
            var cam = camRoot.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 75f; cam.enabled = false;  // enabled only for the owner
            camRoot.AddComponent<AudioListener>().enabled = false;

            var label = new GameObject("NameLabel");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            var tm = TextLabel(label, "P?", 0.035f);
            label.AddComponent<Billboard>();

            root.AddComponent<FirstPersonPlayer>().EditorSetup(camRoot.transform,
                new[] { body.GetComponent<Renderer>(), visor.GetComponent<Renderer>() });
            root.AddComponent<PlayerIdentity>().EditorSetup(new[] { body.GetComponent<Renderer>() }, tm);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ network manager
        static void BuildNetworkManager(GameObject playerPrefab)
        {
            var go = new GameObject("NetworkManager");
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;   // auto-registered by Netcode
            nm.NetworkConfig.ConnectionApproval = true;
            go.AddComponent<SmallTalkNetworkUI>();
            EditorUtility.SetDirty(nm);
        }

        // ------------------------------------------------------------------ level
        // Layout (top view, metres). Spawn room is south; a wall at z=0 has two doorways.
        //
        //   z=10 +-----------------+-----------------+
        //        |  WEST ROOM      |  GOAL ROOM      |
        //        |  [Switch 2B]    |    (green pad)  |
        //   z=0  +--[Door A]-------+-------[Door B]--+
        //        |  [Switch 1]                       |
        //        |        spawn       [Switch 2A]    |
        //   z=-10+-----------------------------------+
        //       x=-10                               x=10
        //
        // Door A: one Toggle switch (basic).  Door B: two Timed switches in different rooms,
        // both must be ON together (3 s window) → players have to talk and count down.
        static void BuildLevel()
        {
            var level = new GameObject(LevelRootName).transform;

            // Floor + outer walls
            Box("Floor", level, new Vector3(0f, -0.05f, 0f), new Vector3(20f, 0.1f, 20f), M_Floor);
            var walls = new GameObject("Walls").transform; walls.SetParent(level, false);
            Wall(walls, "South", new Vector3(0f, 0f, -10f), new Vector3(20f + WallT, WallH, WallT));
            Wall(walls, "North", new Vector3(0f, 0f, 10f), new Vector3(20f + WallT, WallH, WallT));
            Wall(walls, "West", new Vector3(-10f, 0f, 0f), new Vector3(WallT, WallH, 20f + WallT));
            Wall(walls, "East", new Vector3(10f, 0f, 0f), new Vector3(WallT, WallH, 20f + WallT));
            // Middle wall z=0 with doorways centred at x=-5 and x=+5
            Wall(walls, "Mid_W", new Vector3(-8f, 0f, 0f), new Vector3(4f, WallH, WallT));
            Wall(walls, "Mid_C", new Vector3(0f, 0f, 0f), new Vector3(8f, WallH, WallT));
            Wall(walls, "Mid_E", new Vector3(8f, 0f, 0f), new Vector3(4f, WallH, WallT));
            Box("Lintel_A", walls, new Vector3(-5f, (DoorH + WallH) / 2f, 0f), new Vector3(DoorW, WallH - DoorH, WallT), M_Wall);
            Box("Lintel_B", walls, new Vector3(5f, (DoorH + WallH) / 2f, 0f), new Vector3(DoorW, WallH - DoorH, WallT), M_Wall);
            // Divider between west room and goal room
            Wall(walls, "Divider", new Vector3(0f, 0f, 5f), new Vector3(WallT, WallH, 10f));

            // Spawn points P1..P4
            var spawnRoot = new GameObject("SpawnPoints"); spawnRoot.transform.SetParent(level, false);
            var spawnPositions = new[] { new Vector3(-1.5f, 0.05f, -6f), new Vector3(1.5f, 0.05f, -6f), new Vector3(-1.5f, 0.05f, -8f), new Vector3(1.5f, 0.05f, -8f) };
            var points = new Transform[spawnPositions.Length];
            for (int i = 0; i < spawnPositions.Length; i++)
            {
                var p = new GameObject($"Spawn_P{i + 1}").transform;
                p.SetParent(spawnRoot.transform, false);
                p.localPosition = spawnPositions[i];
                p.localRotation = Quaternion.identity; // facing +Z (towards the doors)
                Box("Marker", p, new Vector3(0f, -0.04f, 0f), new Vector3(0.8f, 0.02f, 0.8f), M_Spawn, false);
                points[i] = p;
            }
            spawnRoot.AddComponent<SpawnPoints>().EditorSetup(points);

            // Puzzle 1: Door A + one Toggle switch
            var s1 = CreateSwitch(level, "Switch 1", new Vector3(-7.5f, 0f, -2f), 180f, NetworkSwitch.Mode.Toggle, 0f);
            CreateDoor(level, "Door A", new Vector3(-5f, 0f, 0f), new List<NetworkSwitch> { s1 }, NetworkDoor.Logic.All, false);

            // Puzzle 2: Door B + two Timed switches in different rooms
            var s2a = CreateSwitch(level, "Switch 2A", new Vector3(8f, 0f, -8f), -90f, NetworkSwitch.Mode.Timed, 3f);
            var s2b = CreateSwitch(level, "Switch 2B", new Vector3(-8f, 0f, 8f), 135f, NetworkSwitch.Mode.Timed, 3f);
            CreateDoor(level, "Door B", new Vector3(5f, 0f, 0f), new List<NetworkSwitch> { s2a, s2b }, NetworkDoor.Logic.All, true);

            // Goal
            Box("GoalPad", level, new Vector3(5f, 0.01f, 6f), new Vector3(3f, 0.02f, 3f), M_Goal, false);
            Sign(level, "GOAL", new Vector3(5f, 2.2f, 6f), 0.08f, new Color(0.3f, 1f, 0.4f));
            Sign(level, "Door B: pull 2A + 2B within 3 s", new Vector3(5f, 3.35f, -0.15f), 0.025f, Color.white, false);
        }

        static NetworkSwitch CreateSwitch(Transform parent, string label, Vector3 pos, float yaw, NetworkSwitch.Mode mode, float seconds)
        {
            var root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Box("Pedestal", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 1f, 0.6f), M_Metal);
            var pivot = new GameObject("LeverPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.0f, 0f);
            Box("Handle", pivot, new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.08f), M_Metal);
            Prim(PrimitiveType.Sphere, "Knob", pivot, new Vector3(0f, 0.62f, 0f), Vector3.one * 0.16f, M_Door, true);
            var lamp = Prim(PrimitiveType.Sphere, "Lamp", root.transform, new Vector3(0f, 0.8f, 0.31f), Vector3.one * 0.14f, M_Lamp, false);
            string sub = mode == NetworkSwitch.Mode.Toggle ? "toggle" : $"stays on {seconds:0.#}s";
            Sign(root.transform, $"{label}\n({sub})", new Vector3(0f, 2.0f, 0f), 0.03f, Color.white);

            root.AddComponent<NetworkObject>();
            var sw = root.AddComponent<NetworkSwitch>();
            sw.EditorSetup(label, mode, Mathf.Max(0.1f, seconds), pivot, lamp.GetComponent<Renderer>());
            return sw;
        }

        static NetworkDoor CreateDoor(Transform parent, string label, Vector3 pos, List<NetworkSwitch> switches, NetworkDoor.Logic logic, bool latch)
        {
            var root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            var panel = Box("Panel", root.transform, new Vector3(0f, DoorH / 2f, 0f), new Vector3(DoorW - 0.04f, DoorH - 0.02f, 0.12f), M_Door);
            var lamp = Prim(PrimitiveType.Sphere, "Lamp", root.transform, new Vector3(0.75f, DoorH + 0.3f, 0f), Vector3.one * 0.3f, M_Lamp, false);
            Sign(root.transform, label, new Vector3(0f, DoorH + 0.3f, -0.15f), 0.03f, Color.white, false);

            root.AddComponent<NetworkObject>();
            var door = root.AddComponent<NetworkDoor>();
            door.EditorSetup(label, switches, logic, latch, false, panel.transform, lamp.GetComponent<Renderer>());
            return door;
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
            => Prim(PrimitiveType.Cube, name, parent, localPos, size, mat, collider);

        static void Wall(Transform parent, string name, Vector3 basePos, Vector3 size)
            => Box(name, parent, basePos + new Vector3(0f, size.y / 2f, 0f), size, M_Wall);

        static TextMesh TextLabel(GameObject go, string text, float charSize)
        {
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        static void Sign(Transform parent, string text, Vector3 localPos, float charSize, Color color, bool billboard = true)
        {
            var go = new GameObject("Sign");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Non-billboard signs keep identity rotation = readable from the -Z side (the spawn room).
            var tm = TextLabel(go, text, charSize);
            tm.color = color;
            if (billboard) go.AddComponent<Billboard>();
        }

        static void RemovePrevious(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == LevelRootName || go.GetComponent<NetworkManager>() != null)
                    Object.DestroyImmediate(go);
            }
        }

        static void EnsureLightAndOverviewCamera(Scene scene)
        {
            bool hasLight = false;
            Camera overview = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Directional) hasLight = true;
                if (overview == null) overview = go.GetComponentInChildren<Camera>(true);
            }
            if (!hasLight)
            {
                var lgo = new GameObject("Directional Light");
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Directional; l.shadows = LightShadows.Soft;
                lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            if (overview == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                overview = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            // Overview shot shown before you connect (disabled automatically once you spawn).
            overview.transform.SetPositionAndRotation(new Vector3(0f, 18f, -16f), Quaternion.Euler(52f, 0f, 0f));
        }

        static void RefreshNetworkObjectIds(Scene scene)
        {
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var go in scene.GetRootGameObjects())
                foreach (var no in go.GetComponentsInChildren<NetworkObject>(true))
                {
                    onValidate?.Invoke(no, null);
                    EditorUtility.SetDirty(no);
                }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
