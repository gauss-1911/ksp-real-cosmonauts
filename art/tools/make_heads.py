"""
Builds human cosmonaut heads from the MakeHuman base mesh (CC0) and renders previews.

Run with Blender 4.x in background mode:

    blender -b -P art/tools/make_heads.py -- --mh /path/to/makehuman/makehuman/data --out /tmp/heads

--mh points to the "makehuman/data" folder of a MakeHuman 1.x checkout
(https://github.com/makehumancommunity/makehuman). Its base mesh and targets
were released as CC0 in September 2020.

What it does:
  1. Loads the base mesh (hm08) and morphs it with MakeHuman targets into an
     adult male or female, plus optional per-variant face shape changes.
  2. Cuts out the head and neck, and fits the low-poly eye proxy.
  3. Renders front, three-quarter and profile views of each head.
  4. Optionally saves the scene as a .blend file.

Coordinates: MakeHuman is Y-up with the face looking along +Z, in decimetres.
Blender is Z-up, so (x, y, z) becomes (x, -z, y) scaled by 0.1 (metres). The
face then looks along -Y, which is Blender's front view.
"""

import argparse
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

# --------------------------------------------------------------------------
# MakeHuman file parsing
# --------------------------------------------------------------------------


def parse_obj(path):
    """Returns (coords, uvs, faces). Each face is (vertex ids, uv ids, group name)."""
    coords, uvs, faces = [], [], []
    group = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("v "):
                _, x, y, z = line.split()[:4]
                coords.append(Vector((float(x), float(y), float(z))))
            elif line.startswith("vt "):
                _, u, v = line.split()[:3]
                uvs.append((float(u), float(v)))
            elif line.startswith("g "):
                group = line[2:].strip()
            elif line.startswith("f "):
                vids, tids = [], []
                for corner in line.split()[1:]:
                    parts = corner.split("/")
                    vids.append(int(parts[0]) - 1)
                    tids.append(int(parts[1]) - 1 if len(parts) > 1 and parts[1] else -1)
                faces.append((vids, tids, group))
    return coords, uvs, faces


def load_target(path):
    """A target is a sparse list of 'vertex dx dy dz' lines."""
    offsets = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue
            i, dx, dy, dz = line.split()[:4]
            offsets[int(i)] = Vector((float(dx), float(dy), float(dz)))
    return offsets


def apply_targets(coords, data_dir, weighted_targets):
    result = [c.copy() for c in coords]
    for rel_path, weight in weighted_targets:
        if abs(weight) < 1e-6:
            continue
        for i, d in load_target(os.path.join(data_dir, "targets", rel_path)).items():
            result[i] += d * weight
    return result


def group_center(coords, faces, group):
    ids = {v for vids, _, g in faces if g == group for v in vids}
    total = Vector()
    for v in ids:
        total += coords[v]
    return total / len(ids)


def fit_proxy(mhclo_path, coords):
    """Places a MakeHuman proxy (.mhclo) onto the morphed base mesh."""
    folder = os.path.dirname(mhclo_path)
    obj_file, scales, refs = None, {}, []
    in_verts = False
    with open(mhclo_path, encoding="utf-8") as f:
        for line in f:
            words = line.split()
            if not words or words[0].startswith("#"):
                continue
            if in_verts:
                if len(words) == 1:
                    refs.append(((int(words[0]), 0, 0), (1.0, 0.0, 0.0), (0.0, 0.0, 0.0)))
                    continue
                if len(words) >= 9:
                    v = tuple(int(w) for w in words[0:3])
                    w = tuple(float(x) for x in words[3:6])
                    o = tuple(float(x) for x in words[6:9])
                    refs.append((v, w, o))
                    continue
                in_verts = False
            if words[0] == "obj_file":
                obj_file = os.path.join(folder, words[1])
            elif words[0] in ("x_scale", "y_scale", "z_scale"):
                axis = "xyz".index(words[0][0])
                a, b, den = int(words[1]), int(words[2]), float(words[3])
                scales[axis] = abs(coords[a][axis] - coords[b][axis]) / den
            elif words[0] == "verts":
                in_verts = True

    p_coords, p_uvs, p_faces = parse_obj(obj_file)
    fitted = []
    for (v1, v2, v3), (w1, w2, w3), (ox, oy, oz) in refs:
        offset = Vector((ox * scales.get(0, 1.0), oy * scales.get(1, 1.0), oz * scales.get(2, 1.0)))
        fitted.append(coords[v1] * w1 + coords[v2] * w2 + coords[v3] * w3 + offset)
    assert len(fitted) == len(p_coords), "proxy vertex count mismatch"
    return fitted, p_uvs, p_faces


# --------------------------------------------------------------------------
# Morph recipes
# --------------------------------------------------------------------------

# Paired targets ("...-decr" / "...-incr" and similar) in these folders shape the face.
FACE_FOLDERS = ["head", "nose", "mouth", "chin", "cheek", "eyes", "forehead", "eyebrows", "ears", "neck"]
PAIR_SUFFIXES = [("decr", "incr"), ("down", "up"), ("in", "out"), ("backward", "forward"),
                 ("compress", "uncompress"), ("concave", "convex"), ("less", "more")]


def face_target_pairs(data_dir):
    """Returns {key: (negative target, positive target)}, with left/right eyes sharing a key."""
    pairs = {}
    for folder in FACE_FOLDERS:
        full = os.path.join(data_dir, "targets", folder)
        if not os.path.isdir(full):
            continue
        names = {n[:-len(".target")] for n in os.listdir(full) if n.endswith(".target")}
        for name in sorted(names):
            for neg, pos in PAIR_SUFFIXES:
                if name.endswith("-" + neg) and name[: -len(neg)] + pos in names:
                    base = name[: -len(neg) - 1]
                    key = folder + "/" + base.replace("r-", "").replace("l-", "")
                    pairs.setdefault(key, []).append(
                        (f"{folder}/{name}.target", f"{folder}/{base}-{pos}.target"))
    return pairs


# Some features look odd quickly when pushed, so they vary less.
FOLDER_STRENGTH = {"ears": 0.4, "neck": 0.5, "eyes": 0.6}


def recipe(data_dir, gender, seed=None, strength=0.22):
    """Weighted target list for an adult (25 years) of the given gender ('male' or 'female')."""
    targets = [
        (f"macrodetails/universal-{gender}-young-averagemuscle-averageweight.target", 1.0),
    ]
    rng = random.Random(seed)
    # Mostly European features, with a small random mix for variety between faces.
    mix = [0.8, 0.1, 0.1] if seed is None else [0.6 + 0.3 * rng.random(), 0.2 * rng.random(), 0.2 * rng.random()]
    total = sum(mix)
    for race, share in zip(["caucasian", "african", "asian"], mix):
        targets.append((f"macrodetails/{race}-{gender}-young.target", share / total))

    if seed is not None:
        for key, variants in sorted(face_target_pairs(data_dir).items()):
            value = rng.gauss(0.0, strength * FOLDER_STRENGTH.get(key.split("/")[0], 1.0))
            value = max(-1.0, min(1.0, value))
            for neg, pos in variants:  # left and right share one value
                targets.append((pos, value) if value > 0 else (neg, -value))
    return targets


# --------------------------------------------------------------------------
# Blender scene building
# --------------------------------------------------------------------------


def to_blender(v):
    return Vector((v.x, -v.z, v.y)) * 0.1


def build_object(name, coords, uvs, faces, keep_face, material):
    used = sorted({v for i, (vids, _, _) in enumerate(faces) if keep_face(i) for v in vids})
    remap = {v: n for n, v in enumerate(used)}
    mesh = bpy.data.meshes.new(name)
    kept = [f for i, f in enumerate(faces) if keep_face(i)]
    mesh.from_pydata([to_blender(coords[v]) for v in used], [], [[remap[v] for v in f[0]] for f in kept])
    uv_layer = mesh.uv_layers.new(name="UVMap")
    loop = 0
    for _, tids, _ in kept:
        for t in tids:
            uv_layer.data[loop].uv = uvs[t] if t >= 0 else (0.0, 0.0)
            loop += 1
    mesh.validate()
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(material)
    bpy.context.collection.objects.link(obj)
    return obj, remap


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


LIP_TARGETS = ["mouth/mouth-upperlip-volume-incr.target", "mouth/mouth-lowerlip-volume-incr.target"]


def paint_lips(head, remap, data_dir):
    """Marks the lips (B channel of 'masks') from the vertices the lip volume targets move."""
    weights = {}
    for rel in LIP_TARGETS:
        for i, d in load_target(os.path.join(data_dir, "targets", rel)).items():
            weights[i] = max(weights.get(i, 0.0), d.length)
    top = max(weights.values())
    attr = head.data.color_attributes.new("masks", "FLOAT_COLOR", "POINT")
    for v in attr.data:
        v.color = (0.0, 0.0, 0.0, 1.0)
    for i, w in weights.items():
        if i in remap:
            attr.data[remap[i]].color = (0.0, 0.0, smoothstep(0.15, 0.6, w / top), 1.0)


def paint_masks(head, eyes, gender):
    """Adds hair (R) and eyebrow (G) masks to the 'masks' colour attribute, keeping lips (B).

    Preview only: real skin, brow and hair textures replace this later.
    """
    eye_verts = [v.co for v in eyes.data.vertices]
    eye_z = sum(v.z for v in eye_verts) / len(eye_verts)
    eye_x = sum(abs(v.x) for v in eye_verts) / len(eye_verts)
    front_y = min(v.co.y for v in head.data.vertices)

    attr = head.data.color_attributes["masks"]
    for v in head.data.vertices:
        x, y, z = abs(v.co.x), v.co.y, v.co.z
        depth = y - front_y  # 0 at the tip of the nose, growing towards the back
        # Hairline: high over the forehead, lower over the ears, down to the nape at the back.
        line = eye_z + 0.058 - 0.0011 * max(0.0, depth * 1000 - 40) * (0.62 if gender == "male" else 0.75)
        # Keep the ears clear, blending smoothly into the sideburns.
        ear = smoothstep(eye_x + 0.018, eye_x + 0.034, x) * (1.0 - smoothstep(0.0, 0.035, abs(depth - 0.10)))
        line = line + (max(line, eye_z + 0.03) - line) * ear
        hair = smoothstep(line - 0.004, line + 0.006, z)

        # Brows: an arc above each eye, thick at the inner end and thin at the outer end.
        u = (x - (eye_x - 0.02)) / 0.046
        brow = 0.0
        if 0.0 <= u <= 1.0 and depth < 0.06 and v.normal.y < -0.2:
            centre = eye_z + 0.016 + 0.006 * math.sin(math.pi * min(1.0, u * 1.3)) - 0.006 * u
            half = (0.0058 if gender == "male" else 0.0042) * (1.0 - 0.5 * u)
            brow = 0.85 * (1.0 - smoothstep(half * 0.25, half * 1.5, abs(z - centre)))
        lips = attr.data[v.index].color[2]
        attr.data[v.index].color = (hair, brow, lips, 1.0)


def skin_material(name, tone, hair_color=(0.012, 0.008, 0.005), lip_color=(0.42, 0.16, 0.13)):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*tone, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.48
    bsdf.inputs["Subsurface Weight"].default_value = 0.2
    bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
    bsdf.inputs["Subsurface Scale"].default_value = 0.004
    # Fine noise as skin pores.
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 900.0
    noise.inputs["Detail"].default_value = 4.0
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.04
    bump.inputs["Distance"].default_value = 0.0005
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # Preview hair and brows from the vertex masks, with streaky noise for texture.
    masks = nodes.new("ShaderNodeAttribute")
    masks.attribute_name = "masks"
    sep = nodes.new("ShaderNodeSeparateColor")
    links.new(masks.outputs["Color"], sep.inputs["Color"])
    hair_amount = nodes.new("ShaderNodeMath")
    hair_amount.operation = "MAXIMUM"
    links.new(sep.outputs["Red"], hair_amount.inputs[0])
    links.new(sep.outputs["Green"], hair_amount.inputs[1])
    streaks = nodes.new("ShaderNodeTexNoise")
    streaks.inputs["Scale"].default_value = 2500.0
    streaks.inputs["Detail"].default_value = 2.0
    hair_mix = nodes.new("ShaderNodeMath")
    hair_mix.operation = "MULTIPLY"
    links.new(hair_amount.outputs[0], hair_mix.inputs[0])
    shade = nodes.new("ShaderNodeMapRange")
    shade.inputs["To Min"].default_value = 0.92
    links.new(streaks.outputs["Fac"], shade.inputs["Value"])
    links.new(shade.outputs["Result"], hair_mix.inputs[1])
    lips = nodes.new("ShaderNodeMix")
    lips.data_type = "RGBA"
    lips.inputs["A"].default_value = (*tone, 1.0)
    lips.inputs["B"].default_value = (*lip_color, 1.0)
    lip_amount = nodes.new("ShaderNodeMath")
    lip_amount.operation = "MULTIPLY"
    lip_amount.inputs[1].default_value = 0.55
    links.new(sep.outputs["Blue"], lip_amount.inputs[0])
    links.new(lip_amount.outputs[0], lips.inputs["Factor"])
    mix = nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    links.new(lips.outputs["Result"], mix.inputs["A"])
    mix.inputs["B"].default_value = (*hair_color, 1.0)
    links.new(hair_mix.outputs[0], mix.inputs["Factor"])
    links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
    rough = nodes.new("ShaderNodeMapRange")
    rough.inputs["To Min"].default_value = 0.48
    rough.inputs["To Max"].default_value = 0.75
    links.new(hair_amount.outputs[0], rough.inputs["Value"])
    links.new(rough.outputs["Result"], bsdf.inputs["Roughness"])
    sss = nodes.new("ShaderNodeMapRange")
    sss.inputs["To Min"].default_value = 0.2
    sss.inputs["To Max"].default_value = 0.0
    links.new(hair_amount.outputs[0], sss.inputs["Value"])
    links.new(sss.outputs["Result"], bsdf.inputs["Subsurface Weight"])
    return mat


# Hue shift and saturation applied to MakeHuman's brown eye texture.
EYE_COLORS = {"brown": (0.02, 0.75), "blue": (0.55, 0.55), "grey": (0.55, 0.15), "green": (0.30, 0.45)}


def eye_material(texture_path, color):
    hue, sat = EYE_COLORS[color]
    mat = bpy.data.materials.new("Eyes_" + color)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(texture_path, check_existing=True)
    tint = nodes.new("ShaderNodeHueSaturation")
    tint.inputs["Hue"].default_value = 0.5 + hue
    tint.inputs["Saturation"].default_value = sat
    links.new(tex.outputs["Color"], tint.inputs["Color"])
    links.new(tint.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.3
    bsdf.inputs["Coat Weight"].default_value = 0.6
    bsdf.inputs["Coat Roughness"].default_value = 0.03
    return mat


def build_head(data_dir, name, gender, seed, tone, eye_color, location):
    coords, uvs, faces = parse_obj(os.path.join(data_dir, "3dobjs", "base.obj"))
    morphed = apply_targets(coords, data_dir, recipe(data_dir, gender, seed))

    # Keep body faces at or above the base of the neck.
    neck_y = group_center(morphed, faces, "joint-neck").y - 0.15
    keep = lambda i: faces[i][2] == "body" and all(morphed[v].y >= neck_y for v in faces[i][0])
    head, remap = build_object(name, morphed, uvs, faces, keep, skin_material(name + "_Skin", tone))
    paint_lips(head, remap, data_dir)

    eye_coords, eye_uvs, eye_faces = fit_proxy(os.path.join(data_dir, "eyes", "low-poly", "low-poly.mhclo"), morphed)
    eyes, _ = build_object(name + "_Eyes", eye_coords, eye_uvs, eye_faces, lambda i: True,
                           eye_material(os.path.join(data_dir, "eyes", "materials", "brown_eye.png"), eye_color))
    eyes.parent = head

    # Centre the head over the origin, with the bottom of the neck at z = 0.
    centre = sum((Vector(v.co) for v in head.data.vertices), Vector()) / len(head.data.vertices)
    bottom = min(v.co.z for v in head.data.vertices)
    shift = Vector((centre.x, centre.y, bottom))
    for obj in (head, eyes):
        for v in obj.data.vertices:
            v.co -= shift
    head["height"] = max(v.co.z for v in head.data.vertices)
    # Preview only: subdivide so the painted masks get smooth edges.
    subsurf = head.modifiers.new("Preview", "SUBSURF")
    subsurf.levels = 2
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.modifier_apply(modifier=subsurf.name)
    # Cut the neck square to its own axis (the neck leans forward), part way between
    # the neck and head joints, above where it flares into the shoulders.
    neck = to_blender(group_center(morphed, faces, "joint-neck")) - shift
    skull = to_blender(group_center(morphed, faces, "joint-head")) - shift
    axis = (skull - neck).normalized()
    cut = neck + (skull - neck) * 0.3
    bm = bmesh.new()
    bm.from_mesh(head.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=cut,
                           plane_no=axis, clear_inner=True)
    bm.to_mesh(head.data)
    bm.free()
    head["neck_cut"] = cut
    head["neck_axis"] = axis
    head.data.update()
    paint_masks(head, eyes, gender)
    add_collar(head, name)
    head.location = location

    print(f"{name}: {len(remap)} game vertices ({len(head.data.vertices)} after preview subdivision)")
    return head


def add_collar(head, name):
    """Preview only: a simple suit collar ring that hides the cut at the bottom of the neck."""
    cut, axis = Vector(head["neck_cut"]), Vector(head["neck_axis"])
    ring = [v.co for v in head.data.vertices if abs((v.co - cut).dot(axis)) < 0.002]
    centre = sum(ring, Vector()) / len(ring)
    radius = max((v - centre).length for v in ring) + 0.006
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=radius, depth=0.05, location=centre - axis * 0.018,
                                        rotation=axis.to_track_quat("Z", "Y").to_euler())
    collar = bpy.context.active_object
    collar.name = name + "_Collar"
    bevel = collar.modifiers.new("Bevel", "BEVEL")
    bevel.width, bevel.segments = 0.006, 3
    for poly in collar.data.polygons:
        poly.use_smooth = True
    mat = bpy.data.materials.new(name + "_Fabric")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.05, 0.06, 0.075, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    collar.data.materials.append(mat)
    collar.parent = head
    return collar


def setup_render(resolution):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 128
    # Distro builds of Blender often lack OpenImageDenoise, so only denoise when available.
    scene.cycles.use_denoising = bpy.app.build_options.openimagedenoise if hasattr(bpy.app.build_options, "openimagedenoise") else False
    scene.render.resolution_x = scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in
        bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.18, 0.19, 0.21, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world

    def area_light(name, location, energy, size, color=(1, 1, 1)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.size, data.color = energy, size, color
        light = bpy.data.objects.new(name, data)
        light.location = location
        light.rotation_euler = (Vector((0, 0, 0)) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(light)

    area_light("Key", (-0.9, -1.2, 0.9), 45, 0.8, (1.0, 0.95, 0.88))
    area_light("Fill", (1.1, -0.9, 0.3), 12, 1.2, (0.88, 0.94, 1.0))
    area_light("Rim", (0.4, 1.2, 0.9), 40, 0.6)

    cam_data = bpy.data.cameras.new("Camera")
    cam_data.lens = 85
    cam = bpy.data.objects.new("Camera", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def render_views(cam, head, out_dir, name):
    target = head.location + Vector((0, 0, head["height"] * 0.6))
    views = {"1_front": 0, "2_threequarter": 35, "3_profile": 90}
    files = []
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            obj.hide_render = obj != head and obj.parent != head
    for label, angle in views.items():
        a = math.radians(angle)
        cam.location = target + Vector((-math.sin(a), -math.cos(a), 0.04)) * 0.72
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(out_dir, f"{name}_{label}.png")
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        files.append(path)
    return files


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mh", required=True, help="MakeHuman makehuman/data folder")
    parser.add_argument("--out", required=True, help="output folder")
    parser.add_argument("--resolution", type=int, default=640)
    parser.add_argument("--blend", help="also save the scene to this .blend file")
    args = parser.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    cam = setup_render(args.resolution)

    heads = [
        # name, gender, face seed (None = average face), skin tone (linear RGB), eye colour
        ("Male_A", "male", None, (0.50, 0.30, 0.22), "grey"),
        ("Male_B", "male", 7, (0.44, 0.26, 0.18), "brown"),
        ("Female_A", "female", None, (0.54, 0.34, 0.26), "blue"),
        ("Female_B", "female", 11, (0.48, 0.30, 0.22), "green"),
    ]
    built = [build_head(args.mh, n, g, s, t, e, (i * 1.0, 0, 0)) for i, (n, g, s, t, e) in enumerate(heads)]
    for head, (name, *_rest) in zip(built, heads):
        render_views(cam, head, args.out, name)
    if args.blend:
        bpy.ops.wm.save_as_mainfile(filepath=args.blend)


if __name__ == "__main__":
    main()
