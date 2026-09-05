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
        EditorGUILayout.HelpBox(
            "This will create a new GameObject with the Spherical Ocean Renderer, " +
            "Mesh Filter, Mesh Renderer, and all necessary components for HDRP rendering.",
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

        // Create material
        Shader shader = Shader.Find("SphericalOcean/HDRP");
        if (shader != null)
        {
            Material mat = new Material(shader) { name = "SphericalOceanMat" };
            string path = "Assets/SphericalOcean/";
            if (!AssetDatabase.IsValidFolder(path.TrimEnd('/')))
            {
                AssetDatabase.CreateFolder("Assets", "SphericalOcean");
            }
            AssetDatabase.CreateAsset(mat, path + "SphericalOceanMat.mat");
        }

        Selection.activeGameObject = oceanObj;
        EditorGUIUtility.PingObject(oceanObj);

        Debug.Log("[Spherical Ocean] Created ocean GameObject. Assign the planet center transform and adjust radii.");
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
