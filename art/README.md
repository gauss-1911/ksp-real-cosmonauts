# Art sources

Source files for the mod's models and textures. Everything here is original work.

- `blender/` (planned): `.blend` sources for heads, bodies and suits.
- `textures/` (planned): layered texture sources (e.g. `.kra`, `.xcf`, `.psd`).
- `reference/`: **git-ignored.** Local reference data dumped from the game
  (see `dumpKerbalModels` in `GameData/RealCosmonauts/Settings.cfg`). It is derived
  from the game's own assets, so it must never be committed or redistributed.

## Pipeline

1. Model in Blender, skinned to a skeleton whose bone names and rest pose match the
   stock Kerbal skeleton recorded in the dump.
2. Export to KSP's native `.mu` format with the `io_object_mu` Blender add-on
   (by taniwha), into `GameData/RealCosmonauts/Models/`.
3. Save textures as `.dds` (DXT5, with mipmaps) or `.png` into
   `GameData/RealCosmonauts/Textures/`. KSP loads both from `GameData` on startup.
4. The plugin takes the loaded meshes and puts them on the Kerbal models,
   remapping bones by name.
