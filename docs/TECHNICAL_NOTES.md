# Technical notes

## Target

- KSP 1.12.x (built and tested against 1.12.5).
- Unity 2019.4.18f1, .NET Framework 4.7.2 (`net472`).
- Optional DLC: Making History (vintage suit), Breaking Ground (future suit).

## Why a plugin and not textures only

Texture swaps (the TextureReplacer approach) can change faces and suits, but not
shapes: the head stays large and round and the body stays short. Human proportions
need new meshes, so the mod ships a C# plugin that replaces the meshes on the stock
Kerbal models.

## Approach

1. **Find the Kerbal models.** EVA Kerbals are part prefabs (`AvailablePart.partPrefab`
   with a `KerbalEVA` module). IVA Kerbals carry the `Kerbal` component and are found
   with `Resources.FindObjectsOfTypeAll<Kerbal>()`. There are male and female variants,
   and default, vintage, future and slim suit variants.
2. **Keep the skeleton.** The new meshes are skinned to the stock bones (same names), so
   stock animations, facial expressions, ragdoll, seats and ladders keep working, and
   colliders and physics are untouched. Nothing is written to save files.
3. **Swap meshes and materials.** At the main menu the plugin replaces
   `SkinnedMeshRenderer.sharedMesh`, `bones` and materials on the prefabs, so every
   Kerbal spawned later (IVA, EVA, crew portraits) uses the new look. Scene objects that
   are not spawned from those prefabs (Astronaut Complex, main menu) are patched when
   their scene loads.
4. **Proportions.** If animations only drive bone rotations, bone positions and scales on
   the prefab can be changed to give human proportions. If they also drive positions,
   the proportion change has to live in the mesh and bind poses instead. The model dump
   (`dumpKerbalModels`) samples every clip and lists which bones move or scale, which
   answers this before any modelling starts.

## Survey results (KSP 1.12.5, male Kerbals)

- IVA Kerbals: `Animator` with controller `KerbalIVAExpression`. EVA Kerbals: legacy
  `Animation` (108 clips) plus `Animator` `KerbalEVAExpression`.
- Animations translate only the mouth bones, the root (`globalMove01`) and the pelvis
  (`bn_spA01`), and scale nothing except the parachute canopy. Limb, spine and neck
  lengths are therefore free to change.
- EVA Kerbal: feet at y = 0, head top at 0.82 m. The head mesh (`headMesh01` IVA,
  `headMesh02` EVA, about 900 vertices) spans y 0.33 to 0.82, so the head is about
  60% of the height. It is skinned to `bn_headPivot_a01`, `bn_headPivot_b01`,
  `bn_lowerJaw01`, `bn_upperJaw01`, the mouth bones, `bn_neck01` and `bn_spD01`.
- Eyes, pupils, teeth and tongue are separate skinned meshes under `model01/head02`.

## Head swap (prototype)

`HeadSwapper` adds a `RealCosmonautsHead` skinned mesh next to the stock head, using
the stock bones and bind poses. The mesh comes from an `.rcm` file (see `RcmMesh.cs`)
written by `art/tools/make_heads.py`; MakeHuman's skin weights are mapped to Kerbal
bones (`jaw` to `bn_lowerJaw01`, neck bones to the neck chain, the rest to the skull).
The head is placed with its neck pivot on `bn_headPivot_a01` and scaled by `headScale`.
The stock head, eyes, teeth and tongue get an empty mesh, so mods that retexture or
toggle them (such as TextureReplacer) cannot bring them back.

## Developer tools

Set `dumpKerbalModels = true` (and optionally `exportMeshes = true`) in
`GameData/RealCosmonauts/Settings.cfg`, start the game, open the main menu, then load
any crewed vessel. Reports land in `GameData/RealCosmonauts/PluginData/KerbalDump/`
(`MainMenu/` and `Flight/`). This output is git-ignored and must not be shared publicly.

## Open questions

- Which bones do animations translate or scale (answered by the dump)?
- Do IVA Kerbals use legacy `Animation` or `Animator`?
- How are crew portraits rendered, and do they pick up prefab changes automatically?
- Compatibility with TextureReplacer and other Kerbal-changing mods.
