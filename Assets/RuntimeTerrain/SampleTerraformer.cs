using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

public class SampleTerraformer : MonoBehaviour
{
    // --------------------------------------------------------------------------------
    // Modes
    // --------------------------------------------------------------------------------
    public enum Mode
    {
        Explosion,
        Terraform,
        TerraformBrush,
        PaintDetails,
        RemoveTrees
    }

    [Header("Mode")]
    public Mode mode = Mode.Terraform;

    // --------------------------------------------------------------------------------
    // References
    // --------------------------------------------------------------------------------
    [Header("Refs")]
    public RuntimeTerrain runtimeTerrain;
    public VisualEffect explosionVFX;
    public VisualEffect dustVFX;
    public VisualEffect debreesVFX;

    // --------------------------------------------------------------------------------
    // Brush parameters
    // --------------------------------------------------------------------------------
    [Header("Brush")]
    public float brushRadiusMeters = 5f;

    // --------------------------------------------------------------------------------
    // Height sculpting
    // --------------------------------------------------------------------------------
    [Header("Height Sculpting")]
    [Tooltip("Height change in meters per second (Terraform) or meters per click (Explosion). Shift inverts.")]
    public float heightStrengthMeters = 2f;

    // --------------------------------------------------------------------------------
    // Terraform Brush (texture driven)
    // --------------------------------------------------------------------------------
    [Header("Terraform Brush")]
    public Texture2D heightBrush;
    [Range(0f, 360f)]
    public float brushRotationDegrees = 0f;

    // --------------------------------------------------------------------------------
    // Texture painting
    // --------------------------------------------------------------------------------
    [Header("Texture Painting")]
    public int paintLayerIndex = 0;

    [Tooltip("Opacity added per second (Terraform) or per click (Explosion).")]
    public float paintStrength = 0.5f;

    // --------------------------------------------------------------------------------
    // Explosion multipliers
    // --------------------------------------------------------------------------------
    [Header("Explosion Multipliers")]
    [Tooltip("Explosion uses heightStrengthMeters * this (one-shot).")]
    public float explosionHeightMultiplier = 8f;

    [Tooltip("Explosion uses paintStrength * this (one-shot).")]
    public float explosionPaintMultiplier = 8f;

    // --------------------------------------------------------------------------------
    // Details painting
    // --------------------------------------------------------------------------------
    [Header("Details Painting")]
    public int detailLayerIndex = 0;

    [Tooltip("Detail density change per second. Shift inverts (removes).")]
    public int detailStrength = 200;
    public int detailMaxDensity = 200;

    // --------------------------------------------------------------------------------
    // Input / raycasting
    // --------------------------------------------------------------------------------
    [Header("Input")]
    public float rayMaxDistance = 5000f;

    Camera cam;
    bool wasDownLastFrame;

    // --------------------------------------------------------------------------------
    // Undo/Redo (one-step undo)
    // --------------------------------------------------------------------------------
    RuntimeTerrain.TerrainSnapshot pendingSnapshot;
    RuntimeTerrain.TerrainSnapshot lastSnapshot;

    void Awake()
    {
        if (!runtimeTerrain) runtimeTerrain = FindFirstObjectByType<RuntimeTerrain>();
        cam = Camera.main;
    }

    void Update()
    {
        if (!runtimeTerrain || !cam || Mouse.current == null)
            return;

        // Ctrl+Z => restore last snapshot (one-step undo)
        if (Keyboard.current != null)
        {
            bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (ctrl && Keyboard.current.zKey.wasPressedThisFrame)
            {
                if (lastSnapshot != null)
                    runtimeTerrain.LoadFullSnapshot(lastSnapshot, applyHeights: true, applyAlpha: true, applyDetails: true);

                return;
            }
        }

        bool isDown = Mouse.current.leftButton.isPressed;
        bool pressedThisFrame = isDown && !wasDownLastFrame;
        bool releasedThisFrame = !isDown && wasDownLastFrame;
        wasDownLastFrame = isDown;

        // Start stroke: capture "before"
        if (pressedThisFrame)
        {
            pendingSnapshot = runtimeTerrain.SaveFullSnapshot(includeHeights: true, includeAlpha: true, includeDetails: true);
        }

        // End stroke: commit the "before" snapshot as the last undo point
        if (releasedThisFrame)
        {
            if (pendingSnapshot != null)
                lastSnapshot = pendingSnapshot;

            pendingSnapshot = null;

            // If RuntimeTerrain uses DelayLOD, this is the right time to sync after a stroke.
            runtimeTerrain.FlushDelayedLOD();

            // Update colliders now that editing is done
            runtimeTerrain.UpdateTerrainCollider();
        }

        // Existing mode gating
        if (!isDown && mode == Mode.Terraform) return;
        if (!pressedThisFrame && mode == Mode.Explosion) return;
        if (!isDown && mode == Mode.TerraformBrush) return;
        if (!isDown && mode == Mode.PaintDetails) return;
        if (!isDown && mode == Mode.RemoveTrees) return;

        if (!TryGetTerrainHit(out var hit))
            return;

        switch (mode)
        {
            case Mode.Explosion:
                ApplyExplosion(hit.point);
                break;

            case Mode.Terraform:
                ApplyTerraform(hit.point, Time.deltaTime);
                break;

            case Mode.TerraformBrush:
                ApplyTerraformBrush(hit.point, Time.deltaTime);
                break;

            case Mode.PaintDetails:
                ApplyPaintDetails(hit.point);
                break;

            case Mode.RemoveTrees:
                ApplyRemoveTrees(hit.point);
                break;
        }

        WakeRigidbodiesInRadius(hit.point, brushRadiusMeters);
    }

    // --------------------------------------------------------------------------------
    // Raycast / input helpers
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Raycasts from the mouse cursor into the scene and returns a hit only if it hits a TerrainCollider.
    /// </summary>
    bool TryGetTerrainHit(out RaycastHit hit)
    {
        hit = default;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out hit, rayMaxDistance))
            return false;

        return hit.collider is TerrainCollider;
    }

    /// <summary>
    /// Returns true if either shift key is currently pressed (used to invert operations).
    /// </summary>
    bool IsShiftDown()
    {
        if (Keyboard.current == null) return false;
        return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
    }


    /// <summary>
    /// Wakes all rigidbodies within the given radius (used after terrain modification).
    /// </summary>
    void WakeRigidbodiesInRadius(Vector3 center, float radius)
    {
        var cols = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < cols.Length; i++)
        {
            var rb = cols[i].attachedRigidbody;
            if (!rb) continue;

            rb.WakeUp();
        }
    }

    // --------------------------------------------------------------------------------
    // Terraforming (height + texture paint)
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Convenience wrapper: converts "meters" to normalized terrain height delta and applies:
    /// 1) height sculpt (RaiseRadial) and 2) splat paint (PaintLayer).
    /// </summary>
    void RaiseAndPaint(Vector3 worldPoint, float meters, float paintAmount)
    {
        // Height normalized to terrain height range
        float amountNormalized = meters / runtimeTerrain.terrain.terrainData.size.y;

        runtimeTerrain.RaiseRadial(worldPoint, brushRadiusMeters, amountNormalized);
        runtimeTerrain.PaintLayer(worldPoint, brushRadiusMeters, paintLayerIndex, paintAmount);
    }

    /// <summary>
    /// One-shot crater/explosion: applies a stronger, instantaneous height + paint change and plays VFX.
    /// </summary>
    void ApplyExplosion(Vector3 worldPoint)
    {
        float meters = heightStrengthMeters * explosionHeightMultiplier;
        float paint = paintStrength * explosionPaintMultiplier;

        // Negative meters -> "dig" / crater
        RaiseAndPaint(worldPoint, -meters, paint);
        PlayFx(worldPoint);
    }

    /// <summary>
    /// Continuous terraform while mouse is held:
    /// - heightStrengthMeters is applied per second
    /// - paintStrength is applied per second
    /// - Shift inverts height direction (raise vs lower)
    /// </summary>
    void ApplyTerraform(Vector3 worldPoint, float dt)
    {
        float meters = heightStrengthMeters * dt;
        float paint = paintStrength * dt;

        if (IsShiftDown()) meters *= -1f;

        RaiseAndPaint(worldPoint, meters, paint);
    }

    /// <summary>
    /// Continuous terraform using a brush texture (red channel) for both:
    /// - height sculpt (RaiseWithTexture)
    /// - texture paint (PaintLayerWithTexture)
    /// Supports optional brush rotation.
    /// </summary>
    void ApplyTerraformBrush(Vector3 worldPoint, float dt)
    {
        if (!heightBrush || !runtimeTerrain)
            return;

        float meters = heightStrengthMeters * dt;
        float paint = paintStrength * dt;

        if (IsShiftDown()) meters *= -1f;

        float amountNormalized = meters / runtimeTerrain.terrain.terrainData.size.y;

        runtimeTerrain.RaiseWithTexture(worldPoint, brushRadiusMeters, heightBrush, amountNormalized, brushRotationDegrees);
        runtimeTerrain.PaintLayerWithTexture(worldPoint, brushRadiusMeters, heightBrush, paintLayerIndex, paint, brushRotationDegrees);
    }

    // --------------------------------------------------------------------------------
    // Details painting (grass/foliage)
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Paints detail density (grass/foliage) using a radial smoothstep falloff.
    /// Shift inverts (removes).
    /// </summary>
    void ApplyPaintDetails(Vector3 worldPoint)
    {
        int delta = Mathf.RoundToInt(detailStrength);
        if (delta == 0) delta = (detailStrength > 0 ? 1 : -1);

        if (IsShiftDown()) delta *= -1;

        runtimeTerrain.PaintDetailsRadial(worldPoint, brushRadiusMeters, detailLayerIndex, delta, detailMaxDensity);
    }

    // --------------------------------------------------------------------------------
    // Trees
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Removes all tree instances inside the brush radius (no falloff/probability).
    /// </summary>
    void ApplyRemoveTrees(Vector3 worldPoint)
    {
        runtimeTerrain.RemoveTreesRadial(worldPoint, brushRadiusMeters);
    }

    // --------------------------------------------------------------------------------
    // VFX
    // --------------------------------------------------------------------------------
    /// <summary>
    /// Plays configured VFX at the given world position (used for explosion mode).
    /// </summary>
    void PlayFx(Vector3 worldPoint)
    {
        if (explosionVFX) { explosionVFX.transform.position = worldPoint; explosionVFX.Play(); }
        if (dustVFX) { dustVFX.transform.position = worldPoint; dustVFX.Play(); }
        if (debreesVFX) { debreesVFX.transform.position = worldPoint; debreesVFX.Play(); }
    }
}
