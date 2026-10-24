"""Converts MakeHuman CC0 assets (base mesh + targets) into the body pack used by the app.

Only CC0 data files are read (base.obj and *.target, see the MakeHuman LICENSE.md);
no MakeHuman program code is used. Usage:

    python3 tools/makehuman/convert.py <makehuman>/makehuman/data app/assets/body/body.ddbp.gz

Pack layout (little endian, gzip compressed):
    "DDBP" u32 version (2)
    u32 vertexCount, f32[3 * vertexCount] base positions in metres
    u32 surfaceCount, { str name, u32 indexCount, u32[indexCount] triangle indices }  (body, eyes)
    u32 groupCount,   { str name, u32 count, u32[count] vertex indices }   (joint helpers)
    u32 boneCount,    { str name, i32 parent, u32 count, u32[count] head vertices }
    u8[4 * vertexCount] bone indices, u8[4 * vertexCount] weights (/255, sum 255)
    u32 targetCount,  { str name, u32 count, f32 scale, u16[count] index steps,
                        i16[count] dx, i16[count] dy, i16[count] dz }
        Indices are sorted and stored as differences from the previous index;
        a delta in metres is the stored integer times scale.
    str = u16 byte length + UTF-8
"""

import argparse
import gzip
import os
import re
import struct
import sys

import json

import numpy as np

SCALE = 0.1  # MakeHuman units are decimetres.

AGES = ("young", "old")  # Adults only.
GENDERS = ("female", "male")
LEVELS = ("min", "average", "max")

# Regional targets offered by the body editor. Private-area targets (genitals,
# pelvis bulge, nipple size / projection sliders) are deliberately not converted.
REGION_TARGETS = [
    r"breast/breast-(dist|point|trans|volume-vert)-.*",
    r"torso/torso-.*",
    r"hip/hip-.*",
    r"stomach/stomach-(navel|tone)-.*",
    r"buttocks/buttocks-volume-.*",
    r"pelvis/pelvis-tone-.*",
    r"neck/neck-(scale|trans|double)-.*",
    r"armslegs/[lr]-(upperarm|lowerarm|upperleg|lowerleg|hand-scale|foot-scale|leg-valgus)-.*",
    r"armslegs/[lr]-(upperarm|lowerarm)-(fat|muscle|scale-depth|scale-horiz|scale-vert)-.*",
    r"armslegs/(upperlegs|lowerlegs)-height-.*",
    r"measure/measure-.*",
    r"bodyshapes/bodyshapes-.*",
    # Always applied (not a slider): smooths the chest under the fitted top.
    r"breast/nipple-point-decr",
]


# Humanoid bones (VRM / Godot humanoid names) built from the MakeHuman default
# skeleton: name, MakeHuman bone whose head is the joint, parent. Every other
# MakeHuman bone gives its skin weights to the nearest listed ancestor or alias.
HUMANOID = [
    ("Hips", "root", None),
    ("Spine", "spine05", "Hips"),
    ("Chest", "spine03", "Spine"),
    ("UpperChest", "spine01", "Chest"),
    ("Neck", "neck01", "UpperChest"),
    ("Head", "head", "Neck"),
]
for side, mh in (("Left", "L"), ("Right", "R")):
    HUMANOID += [
        (f"{side}Shoulder", f"clavicle.{mh}", "UpperChest"),
        (f"{side}UpperArm", f"upperarm01.{mh}", f"{side}Shoulder"),
        (f"{side}LowerArm", f"lowerarm01.{mh}", f"{side}UpperArm"),
        (f"{side}Hand", f"wrist.{mh}", f"{side}LowerArm"),
        (f"{side}UpperLeg", f"upperleg01.{mh}", "Hips"),
        (f"{side}LowerLeg", f"lowerleg01.{mh}", f"{side}UpperLeg"),
        (f"{side}Foot", f"foot.{mh}", f"{side}LowerLeg"),
    ]

# Bones whose weights belong to a humanoid bone other than their nearest ancestor.
WEIGHT_ALIASES = {f"shoulder01.{mh}": f"{side}UpperArm" for side, mh in (("Left", "L"), ("Right", "R"))}


def build_rig(rig_dir, vertex_count):
    skeleton = json.load(open(os.path.join(rig_dir, "default.mhskel")))
    weights = json.load(open(os.path.join(rig_dir, "default_weights.mhw")))["weights"]
    bones, joints = skeleton["bones"], skeleton["joints"]

    names = [h[0] for h in HUMANOID]
    by_mh = {mh: i for i, (_, mh, _) in enumerate(HUMANOID)}

    def humanoid_for(mh_bone):
        if mh_bone in WEIGHT_ALIASES:
            return names.index(WEIGHT_ALIASES[mh_bone])
        while mh_bone is not None:
            if mh_bone in by_mh:
                return by_mh[mh_bone]
            mh_bone = bones[mh_bone]["parent"]
        return 0

    accumulated = np.zeros((vertex_count, len(HUMANOID)))
    for mh_bone, entries in weights.items():
        target = humanoid_for(mh_bone)
        for vertex, weight in entries:
            accumulated[vertex, target] += weight

    # Keep the four strongest influences per vertex, quantised to bytes summing to 255.
    order = np.argsort(-accumulated, axis=1)[:, :4]
    top = np.take_along_axis(accumulated, order, axis=1)
    total = top.sum(axis=1, keepdims=True)
    unweighted = total[:, 0] <= 0
    top = np.where(total > 0, top / np.where(total > 0, total, 1), 0)
    top[unweighted, 0] = 1.0
    order[unweighted, 0] = 0
    quantised = np.floor(top * 255).astype(np.int32)
    quantised[:, 0] += 255 - quantised.sum(axis=1)

    rig_bones = []
    for name, mh_bone, parent in HUMANOID:
        head = joints[bones[mh_bone]["head"]]
        rig_bones.append((name, names.index(parent) if parent else -1, head))
    return rig_bones, order.astype(np.uint8), quantised.astype(np.uint8)


def macro_targets():
    names = []
    for g in GENDERS:
        for a in AGES:
            for m in LEVELS:
                for w in LEVELS:
                    base = f"{g}-{a}-{m}muscle-{w}weight"
                    names.append(f"macrodetails/universal-{base}")
                    for h in ("min", "max"):
                        names.append(f"macrodetails/height/{base}-{h}height")
                    for p in ("ideal", "uncommon"):
                        names.append(f"macrodetails/proportions/{base}-{p}proportions")
                    if g == "female":
                        for cup in LEVELS:
                            for firm in LEVELS:
                                if cup == "average" and firm == "average":
                                    continue  # The base shape; there is no file for it.
                                names.append(f"breast/{base}-{cup}cup-{firm}firmness")
            for race in ("african", "asian", "caucasian"):
                names.append(f"macrodetails/{race}-{g}-{a}")
    return names


def read_obj(path):
    positions, faces, group = [], {}, None
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                positions.append([float(t) for t in line.split()[1:4]])
            elif line.startswith("g "):
                group = line.split()[1]
            elif line.startswith("f "):
                idx = [int(t.split("/")[0]) - 1 for t in line.split()[1:]]
                faces.setdefault(group, []).append(idx)
    return np.array(positions, dtype=np.float64), faces


def read_target(path):
    indices, deltas = [], []
    with open(path) as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue
            parts = line.split()
            indices.append(int(parts[0]))
            deltas.append([float(parts[1]), float(parts[2]), float(parts[3])])
    return np.array(indices, dtype=np.uint32), np.array(deltas, dtype=np.float64).reshape(-1, 3)


def triangulate(faces):
    tris = []
    for f in faces:
        for i in range(1, len(f) - 1):
            tris.append((f[0], f[i], f[i + 1]))
    return tris


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("data", help="makehuman/data directory")
    parser.add_argument("output")
    args = parser.parse_args()

    positions, faces = read_obj(os.path.join(args.data, "3dobjs", "base.obj"))
    if len(positions) > 0xFFFF:
        sys.exit("target indices are stored as u16")

    target_names = macro_targets()
    root = os.path.join(args.data, "targets")
    patterns = [re.compile(p + r"\.target$") for p in REGION_TARGETS]
    for dirpath, _, files in os.walk(root):
        for name in sorted(files):
            rel = os.path.relpath(os.path.join(dirpath, name), root).replace(os.sep, "/")
            if any(p.fullmatch(rel) for p in patterns):
                target_names.append(rel[: -len(".target")])
    target_names = sorted(set(target_names))

    targets = {}
    for name in target_names:
        path = os.path.join(root, name + ".target")
        if not os.path.exists(path):
            sys.exit(f"missing target {name}")
        idx, d = read_target(path)
        # Movements under 0.05 mm are invisible; dropping them halves the pack size.
        keep = np.abs(d).max(axis=1) * SCALE >= 5e-5
        targets[name] = (idx[keep], d[keep])

    # The underwear is drawn by the body shader from rest positions (see UnderwearCut.cs),
    # so the body is a single surface.
    eyes = faces["helper-l-eye"] + faces["helper-r-eye"]
    surfaces = [("body", triangulate(faces["body"])), ("eyes", triangulate(eyes))]

    groups = [(g[len("joint-"):], sorted({i for f in fs for i in f})) for g, fs in faces.items() if g.startswith("joint-")]

    out = bytearray(b"DDBP")
    out += struct.pack("<I", 2)

    def string(s):
        data = s.encode("utf-8")
        return struct.pack("<H", len(data)) + data

    out += struct.pack("<I", len(positions))
    out += (positions * SCALE).astype("<f4").tobytes()
    out += struct.pack("<I", len(surfaces))
    for name, tris in surfaces:
        flat = np.array(tris, dtype="<u4").ravel()
        out += string(name) + struct.pack("<I", len(flat)) + flat.tobytes()
    out += struct.pack("<I", len(groups))
    for name, idx in groups:
        out += string(name) + struct.pack("<I", len(idx)) + np.array(idx, dtype="<u4").tobytes()
    rig_bones, bone_index, bone_weight = build_rig(os.path.join(args.data, "rigs"), len(positions))
    left_arm = positions[rig_bones[[b[0] for b in rig_bones].index("LeftUpperArm")][2]].mean(axis=0)
    if left_arm[0] <= 0:
        sys.exit("expected MakeHuman .L bones on +X (the character's left)")
    out += struct.pack("<I", len(rig_bones))
    for name, parent, head in rig_bones:
        out += string(name) + struct.pack("<iI", parent, len(head)) + np.array(head, dtype="<u4").tobytes()
    out += bone_index.tobytes() + bone_weight.tobytes()

    out += struct.pack("<I", len(targets))
    for name in target_names:
        idx, d = targets[name]
        order = np.argsort(idx, kind="stable")
        idx, d = idx[order], d[order] * SCALE
        steps = np.diff(idx, prepend=0)
        scale = max(float(np.abs(d).max()) if len(d) else 0.0, 1e-9) / 32767.0
        q = np.round(d / scale).astype("<i2")
        out += string(name) + struct.pack("<If", len(idx), scale)
        out += steps.astype("<u2").tobytes() + q[:, 0].tobytes() + q[:, 1].tobytes() + q[:, 2].tobytes()

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with gzip.open(args.output, "wb", compresslevel=9) as f:
        f.write(bytes(out))
    print(f"{len(positions)} vertices, {len(rig_bones)} bones, {len(targets)} targets, "
          f"{len(out) / 1e6:.1f} MB raw -> {os.path.getsize(args.output) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
