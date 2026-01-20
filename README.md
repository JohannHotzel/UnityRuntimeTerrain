# Unity Runtime Terrain

**Edit Unity Terrains at runtime** — height sculpting, texture painting, details, trees + simple undo/redo.

A lightweight **runtime terrain editing system** for Unity.  
Attach `RuntimeTerrain` to a `Terrain` and modify it directly during play mode via a clean, scriptable API.

Ideal for **terraforming**, **digging**, **explosions**, **editor-like sculpting**, or gameplay-driven terrain deformation.

![Terrain Gif](Images/TerrainGif.gif)

> Demo: https://www.youtube.com/shorts/OJtpWjD3vbM

---

## ✨ Features

### 🏔️ Height Sculpting
- `RaiseRadial` — smoothstep radial falloff brush
- `RaiseWithTexture` — texture-based brush (red channel), optional rotation
- Optional smoothing pass to reduce height stepping artifacts
- Optional **DelayLOD** workflow for better performance  
  → call `FlushDelayedLOD()` after finishing a stroke

### 🎨 Texture Painting (Alphamaps / Splatmaps)
- `PaintLayer` — radial falloff, automatic layer re-normalization
- `PaintLayerWithTexture` — texture-driven alpha painting with rotation

### 🌱 Details Painting (Grass / Detail Meshes)
- `PaintDetailsRadial` — add or remove detail density with falloff

### 🌳 Trees
- `RemoveTreesRadial` — removes all tree instances inside the brush radius  
- Tree placement helpers can be added later if needed

### ↩️ Undo / Redo (Snapshot-based)
- `SaveFullSnapshot` — captures heights, alphamaps, details, and trees
- `LoadFullSnapshot` — restores a previous terrain state



## 🧪 Sample Controller

`SampleTerraformer` demonstrates:
- Mouse-driven terrain editing
- Multiple modes (terraform, explosion, paint, details, trees)
- One-step undo (`Ctrl + Z`)
- VFX integration
- Rigidbody wake-up after terrain changes

Use it as a **reference or starting point**, not a hard dependency.



## ⚠️ Notes & Limitations
- Editing near **terrain borders** may cause visible seams (especially with neighboring terrains).  
  Border stitching is not handled yet.



## 🧱 Physics / Rigidbody Note

When modifying terrain heights at runtime, **sleeping rigidbodies** may not immediately react to collider changes.  
This can result in objects appearing to float or stick.

### Recommended solutions
- Call `UpdateTerrainCollider()` after finishing a brush stroke **or**
- Wake nearby rigidbodies manually using `rb.WakeUp()`

Both approaches are shown in `SampleTerraformer`.
