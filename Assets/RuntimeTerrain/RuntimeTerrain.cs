using UnityEngine;

public class RuntimeTerrain : MonoBehaviour
{
    [Header("Target")]
    public Terrain terrain;

    [Header("Runtime")]
    public bool cloneTerrainDataAtRuntime = true;

    [Header("Performance")]
    public bool useDelayLOD = true;

    [Header("Smoothing (reduces stepping)")]
    [Range(0, 3)] public int smoothIterations = 1;      // 0 = off
    [Range(0f, 1f)] public float smoothStrength = 0.5f; // blend to neighbor average


    public TerrainLayer[] layers;

    TerrainData td;

    void Awake()
    {
        if (!terrain) terrain = GetComponent<Terrain>();
        if (!terrain) terrain = Terrain.activeTerrain;
        if (!terrain) return;

        if (cloneTerrainDataAtRuntime)
        {
            TerrainData runtimeTD = Instantiate(terrain.terrainData);
            runtimeTD.name = terrain.terrainData.name + "_RuntimeClone";

            terrain.terrainData = runtimeTD;

            var tc = terrain.GetComponent<TerrainCollider>();
            if (tc) tc.terrainData = runtimeTD;
        }

        td = terrain.terrainData;
        layers = td.terrainLayers;
    }


    // --------------------------------------------------------------------------------
    // Raise and lower terrain heights
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Raise/lower height using a smoothstep radial falloff.
    /// amountNormalized is a normalized height delta (0..1 relative to terrain height range).
    /// Use negative to lower.
    /// </summary>
    public void RaiseRadial(Vector3 worldPos, float brushRadiusMeters, float amountNormalized)
    {
        if (!terrain) return;
        td ??= terrain.terrainData;

        // World -> terrain local
        Vector3 localPos = worldPos - terrain.transform.position;

        int hmRes = td.heightmapResolution;
 
        // Local -> normalized 0..1
        float nx = localPos.x / td.size.x;
        float nz = localPos.z / td.size.z;

        //Claming here can cause issues when raising terrain at the edges
        //nx = Mathf.Clamp01(nx);
        //nz = Mathf.Clamp01(nz);

        // Normalized -> heightmap indices
        int centerX = Mathf.RoundToInt(nx * (hmRes - 1));
        int centerZ = Mathf.RoundToInt(nz * (hmRes - 1));


        // Brush radius (meters) -> heightmap pixels (separate for X and Z)
        int radiusPxX = Mathf.RoundToInt((brushRadiusMeters / td.size.x) * (hmRes - 1));
        int radiusPxZ = Mathf.RoundToInt((brushRadiusMeters / td.size.z) * (hmRes - 1));
        radiusPxX = Mathf.Max(1, radiusPxX);
        radiusPxZ = Mathf.Max(1, radiusPxZ);

        // Clamp affected rect to bounds
        int x0 = Mathf.Clamp(centerX - radiusPxX, 0, hmRes - 1);
        int z0 = Mathf.Clamp(centerZ - radiusPxZ, 0, hmRes - 1);
        int x1 = Mathf.Clamp(centerX + radiusPxX, 0, hmRes - 1);
        int z1 = Mathf.Clamp(centerZ + radiusPxZ, 0, hmRes - 1);

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        float[,] heights = td.GetHeights(x0, z0, width, height);

        // Raise with smoothstep falloff
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x0 + x;
                int pz = z0 + z;

                // Elliptical normalized distance using separate radii
                float dx = (px - centerX) / (float)radiusPxX;
                float dz = (pz - centerZ) / (float)radiusPxZ;
                float dist01 = Mathf.Sqrt(dx * dx + dz * dz);

                if (dist01 > 1f) continue;

                // Smoothstep falloff (t: 1 center -> 0 edge)
                float t = Mathf.Clamp01(1f - dist01);
                float falloff = t * t * (3f - 2f * t);

                heights[z, x] = Mathf.Clamp01(heights[z, x] + amountNormalized * falloff);
            }
        }

        SmoothHeightsInPlace(heights);

        // Write back
        if (useDelayLOD) td.SetHeightsDelayLOD(x0, z0, heights);
        else td.SetHeights(x0, z0, heights);
    }

    /// <summary>
    /// Raise/lower height using a brush texture (red channel).
    /// amountNormalized is a normalized height delta (0..1 relative to terrain height range).
    /// Use negative to lower.
    /// </summary>
    public void RaiseWithTexture(Vector3 worldPos, float brushRadiusMeters, Texture2D brushTex, float amountNormalized, float rotationDegrees = 0f)
    {
        if (!terrain) return;
        if (!brushTex) return;
        td ??= terrain.terrainData;

        Vector3 localPos = worldPos - terrain.transform.position;

        int hmRes = td.heightmapResolution;

        // Local -> normalized 0..1
        float nx = localPos.x / td.size.x;
        float nz = localPos.z / td.size.z;

        //Claming here can cause issues when raising terrain at the edges
        //nx = Mathf.Clamp01(nx);
        //nz = Mathf.Clamp01(nz);

        // Normalized -> heightmap indices
        int centerX = Mathf.RoundToInt(nx * (hmRes - 1));
        int centerZ = Mathf.RoundToInt(nz * (hmRes - 1));

        int radiusPxX = Mathf.RoundToInt((brushRadiusMeters / td.size.x) * (hmRes - 1));
        int radiusPxZ = Mathf.RoundToInt((brushRadiusMeters / td.size.z) * (hmRes - 1));
        radiusPxX = Mathf.Max(1, radiusPxX);
        radiusPxZ = Mathf.Max(1, radiusPxZ);

        int x0 = Mathf.Clamp(centerX - radiusPxX, 0, hmRes - 1);
        int z0 = Mathf.Clamp(centerZ - radiusPxZ, 0, hmRes - 1);
        int x1 = Mathf.Clamp(centerX + radiusPxX, 0, hmRes - 1);
        int z1 = Mathf.Clamp(centerZ + radiusPxZ, 0, hmRes - 1);

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        float[,] heights = td.GetHeights(x0, z0, width, height);

        // Precompute rotation
        float rad = rotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x0 + x;
                int pz = z0 + z;

                float dx = (px - centerX) / (float)radiusPxX;
                float dz = (pz - centerZ) / (float)radiusPxZ;

                float dist01 = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist01 > 1f) continue;

                // Base UV (0..1)
                float u = dx * 0.5f + 0.5f;
                float v = dz * 0.5f + 0.5f;

                // Rotate UVs around center (0.5, 0.5)
                if (rotationDegrees != 0f)
                {
                    float uu = u - 0.5f;
                    float vv = v - 0.5f;

                    float ru = uu * cos - vv * sin;
                    float rv = uu * sin + vv * cos;

                    u = ru + 0.5f;
                    v = rv + 0.5f;
                }

                // Clamp to avoid sampling outside texture
                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);

                float alpha = brushTex.GetPixelBilinear(u, v).r;

                float influence = amountNormalized * alpha;
                heights[z, x] = Mathf.Clamp01(heights[z, x] + influence);
            }
        }

        SmoothHeightsInPlace(heights);

        if (useDelayLOD) td.SetHeightsDelayLOD(x0, z0, heights);
        else td.SetHeights(x0, z0, heights);
    }



    // --------------------------------------------------------------------------------
    // Painting alphamaps
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Paints alphamaps towards a target layer.
    /// paintAmount is the amount added to the target layer per call (0..1-ish). Use negative to "erase" (optional behaviour).
    /// </summary>
    public void PaintLayer(Vector3 worldPos, float brushRadiusMeters, int paintLayerIndex, float paintAmount)
    {
        if (!terrain) return;
        td ??= terrain.terrainData;

        if (td.terrainLayers == null || td.terrainLayers.Length == 0) return;

        int layerCount = td.terrainLayers.Length;
        int target = Mathf.Clamp(paintLayerIndex, 0, layerCount - 1);

        Vector3 localPos = worldPos - terrain.transform.position;

        float nx = localPos.x / td.size.x;
        float nz = localPos.z / td.size.z;

        //Claming here can cause issues when raising terrain at the edges
        //nx = Mathf.Clamp01(nx);
        //nz = Mathf.Clamp01(nz);

        int aW = td.alphamapWidth;
        int aH = td.alphamapHeight;

        int centerX = Mathf.RoundToInt(nx * (aW - 1));
        int centerZ = Mathf.RoundToInt(nz * (aH - 1));


        int radiusPxX = Mathf.RoundToInt((brushRadiusMeters / td.size.x) * (aW - 1));
        int radiusPxZ = Mathf.RoundToInt((brushRadiusMeters / td.size.z) * (aH - 1));
        radiusPxX = Mathf.Max(1, radiusPxX);
        radiusPxZ = Mathf.Max(1, radiusPxZ);

        int x0 = Mathf.Clamp(centerX - radiusPxX, 0, aW - 1);
        int z0 = Mathf.Clamp(centerZ - radiusPxZ, 0, aH - 1);
        int x1 = Mathf.Clamp(centerX + radiusPxX, 0, aW - 1);
        int z1 = Mathf.Clamp(centerZ + radiusPxZ, 0, aH - 1);

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        float[,,] alpha = td.GetAlphamaps(x0, z0, width, height);

        float add = paintAmount;

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x0 + x;
                int pz = z0 + z;

                float dx = (px - centerX) / (float)radiusPxX;
                float dz = (pz - centerZ) / (float)radiusPxZ;
                float dist01 = Mathf.Sqrt(dx * dx + dz * dz);

                if (dist01 > 1f) continue;

                float t = Mathf.Clamp01(1f - dist01);
                float falloff = t * t * (3f - 2f * t);

                float inc = add * falloff;

                alpha[z, x, target] = Mathf.Clamp01(alpha[z, x, target] + inc);


                float others = 0f;
                for (int l = 0; l < layerCount; l++)
                    if (l != target) others += alpha[z, x, l];

                float targetVal = alpha[z, x, target];
                float remain = Mathf.Clamp01(1f - targetVal);

                if (others > 1e-6f)
                {
                    float scale = remain / others;
                    for (int l = 0; l < layerCount; l++)
                        if (l != target) alpha[z, x, l] *= scale;
                }
                else
                {
                    for (int l = 0; l < layerCount; l++)
                        if (l != target) alpha[z, x, l] = 0f;

                    alpha[z, x, target] = 1f;
                }


            }
        }

        td.SetAlphamaps(x0, z0, alpha);
    }

    /// <summary>
    /// Paints alphamaps towards a target layer using a brush texture (red channel).
    /// paintAmount is the amount added to the target layer per call (0..1-ish). Use negative to "erase".
    /// </summary>
    public void PaintLayerWithTexture(Vector3 worldPos, float brushRadiusMeters, Texture2D brushTex, int paintLayerIndex, float paintAmount, float rotationDegrees = 0f)
    {
        if (!terrain) return;
        if (!brushTex) return;
        td ??= terrain.terrainData;

        if (td.terrainLayers == null || td.terrainLayers.Length == 0) return;

        int layerCount = td.terrainLayers.Length;
        int target = Mathf.Clamp(paintLayerIndex, 0, layerCount - 1);

        Vector3 localPos = worldPos - terrain.transform.position;

        float nx = localPos.x / td.size.x;
        float nz = localPos.z / td.size.z;

        //Claming here can cause issues when raising terrain at the edges
        //nx = Mathf.Clamp01(nx);
        //nz = Mathf.Clamp01(nz);

        int aW = td.alphamapWidth;
        int aH = td.alphamapHeight;

        int centerX = Mathf.RoundToInt(nx * (aW - 1));
        int centerZ = Mathf.RoundToInt(nz * (aH - 1));

        int radiusPxX = Mathf.RoundToInt((brushRadiusMeters / td.size.x) * (aW - 1));
        int radiusPxZ = Mathf.RoundToInt((brushRadiusMeters / td.size.z) * (aH - 1));
        radiusPxX = Mathf.Max(1, radiusPxX);
        radiusPxZ = Mathf.Max(1, radiusPxZ);

        int x0 = Mathf.Clamp(centerX - radiusPxX, 0, aW - 1);
        int z0 = Mathf.Clamp(centerZ - radiusPxZ, 0, aH - 1);
        int x1 = Mathf.Clamp(centerX + radiusPxX, 0, aW - 1);
        int z1 = Mathf.Clamp(centerZ + radiusPxZ, 0, aH - 1);

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        float[,,] alpha = td.GetAlphamaps(x0, z0, width, height);

        // Precompute rotation
        float rad = rotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x0 + x;
                int pz = z0 + z;

                float dx = (px - centerX) / (float)radiusPxX;
                float dz = (pz - centerZ) / (float)radiusPxZ;

                float dist01 = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist01 > 1f) continue;

                // Base UV (0..1)
                float u = dx * 0.5f + 0.5f;
                float v = dz * 0.5f + 0.5f;

                // Rotate UVs around center (0.5, 0.5)
                if (rotationDegrees != 0f)
                {
                    float uu = u - 0.5f;
                    float vv = v - 0.5f;

                    float ru = uu * cos - vv * sin;
                    float rv = uu * sin + vv * cos;

                    u = ru + 0.5f;
                    v = rv + 0.5f;
                }

                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);

                float brushAlpha = brushTex.GetPixelBilinear(u, v).r;

                float inc = paintAmount * brushAlpha;
                alpha[z, x, target] = Mathf.Clamp01(alpha[z, x, target] + inc);


                float others = 0f;
                for (int l = 0; l < layerCount; l++)
                    if (l != target) others += alpha[z, x, l];

                float targetVal = alpha[z, x, target];
                float remain = Mathf.Clamp01(1f - targetVal);

                if (others > 1e-6f)
                {
                    float scale = remain / others;
                    for (int l = 0; l < layerCount; l++)
                        if (l != target) alpha[z, x, l] *= scale;
                }
                else
                {
                    for (int l = 0; l < layerCount; l++)
                        if (l != target) alpha[z, x, l] = 0f;

                    alpha[z, x, target] = 1f;
                }

            }
        }

        td.SetAlphamaps(x0, z0, alpha);
    }



    // --------------------------------------------------------------------------------
    // Details (grass/mesh details) painting
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Paints detail density towards a target detail layer using a smoothstep radial falloff.
    /// </summary>
    public void PaintDetailsRadial(Vector3 worldPos, float brushRadiusMeters, int detailLayerIndex, int delta, int maxDensity = 16)
    {
        if (!terrain) return;
        td ??= terrain.terrainData;

        int layerCount = td.detailPrototypes?.Length ?? 0;
        if (layerCount == 0) return;

        int target = Mathf.Clamp(detailLayerIndex, 0, layerCount - 1);

        // World -> terrain local
        Vector3 localPos = worldPos - terrain.transform.position;

        int dW = td.detailWidth;
        int dH = td.detailHeight;


        //Claming here can cause issues when raising terrain at the edges
        //float nx = Mathf.Clamp01(localPos.x / td.size.x);
        //float nz = Mathf.Clamp01(localPos.z / td.size.z);

        float nx = (localPos.x / td.size.x);
        float nz = (localPos.z / td.size.z);

        int centerX = Mathf.RoundToInt(nx * (dW - 1));
        int centerZ = Mathf.RoundToInt(nz * (dH - 1));

        int radiusPxX = Mathf.RoundToInt((brushRadiusMeters / td.size.x) * (dW - 1));
        int radiusPxZ = Mathf.RoundToInt((brushRadiusMeters / td.size.z) * (dH - 1));
        radiusPxX = Mathf.Max(1, radiusPxX);
        radiusPxZ = Mathf.Max(1, radiusPxZ);

        int x0 = Mathf.Clamp(centerX - radiusPxX, 0, dW - 1);
        int z0 = Mathf.Clamp(centerZ - radiusPxZ, 0, dH - 1);
        int x1 = Mathf.Clamp(centerX + radiusPxX, 0, dW - 1);
        int z1 = Mathf.Clamp(centerZ + radiusPxZ, 0, dH - 1);

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        int[,] map = td.GetDetailLayer(x0, z0, width, height, target);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x0 + x;
                int pz = z0 + z;

                float dx = (px - centerX) / (float)radiusPxX;
                float dz = (pz - centerZ) / (float)radiusPxZ;
                float dist01 = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist01 > 1f) continue;

                float t = Mathf.Clamp01(1f - dist01);
                float falloff = t * t * (3f - 2f * t);

                int inc = Mathf.RoundToInt(delta * falloff);
                map[z, x] = Mathf.Clamp(map[z, x] + inc, 0, maxDensity);
            }
        }

        td.SetDetailLayer(x0, z0, target, map);
    }



    // --------------------------------------------------------------------------------
    // Trees painting (TreeInstances)
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Convenience: remove all trees in radius (no falloff/probability).
    /// </summary>
    public void RemoveTreesRadial(Vector3 worldPos, float radiusMeters)
    {
        if (!terrain) return;
        td ??= terrain.terrainData;

        Vector3 localPos = worldPos - terrain.transform.position;
        
        //Claming here can cause issues when raising terrain at the edges
        //float cx = Mathf.Clamp01(localPos.x / td.size.x);
        //float cz = Mathf.Clamp01(localPos.z / td.size.z);
        float cx = localPos.x / td.size.x;
        float cz = localPos.z / td.size.z;


        float rx = Mathf.Max(1e-6f, radiusMeters / td.size.x);
        float rz = Mathf.Max(1e-6f, radiusMeters / td.size.z);

        var trees = td.treeInstances;
        if (trees == null || trees.Length == 0) return;

        var kept = new System.Collections.Generic.List<TreeInstance>(trees.Length);

        for (int i = 0; i < trees.Length; i++)
        {
            var t = trees[i];
            float dx = (t.position.x - cx) / rx;
            float dz = (t.position.z - cz) / rz;
            float dist01 = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist01 > 1f) kept.Add(t);
        }

        td.treeInstances = kept.ToArray();
    }



    // --------------------------------------------------------------------------------
    // Undo / Redo
    // --------------------------------------------------------------------------------
    [System.Serializable]
    public class TerrainSnapshot
    {
        public int hmRes;
        public int amW, amH, layers;
        public int dW, dH, detailLayers;

        public float[,] heights;   // [hmRes, hmRes]  (Unity gibt [h, w] zurück)
        public float[,,] alpha;    // [amH, amW, layers]
        public int[][,] details;

        public TreeInstance[] trees;
    }

    /// <summary>
    /// Captures a full snapshot of the current terrain state (heightmap and/or alphamaps).
    /// Use this to store an undo/redo step before applying a modification.
    /// </summary>
    public TerrainSnapshot SaveFullSnapshot(bool includeHeights = true, bool includeAlpha = true, bool includeDetails = true)
    {
        if (!terrain) return null;
        td ??= terrain.terrainData;

        var snap = new TerrainSnapshot();

        if (includeHeights)
        {
            int hmRes = td.heightmapResolution;
            snap.hmRes = hmRes;
            snap.heights = td.GetHeights(0, 0, hmRes, hmRes);
        }

        if (includeAlpha)
        {
            int aW = td.alphamapWidth;
            int aH = td.alphamapHeight;
            int layerCount = td.alphamapLayers;

            snap.amW = aW;
            snap.amH = aH;
            snap.layers = layerCount;
            snap.alpha = td.GetAlphamaps(0, 0, aW, aH);
        }

        if (includeDetails)
        {
            int dW = td.detailWidth;
            int dH = td.detailHeight;
            int dLayers = td.detailPrototypes?.Length ?? 0;

            snap.dW = dW;
            snap.dH = dH;
            snap.detailLayers = dLayers;

            if (dLayers > 0)
            {
                snap.details = new int[dLayers][,];
                for (int l = 0; l < dLayers; l++)
                    snap.details[l] = td.GetDetailLayer(0, 0, dW, dH, l);
            }
        }


        snap.trees = td.treeInstances;

        return snap;
    }

    /// <summary>
    /// Restores a previously captured full snapshot (heightmap and/or alphamaps) onto the terrain.
    /// Use this to perform undo/redo by reverting the terrain to a known state.
    /// </summary>
    public void LoadFullSnapshot(TerrainSnapshot snap, bool applyHeights = true, bool applyAlpha = true, bool applyDetails = true)
    {
        if (snap == null) return;
        if (!terrain) return;
        td ??= terrain.terrainData;

        if (applyHeights && snap.heights != null)
        {
            int hmRes = td.heightmapResolution;
            if (snap.hmRes != hmRes) Debug.LogWarning($"[RuntimeTerrain] Heightmap resolution mismatch. Snapshot {snap.hmRes}, Terrain {hmRes}.");
            else
            {
                if (useDelayLOD) td.SetHeightsDelayLOD(0, 0, snap.heights);
                else td.SetHeights(0, 0, snap.heights);
            }
        }

        if (applyAlpha && snap.alpha != null)
        {
            int aW = td.alphamapWidth;
            int aH = td.alphamapHeight;
            int layers = td.alphamapLayers;

            if (snap.amW != aW || snap.amH != aH || snap.layers != layers)
                Debug.LogWarning($"[RuntimeTerrain] Alphamap mismatch. Snapshot {snap.amW}x{snap.amH} L{snap.layers}, Terrain {aW}x{aH} L{layers}.");
            else
                td.SetAlphamaps(0, 0, snap.alpha);
        }

        if (applyDetails && snap.details != null)
        {
            int dW = td.detailWidth;
            int dH = td.detailHeight;
            int dLayers = td.detailPrototypes?.Length ?? 0;

            if (snap.dW != dW || snap.dH != dH || snap.detailLayers != dLayers)
            {
                Debug.LogWarning($"[RuntimeTerrain] Detail mismatch. Snapshot {snap.dW}x{snap.dH} L{snap.detailLayers}, Terrain {dW}x{dH} L{dLayers}.");
            }
            else
            {
                for (int l = 0; l < dLayers; l++)
                    td.SetDetailLayer(0, 0, l, snap.details[l]);
            }
        }

        if (snap.trees != null)
            td.treeInstances = snap.trees;

        if (useDelayLOD) FlushDelayedLOD();
    }



    // --------------------------------------------------------------------------------
    // Helper methods
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Smooth the given heightmap in place. To reduce "stepping" artifacts.
    /// </summary>
    void SmoothHeightsInPlace(float[,] heights)
    {
        if (smoothIterations <= 0 || smoothStrength <= 0f) return;

        int height = heights.GetLength(0);
        int width = heights.GetLength(1);

        for (int it = 0; it < smoothIterations; it++)
        {
            float[,] copy = (float[,])heights.Clone();

            for (int z = 1; z < height - 1; z++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float avg =
                        (copy[z, x] +
                         copy[z - 1, x] + copy[z + 1, x] +
                         copy[z, x - 1] + copy[z, x + 1]) / 5f;

                    heights[z, x] = Mathf.Lerp(copy[z, x], avg, smoothStrength);
                }
            }
        }
    }

    /// <summary>
    /// Call this after you finished a "stroke" when using DelayLOD (optional).
    /// </summary>
    public void FlushDelayedLOD()
    {
        if (!terrain) return;
        //terrain.ApplyDelayedHeightmapModification(); -> Deprecated
        td.SyncHeightmap();      
    }

    /// <summary>
    /// Rigidbodies may go to sleep and not notice terrain collider updates.
    /// As a workaround, we briefly toggle the TerrainCollider to force
    /// Unity's physics system to rebuild contacts.
    ///
    /// This is relatively expensive and should only be used if you are
    /// not waking nearby rigidbodies manually.
    /// </summary> 
    public void UpdateTerrainCollider()
    {
        if (!terrain) return;
        var tc = terrain.GetComponent<TerrainCollider>();
        if (tc)
        {
            tc.enabled = false;
            tc.enabled = true;
        }
    }

}
