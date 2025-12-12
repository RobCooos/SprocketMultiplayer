using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppNewtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEngine;
using MelonLoader;

namespace SprocketMultiplayer.Core {
    public static class BlueprintSpawner
    {
        private static bool isInitialized = false;
        private static Dictionary<string, GameObject> tankPrefabs = new Dictionary<string, GameObject>();
        private static List<string> tankIdList = new List<string>();
        private static string defaultTankId = null;

        private static readonly string tankFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Sprocket", "Factions", "AllowedVehicles", "Blueprints", "Vehicles"
        );

        public static void InitializeFromDatabase()
        {
            if (isInitialized)
            {
                MelonLogger.Msg("[BlueprintSpawner] Already initialized.");
                return;
            }

            MelonLogger.Msg("[BlueprintSpawner] ===== INITIALIZING FROM TANKDATABASE =====");

            var tanks = TankDatabase.LoadTanks();
            if (tanks == null || tanks.Count == 0)
            {
                MelonLogger.Warning("[BlueprintSpawner] TankDatabase returned no tanks. Falling back to direct scan.");
                Initialize();
                return;
            }

            MelonLogger.Msg($"[BlueprintSpawner] Found {tanks.Count} tanks in database.");

            int successCount = 0;

            foreach (var tankInfo in tanks)
            {
                if (tankInfo == null || string.IsNullOrEmpty(tankInfo.Name)) continue;

                try
                {
                    string jsonText = File.ReadAllText(tankInfo.BlueprintPath);
                    GameObject prefab = CreateTankPrefabFromJson(jsonText, tankInfo.Name);
                    
                    if (prefab != null)
                    {
                        RegisterTankPrefab(tankInfo.Name, prefab);
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[BlueprintSpawner] Failed to load '{tankInfo.Name}': {ex.Message}");
                }
            }

            isInitialized = true;
            MelonLogger.Msg($"[BlueprintSpawner] ✓ Loaded {successCount}/{tanks.Count} tanks from database. Default: {defaultTankId ?? "None"}");
            MelonLogger.Msg("[BlueprintSpawner] ===== INITIALIZATION COMPLETE =====");
        }

        public static void EnsureInitialized()
        {
            if (isInitialized) return;
            
            try
            {
                InitializeFromDatabase();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BlueprintSpawner] Database init failed: {ex.Message}");
                Initialize();
            }
        }

        public static void Initialize()
        {
            if (isInitialized)
            {
                MelonLogger.Msg("[BlueprintSpawner] Already initialized.");
                return;
            }

            MelonLogger.Msg("[BlueprintSpawner] ===== INITIALIZING BLUEPRINT SPAWNER =====");

            if (!Directory.Exists(tankFolder))
            {
                MelonLogger.Warning($"[BlueprintSpawner] Tank folder not found: {tankFolder}");
                isInitialized = true;
                return;
            }

            var files = Directory.GetFiles(tankFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".json") || f.EndsWith(".blueprint"))
                .ToArray();

            MelonLogger.Msg($"[BlueprintSpawner] Found {files.Length} blueprint files.");

            int successCount = 0;

            foreach (var file in files)
            {
                string tankName = Path.GetFileNameWithoutExtension(file);

                try
                {
                    string jsonText = File.ReadAllText(file);
                    GameObject prefab = CreateTankPrefabFromJson(jsonText, tankName);
                    
                    if (prefab != null)
                    {
                        RegisterTankPrefab(tankName, prefab);
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[BlueprintSpawner] Failed to load '{tankName}': {ex.Message}");
                }
            }

            isInitialized = true;
            MelonLogger.Msg($"[BlueprintSpawner] ✓ Loaded {successCount}/{files.Length} tanks. Default: {defaultTankId ?? "None"}");
            MelonLogger.Msg("[BlueprintSpawner] ===== INITIALIZATION COMPLETE =====");
        }
        
        
        private static GameObject CreateTankPrefabFromJson(string json, string tankName) {
            try {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var header = root["header"];
                
                if (header == null) {
                    MelonLogger.Warning($"[BlueprintSpawner] No header in: {tankName}");
                    return null;
                }
                
                var il2cppHeader = header.ToObject<JToken>();
                string name = GetString(il2cppHeader, "name", tankName);
                float mass = GetFloat(il2cppHeader, "mass", 15000f);

                var tankObj = new GameObject(name);
                tankObj.SetActive(false);
    
                var rb = tankObj.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.drag = 0f;
                rb.angularDrag = 0.5f;

                GameObject hullObj = null;
                GameObject turretObj = null;

                var blueprints = root["blueprints"];
                if (blueprints == null)
                {
                    MelonLogger.Warning($"[BlueprintSpawner] No blueprints array in: {tankName}");
                    UnityEngine.Object.Destroy(tankObj);
                    return null;
                }
                
                int blueprintCount = 0;
                try
                {
                    blueprintCount = blueprints.Count();
                }
                catch
                {
                    MelonLogger.Warning($"[BlueprintSpawner] Cannot read blueprints in: {tankName}, skipping");
                    UnityEngine.Object.Destroy(tankObj);
                    return null;
                }

                for (int i = 0; i < blueprintCount; i++)
                {
                    var entry = blueprints[i];
                    if (entry == null) continue;

                    string type = "";
                    try
                    {
                        var typeToken = entry["type"];
                        if (typeToken != null)
                            type = typeToken.ToString();
                    }
                    catch { continue; }

                    if (string.IsNullOrEmpty(type)) continue;

                    var bp = entry["blueprint"];
                    if (bp == null) continue;

                    // Pass regular Newtonsoft token to Create methods
                    switch (type) {
                        case "chassis":
                            hullObj = CreateChassis(tankObj.transform, bp);  // bp is regular Newtonsoft
                            break;

                        case "structure":
                            CreateStructure(tankObj.transform, bp);
                            break;

                        case "turret":
                            turretObj = CreateTurret(tankObj.transform, bp);
                            break;

                        case "cannon":
                            if (turretObj != null)
                            {
                                // Cannon still needs Il2Cpp for GetFloat
                                var il2cppBp = bp.ToObject<JToken>();
                                CreateCannon(turretObj.transform, il2cppBp);
                            }
                            break;
                    }
                }

                // Ensure collider exists
                if (tankObj.GetComponent<Collider>() == null)
                {
                    var collider = tankObj.AddComponent<BoxCollider>();
                    if (hullObj != null && hullObj.GetComponent<Renderer>() != null)
                    {
                        collider.size = hullObj.GetComponent<Renderer>().bounds.size;
                    }
                    else
                    {
                        collider.size = new Vector3(2f, 1.5f, 6f);
                    }
                }

                return tankObj;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[BlueprintSpawner] Failed to parse '{tankName}': {ex}");
                return null;
            }
        }

        // === HELPER METHODS ===

        private static float GetFloat(JToken token, string key, float defaultValue)
        {
            try
            {
                var obj = token as JObject;
                if (obj == null) return defaultValue;

                var value = obj[key];
                if (value == null) return defaultValue;

                return Convert.ToSingle(value.ToString());
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string GetString(JToken token, string key, string defaultValue)
        {
            try
            {
                var obj = token as JObject;
                if (obj == null) return defaultValue;

                var value = obj[key];
                if (value == null) return defaultValue;
                return value.ToString();
            }
            catch
            {
                return defaultValue;
            }
        }

        private static GameObject CreateChassis(Transform parent, Newtonsoft.Json.Linq.JToken bp) {
            var chassis = new GameObject("Chassis");
            chassis.transform.SetParent(parent);

            // Get position using direct access
            float x = bp.Value<float?>("x") ?? 0f;
            float y = bp.Value<float?>("y") ?? 0f;
            float z = bp.Value<float?>("z") ?? 0f;
            chassis.transform.localPosition = new Vector3(x, y, z) / 1000f;

            // Try to build mesh from armour data
            var mesh = BuildMeshFromArmour(bp);
            if (mesh != null)
            {
                var meshFilter = chassis.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                
                var renderer = chassis.AddComponent<MeshRenderer>();
                renderer.material = CreateDefaultMaterial(new Color(0.3f, 0.3f, 0.3f));
            }
            else
            {
                // Fallback to primitive if no mesh data
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                primitive.transform.SetParent(chassis.transform);
                primitive.transform.localPosition = Vector3.zero;
                
                float length = bp.Value<float?>("length") ?? 6000f;
                float width = bp.Value<float?>("width") ?? 2000f;
                float height = bp.Value<float?>("groundClearance") ?? 500f;
                primitive.transform.localScale = new Vector3(width, height, length) / 1000f;
                
                UnityEngine.Object.DestroyImmediate(primitive.GetComponent<Collider>());
            }

            return chassis;
        }

        private static void CreateStructure(Transform parent, Newtonsoft.Json.Linq.JToken bp) {
            var structure = new GameObject("Structure");
            structure.transform.SetParent(parent);

            float x = bp.Value<float?>("x") ?? 0f;
            float y = bp.Value<float?>("y") ?? 0f;
            float z = bp.Value<float?>("z") ?? 0f;
            structure.transform.localPosition = new Vector3(x, y, z) / 1000f;

            var mesh = BuildMeshFromArmour(bp);
            if (mesh != null)
            {
                var meshFilter = structure.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                
                var renderer = structure.AddComponent<MeshRenderer>();
                renderer.material = CreateDefaultMaterial(new Color(0.4f, 0.4f, 0.3f));
            }
            else
            {
                // Fallback
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                primitive.transform.SetParent(structure.transform);
                primitive.transform.localPosition = Vector3.zero;
                
                float volume = bp.Value<float?>("armourVolume") ?? 1000000f;
                float side = Mathf.Pow(volume / 1000000f, 1f / 3f);
                primitive.transform.localScale = Vector3.one * side;
                
                UnityEngine.Object.DestroyImmediate(primitive.GetComponent<Collider>());
            }
        }

        private static GameObject CreateTurret(Transform parent, Newtonsoft.Json.Linq.JToken bp) {
            var turret = new GameObject("Turret");
            turret.transform.SetParent(parent);

            float x = bp.Value<float?>("x") ?? 0f;
            float y = bp.Value<float?>("y") ?? 0f;
            float z = bp.Value<float?>("z") ?? 0f;
            turret.transform.localPosition = new Vector3(x, y, z) / 1000f;

            var mesh = BuildMeshFromArmour(bp);
            if (mesh != null)
            {
                var meshFilter = turret.AddComponent<MeshFilter>();
                meshFilter.mesh = mesh;
                
                var renderer = turret.AddComponent<MeshRenderer>();
                renderer.material = CreateDefaultMaterial(new Color(0.35f, 0.35f, 0.35f));
            }

            return turret;
        }

        private static void CreateCannon(Transform parent, JToken bp) {
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "CannonBarrel";
            barrel.transform.SetParent(parent);
            
            float caliber = GetFloat(bp, "caliber", 75f);
            float barrelLength = GetFloat(bp, "barrelLength", 3000f);
            float x = GetFloat(bp, "x", 0f);
            float y = GetFloat(bp, "y", 500f);
            float z = GetFloat(bp, "z", 2000f);

            barrel.transform.localPosition = new Vector3(x, y, z) / 1000f;
            barrel.transform.localScale = new Vector3(caliber / 1000f, barrelLength / 2000f, caliber / 1000f);
            barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);

            var renderer = barrel.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = CreateDefaultMaterial(new Color(0.2f, 0.2f, 0.2f));
                if (mat != null)
                    renderer.material = mat;
            }
        }

        private static Mesh BuildMeshFromArmour(Newtonsoft.Json.Linq.JToken bp)
    {
        try
        {
            var armourToken = bp["armour"];
            if (armourToken == null)
            {
                MelonLogger.Warning("[BlueprintSpawner] No 'armour' token found");
                return null;
            }

            MelonLogger.Msg($"[BlueprintSpawner] Found armour token, type: {armourToken.Type}");

            var segmentsToken = armourToken["segments"];
            if (segmentsToken == null)
            {
                MelonLogger.Warning("[BlueprintSpawner] No 'segments' token found in armour");
                return null;
            }

            MelonLogger.Msg($"[BlueprintSpawner] Found segments token, type: {segmentsToken.Type}");

            if (segmentsToken.Type != Newtonsoft.Json.Linq.JTokenType.Array)
            {
                MelonLogger.Warning($"[BlueprintSpawner] Segments is not an array, it's: {segmentsToken.Type}");
                return null;
            }

            var segmentsArray = (Newtonsoft.Json.Linq.JArray)segmentsToken;
            MelonLogger.Msg($"[BlueprintSpawner] Segments array has {segmentsArray.Count} items");

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            int processedSegments = 0;
            foreach (var segment in segmentsArray)
            {
                if (segment == null) continue;

                var corners = segment["corners"];
                if (corners == null || corners.Type != Newtonsoft.Json.Linq.JTokenType.Array) 
                    continue;

                var cornersArray = (Newtonsoft.Json.Linq.JArray)corners;
                if (cornersArray.Count < 3) continue;

                processedSegments++;
                if (processedSegments == 1)
                {
                    MelonLogger.Msg($"[BlueprintSpawner] First segment has {cornersArray.Count} corners");
                }

                // Extract vertex positions
                List<Vector3> segmentVerts = new List<Vector3>();
                foreach (var corner in cornersArray)
                {
                    if (corner == null) continue;

                    float cx = corner.Value<float?>("x") ?? 0f;
                    float cy = corner.Value<float?>("y") ?? 0f;
                    float cz = corner.Value<float?>("z") ?? 0f;

                    segmentVerts.Add(new Vector3(cx, cy, cz) / 1000f);
                }

                if (segmentVerts.Count < 3) continue;

                // Create triangles (fan triangulation)
                int baseIndex = vertices.Count;
                vertices.AddRange(segmentVerts);

                for (int t = 1; t < segmentVerts.Count - 1; t++)
                {
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + t);
                    triangles.Add(baseIndex + t + 1);
                }
            }

            MelonLogger.Msg($"[BlueprintSpawner] Processed {processedSegments} segments, total vertices: {vertices.Count}");

            if (vertices.Count < 3)
            {
                MelonLogger.Warning("[BlueprintSpawner] Not enough vertices for mesh");
                return null;
            }

            // Create mesh
            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MelonLogger.Msg($"[BlueprintSpawner] ✓ Created mesh with {mesh.vertexCount} vertices");
            return mesh;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"[BlueprintSpawner] Failed to build mesh: {ex.Message}");
            MelonLogger.Error($"[BlueprintSpawner] Stack trace: {ex.StackTrace}");
            return null;
        }
    }

        private static float GetFloatDirect(Newtonsoft.Json.Linq.JToken token, string key, float defaultValue)
        {
            try
            {
                var value = token[key];
                if (value == null) return defaultValue;
                return Convert.ToSingle(value.ToString());
            }
            catch
            {
                return defaultValue;
            }
        }

        private static Material CreateDefaultMaterial(Color color) {
            // Try to find existing tank materials in the scene
            var existingRenderer = UnityEngine.Object.FindObjectOfType<Renderer>();
            if (existingRenderer != null && existingRenderer.sharedMaterial != null)
            {
                Material mat = new Material(existingRenderer.sharedMaterial);
                mat.color = color;
                return mat;
            }
    
            // Fallback to primitive material
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material fallback = new Material(temp.GetComponent<Renderer>().sharedMaterial);
            fallback.color = color;
            UnityEngine.Object.DestroyImmediate(temp);
            return fallback;
        }

        // === PUBLIC API ===

        public static void RegisterTankPrefab(string tankId, GameObject prefab)
        {
            if (tankPrefabs.ContainsKey(tankId))
            {
                MelonLogger.Warning($"[BlueprintSpawner] Overwriting existing tank: {tankId}");
                UnityEngine.Object.Destroy(tankPrefabs[tankId]);
            }

            tankPrefabs[tankId] = prefab;
            tankIdList.Add(tankId);
            
            if (defaultTankId == null)
                defaultTankId = tankId;

            MelonLogger.Msg($"[BlueprintSpawner] Registered tank: {tankId}");
        }

        public static GameObject SpawnTank(string tankId, Vector3 position, Quaternion rotation) {
            EnsureInitialized();

            if (string.IsNullOrEmpty(tankId) || !tankPrefabs.TryGetValue(tankId, out var prefab))
            {
                MelonLogger.Warning($"[BlueprintSpawner] Tank '{tankId}' not found, using default...");
                
                if (defaultTankId == null || !tankPrefabs.TryGetValue(defaultTankId, out prefab))
                {
                    MelonLogger.Error("[BlueprintSpawner] No default tank available!");
                    return null;
                }
                
                tankId = defaultTankId;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            instance.name = $"{tankId}_Instance";
            instance.SetActive(true);
    
            // DEBUG: Check what we actually created
            var meshFilters = instance.GetComponentsInChildren<MeshFilter>();
            MelonLogger.Msg($"[BlueprintSpawner] Spawned with {meshFilters.Length} mesh components");
            foreach (var mf in meshFilters)
            {
                MelonLogger.Msg($"  - {mf.gameObject.name}: {(mf.mesh != null ? $"{mf.mesh.vertexCount} verts" : "NO MESH")}");
            }
    
            return instance;
        }

        public static string GetDefaultTankId()
        {
            EnsureInitialized();
            return defaultTankId;
        }

        public static List<string> GetAvailableTankIds()
        {
            EnsureInitialized();
            return new List<string>(tankIdList);
        }

        public static bool HasTank(string tankId)
        {
            EnsureInitialized();
            return tankPrefabs.ContainsKey(tankId);
        }
        
        public static int GetTankCount()
        {
            EnsureInitialized();
            return tankPrefabs.Count;
        }
    }
}