using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// HDRP Custom Pass that renders the spherical ocean.
/// Uses MaterialPropertyBlock for per-frame updates to avoid SRP Batcher breaks.
/// </summary>
[System.Serializable]
public class SphericalOceanCustomPass : CustomPass
{
    public SphericalOceanRenderer oceanRenderer;

    private Material _oceanMaterial;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MaterialPropertyBlock _propBlock;

    protected override void Setup(ScriptableRenderContext ctx, CommandBuffer cmd)
    {
        _propBlock = new MaterialPropertyBlock();

        if (oceanRenderer != null)
        {
            _mf = oceanRenderer.GetComponent<MeshFilter>();
            _mr = oceanRenderer.GetComponent<MeshRenderer>();
            if (_mr != null) _oceanMaterial = _mr.sharedMaterial;
        }
    }

    protected override void Execute(CustomPassContext ctx)
    {
        if (_oceanMaterial == null || _mf == null || _mf.sharedMesh == null) return;
        if (oceanRenderer == null) return;

        // Only set per-frame globals via property block (time, center)
        // Material properties are set once via SyncMaterialProperties on change
        _mr.GetPropertyBlock(_propBlock);
        _propBlock.SetVector("_OceanCenterPosWorld", oceanRenderer.GetPlanetCenter());
        _propBlock.SetFloat("_CrestTime", Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup);
        _mr.SetPropertyBlock(_propBlock);

        // Draw ocean mesh
        Graphics.DrawMesh(
            _mf.sharedMesh,
            _mr.transform.localToWorldMatrix,
            _oceanMaterial,
            oceanRenderer.gameObject.layer,
            ctx.camera);
    }

    protected override void Cleanup()
    {
    }
}
