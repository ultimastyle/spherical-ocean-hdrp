using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool for setting up the Spherical Ocean system in an HDRP project.
/// </summary>
public class SphericalOceanSetup : EditorWindow
{
    [MenuItem("Tools/Spherical Ocean/Setup Wizard")]
    public static void ShowWindow()
    {
        GetWindow<SphericalOceanSetup>("Spherical Ocean Setup");
    }

    private Transform _planetCenter;
    private float _oceanRadius = 420f;
    private float _seaLevelRadius = 417f;

    private void OnGUI()
    {
        GUILayout.Label("Spherical Ocean HDRP Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _planetCenter = (Transform)EditorGUILayout.ObjectField("Planet Center", _planetCenter, typeof(Transform), true);
        _oceanRadius = EditorGUILayout.FloatField("Ocean Radius", _oceanRadius);
        _seaLevelRadius = EditorGUILayout.FloatField("Sea Level Radius", _seaLevelRadius);

        EditorGUILayout.Space();

        if (GUILayout.Button("Create Ocean GameObject", GUILayout.Height(30)))
        {
            CreateOcean();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Create Tropical Preset Assets", GUILayout.Height(25)))
        {
            CreateTropicalPreset();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Setup: Create Ocean GameObject first, then assign the created WaveCascadeData asset.\n" +
            "Tropical Preset: Creates a pre-configured WaveCascadeData + material with tropical island water colors.",
            MessageType.Info);
    }

    private void CreateOcean()
    {
        // Create ocean parent
        GameObject oceanObj = new GameObject("SphericalOcean");
        Undo.RegisterCreatedObjectUndo(oceanObj, "Create Spherical Ocean");

        // Add components
        var renderer = oceanObj.AddComponent<SphericalOceanRenderer>();
        renderer.planetCenter = _planetCenter;
        renderer.oceanRadius = _oceanRadius;
        renderer.seaLevelRadius = _seaLevelRadius;

        // Ensure folder exists
        string path = "Assets/SphericalOcean";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder("Assets", "SphericalOcean");
        }

        // Create material
        Shader shader = Shader.Find("SphericalOcean/HDRP");
        if (shader != null)
        {
            Material mat = new Material(shader) { name = "SphericalOceanMat" };
            AssetDatabase.CreateAsset(mat, path + "/SphericalOceanMat.mat");
        }

        // Create cascade data if missing
        var cascadeData = AssetDatabase.LoadAssetAtPath<WaveCascadeData>(path + "/TropicalCascades.asset");
        if (cascadeData == null)
        {
            cascadeData = ScriptableObject.CreateInstance<WaveCascadeData>();
            cascadeData.cascades = WaveCascadeData.DefaultCascades();
            AssetDatabase.CreateAsset(cascadeData, path + "/TropicalCascades.asset");
        }

        renderer.cascadeData = cascadeData;

        Selection.activeGameObject = oceanObj;
        EditorGUIUtility.PingObject(oceanObj);

        Debug.Log("[Spherical Ocean] Created ocean with tropical cascade preset. Assign textures in inspector.");
    }

    private void CreateTropicalPreset()
    {
        string path = "Assets/SphericalOcean";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder("Assets", "SphericalOcean");
        }

        // Create cascade data
        var cascadeData = AssetDatabase.LoadAssetAtPath<WaveCascadeData>(path + "/TropicalCascades.asset");
        if (cascadeData == null)
        {
            cascadeData = ScriptableObject.CreateInstance<WaveCascadeData>();
            cascadeData.cascades = WaveCascadeData.DefaultCascades();
            AssetDatabase.CreateAsset(cascadeData, path + "/TropicalCascades.asset");
            Debug.Log("[Spherical Ocean] Created TropicalCascades.asset");
        }

        // Create material with tropical defaults
        Shader shader = Shader.Find("SphericalOcean/HDRP");
        if (shader != null)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path + "/SphericalOceanTropical.mat");
            if (existing == null)
            {
                Material mat = new Material(shader) { name = "SphericalOceanTropical" };
                AssetDatabase.CreateAsset(mat, path + "/SphericalOceanTropical.mat");
                Debug.Log("[Spherical Ocean] Created SphericalOceanTropical.mat");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Spherical Ocean] Tropical preset assets ready at " + path);
    }

    [MenuItem("Tools/Spherical Ocean/Add Buoyancy to Selection")]
    public static void AddBuoyancy()
    {
        foreach (GameObject obj in Selection.gameObjects)
        {
            if (obj.GetComponent<SphericalBuoyancy>() == null)
            {
                Undo.AddComponent<SphericalBuoyancy>(obj);
                Debug.Log($"[Spherical Ocean] Added buoyancy to {obj.name}");
            }
        }
    }

    [MenuItem("Tools/Spherical Ocean/Add Underwater Effect to Camera")]
    public static void AddUnderwaterEffect()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[Spherical Ocean] No main camera found.");
            return;
        }

        if (cam.GetComponent<SphericalUnderwaterEffect>() == null)
        {
            Undo.AddComponent<SphericalUnderwaterEffect>(cam);
            Debug.Log("[Spherical Ocean] Added underwater effect to main camera.");
        }
    }
}
