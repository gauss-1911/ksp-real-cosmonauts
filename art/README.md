# Art sources

Source files for the mod's models and textures. Everything here is original work, except
for the third-party base data listed under [Credits](#credits).

- `blender/` (planned): `.blend` sources for heads, bodies and suits.
- `textures/` (planned): layered texture sources (e.g. `.kra`, `.xcf`, `.psd`).
- `reference/`: **git-ignored.** Local reference data dumped from the game
  (see `dumpKerbalModels` in `GameData/RealCosmonauts/Settings.cfg`). It is derived
  from the game's own assets, so it must never be committed or redistributed.

## Heads

`tools/make_heads.py` builds the heads from the MakeHuman base mesh and renders previews:

```sh
git clone --depth 1 https://github.com/makehumancommunity/makehuman
blender -b -P art/tools/make_heads.py -- --mh makehuman/makehuman/data --out /tmp/heads
```

It morphs the base mesh into an adult male or female with MakeHuman's targets, adds
seeded random face variations, cuts out the head and neck, and fits the eyes. Hair,
brows and lips in the previews are placeholder vertex masks; real textures come later.

## Pipeline

1. Model in Blender, skinned to a skeleton whose bone names and rest pose match the
   stock Kerbal skeleton recorded in the dump.
2. Export to KSP's native `.mu` format with the `io_object_mu` Blender add-on
   (by taniwha), into `GameData/RealCosmonauts/Models/`.
3. Save textures as `.dds` (DXT5, with mipmaps) or `.png` into
   `GameData/RealCosmonauts/Textures/`. KSP loads both from `GameData` on startup.
4. The plugin takes the loaded meshes and puts them on the Kerbal models,
   remapping bones by name.

## Credits

- Base head geometry, morph targets and eye proxy: [MakeHuman](http://www.makehumancommunity.org/)
  1.x assets, released under CC0 1.0 by the MakeHuman team (Data Collection AB,
  Joel Palmius, Jonas Hauquier; original work by Manuel Bastioni). They are not stored in
  this repository; the build script reads them from a MakeHuman checkout.
