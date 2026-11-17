"""Shared kit for modelling wirenook props in headless Blender.

A prop is a small low-poly gadget for a VRChat avatar. Every wirenook package
that ships one builds it from code with this module, so the whole family shares
one palette, one pair of materials, one rig shape and one FBX export, and Unity
can merge them into a single draw call.

    blender -b --python blender/build_props.py        (in a repo that has props)

The contract, enforced by `validate` before anything is exported:

* Colour. Every face sits on one swatch of the wirenook palette: all its
  `UVMap` loops are on that swatch's cell centre. No other UV layer.
* Materials. `wirenook` (opaque toon) for solid parts, `wirenook-glow`
  (additive) only for things made of light, and those use emissive swatches.
* Rig. Armature `<Prop>Rig`, mesh `<Prop>` skinned to it, root bone `<Prop>` at
  the hold point (the origin), child bones `<Prop>.<Part>`. Every vertex has
  full weight on exactly one bone, because Unity hides a prop by scaling its
  root bone to zero.
* Axes. Model with forward along Blender -Y and up along +Z, every bone
  pointing along +Z with no roll. The export turns that into Unity's +Z
  forward and +Y up with no rotation or scale on any object or bone.

Written for Blender 5.0.
"""

from __future__ import annotations

import contextlib
import dataclasses
import errno
import json
import math
import os
import pathlib
import shutil
import tempfile
from typing import Callable, Iterable, Sequence, Union

import bpy
import numpy
from mathutils import Matrix, Vector

SOLID = "wirenook"
GLOW = "wirenook-glow"
UV_LAYER = "UVMap"

FORWARD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))

TEXTURES = pathlib.Path(__file__).resolve().parents[2] / "Packages" / "net.wirenook.assets" / "Textures"
PALETTE_JSON = TEXTURES / "wirenook-palette.json"

UV_TOLERANCE = 1e-4
WEIGHT_TOLERANCE = 1e-4
LENGTH_TOLERANCE = 1e-5
MAX_INFLUENCES = 4
# Bone length is cosmetic: only the head and the orientation reach Unity.
BONE_LENGTH = 0.03
# How bright the additive material looks in Blender; Unity has its own setting.
GLOW_NODE = "Glow"
GLOW_STRENGTH = 0.35

# What the FBX exporter multiplies every root object by to turn Blender's
# Z-up into FBX's Y-up.
BLENDER_TO_FBX = Matrix.Rotation(-math.pi / 2.0, 4, "X")

Swatch = Union[str, Callable[[Vector], str]]


class ContractError(Exception):
    """A prop breaks the contract; the message lists every violation."""

    def __init__(self, subject: str, errors: Sequence[str]):
        super().__init__(f"{subject}: " + "; ".join(errors))
        self.subject = subject
        self.errors = list(errors)


# --------------------------------------------------------------------------
# Palette
# --------------------------------------------------------------------------


class Palette:
    """The baked swatch map: where each named colour sits on the texture."""

    def __init__(self, json_path: pathlib.Path = PALETTE_JSON):
        if not json_path.is_file():
            raise FileNotFoundError(f"{json_path} is missing; bake it with tools/palette.py")
        self.image_path = json_path.with_suffix(".png")
        self.emission_path = json_path.with_name(json_path.stem + "-emission.png")
        self.swatches = json.loads(json_path.read_text(encoding="utf-8"))["swatches"]

    def uv(self, name: str) -> tuple[float, float]:
        try:
            u, v = self.swatches[name]["uv"]
        except KeyError:
            raise KeyError(f"the wirenook palette has no swatch named {name!r}") from None
        return u, v

    def color(self, name: str) -> tuple[float, float, float]:
        """The swatch as linear RGB, which is what Blender's colour inputs take."""
        hex_colour = self.swatches[name]["color"].lstrip("#")
        srgb = [int(hex_colour[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
        return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)

    def swatch_at(self, uv: Sequence[float]) -> str | None:
        """The swatch whose cell centre this UV sits on, if any."""
        for name, swatch in self.swatches.items():
            if abs(swatch["uv"][0] - uv[0]) <= UV_TOLERANCE and abs(swatch["uv"][1] - uv[1]) <= UV_TOLERANCE:
                return name
        return None

    def is_emissive(self, name: str) -> bool:
        return bool(self.swatches[name]["emissive"])


# --------------------------------------------------------------------------
# Contract
# --------------------------------------------------------------------------


@dataclasses.dataclass(frozen=True)
class PropSpec:
    """What Unity expects of one prop; `validate` checks a scene against it."""

    name: str
    """Mesh object and root bone name, e.g. `LaserPointer`."""
    stem: str
    """File name of the .blend and .fbx, e.g. `laser-pointer`."""
    triangle_budget: int
    parts: dict[str, str | None] = dataclasses.field(default_factory=dict)
    """Child bones by part name, each mapped to its parent part (None: the root)."""
    materials: tuple[str, ...] = (SOLID,)
    beams: dict[str, float] = dataclasses.field(default_factory=dict)
    """Parts whose geometry must start at the bone and run exactly this far forward."""

    @property
    def rig_name(self) -> str:
        return self.name + "Rig"

    def bone(self, part: str | None = None) -> str:
        return self.name if part is None else f"{self.name}.{part}"

    @property
    def bone_parents(self) -> dict[str, str | None]:
        parents: dict[str, str | None] = {self.name: None}
        for part, parent in self.parts.items():
            parents[self.bone(part)] = self.bone(parent)
        return parents


# --------------------------------------------------------------------------
# Geometry
# --------------------------------------------------------------------------


def frame(axis: Vector) -> tuple[Vector, Vector, Vector]:
    """A right-handed basis (u, v, w) with w along `axis`.

    u is as close to +X as the axis allows, so angle 0 of anything revolved
    about forward or up starts on the prop's side and a quarter turn later
    reaches up (about forward) or back (about up).
    """
    w = Vector(axis).normalized()
    reference = Vector((1.0, 0.0, 0.0)) if abs(w.x) < 0.9 else Vector((0.0, 1.0, 0.0))
    u = (reference - w * reference.dot(w)).normalized()
    return u, w.cross(u), w


def ring_points(center, u: Vector, v: Vector, radius_u: float, radius_v: float, count: int, phase: float = 0.0):
    """`count` points on an ellipse; `phase` is in steps, so 0.5 turns a corner into a flat."""
    center = Vector(center)
    step = 2.0 * math.pi / count
    return [center + u * (radius_u * math.cos((k + phase) * step)) + v * (radius_v * math.sin((k + phase) * step))
            for k in range(count)]


def chamfered_rect(center, u: Vector, v: Vector, half_u: float, half_v: float, chamfer: float):
    """An eight-cornered rounded rectangle, counter-clockwise from u towards v."""
    center = Vector(center)
    corners = [(half_u, -half_v + chamfer), (half_u, half_v - chamfer), (half_u - chamfer, half_v),
               (-half_u + chamfer, half_v), (-half_u, half_v - chamfer), (-half_u, -half_v + chamfer),
               (-half_u + chamfer, -half_v), (half_u - chamfer, -half_v)]
    return [center + u * a + v * b for a, b in corners]


@dataclasses.dataclass(frozen=True)
class Volume:
    """The space an ellipsoid fills, for dropping faces buried inside another one."""

    to_unit: Matrix
    floor: float | None
    # The mesh is inscribed in the true ellipsoid, so a face just under the
    # true surface can still be on show between two facets. Shrinking the
    # test volume to fit inside the facets never removes a visible face.
    shrink: float

    def encloses(self, point: Vector) -> bool:
        if self.floor is not None and point.z <= self.floor + 1e-6:
            return False
        return (self.to_unit @ point).length < self.shrink


@dataclasses.dataclass
class _Face:
    vertices: tuple[int, ...]
    swatch: str
    material: str
    smooth: bool


class MeshBuilder:
    """Collects the polygons of one prop.

    Each face is given its swatch and each vertex its bone as it is made, so
    the colour and skinning contracts hold by construction.
    """

    def __init__(self, spec: PropSpec, palette: Palette):
        self.spec = spec
        self.palette = palette
        self.points: list[Vector] = []
        self.point_bones: list[str] = []
        self.faces: list[_Face] = []
        self.sharp_edges: set[tuple[int, int]] = set()

    def part(self, part: str | None = None, material: str = SOLID) -> "Part":
        """A group of faces that follows one bone (the root when `part` is None)."""
        if part is not None and part not in self.spec.parts:
            raise KeyError(f"{self.spec.name} has no part named {part!r}")
        return Part(self, self.spec.bone(part), material)

    def triangle_count(self) -> int:
        return sum(len(face.vertices) - 2 for face in self.faces)

    def to_mesh(self, name: str) -> tuple[bpy.types.Mesh, list[str]]:
        """Builds the Blender mesh and returns it with the bone of each vertex."""
        used = sorted({i for face in self.faces for i in face.vertices})
        remap = {old: new for new, old in enumerate(used)}
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([self.points[i] for i in used], [],
                         [[remap[i] for i in face.vertices] for face in self.faces])

        slots = [m for m in (SOLID, GLOW) if any(face.material == m for face in self.faces)]
        materials = ensure_materials(self.palette)
        for slot in slots:
            mesh.materials.append(materials[slot])
        mesh.polygons.foreach_set("material_index", [slots.index(face.material) for face in self.faces])
        mesh.polygons.foreach_set("use_smooth", [face.smooth for face in self.faces])

        uv_layer = mesh.uv_layers.new(name=UV_LAYER)
        uvs = [c for face in self.faces for _ in face.vertices for c in self.palette.uv(face.swatch)]
        uv_layer.uv.foreach_set("vector", uvs)

        sharp = {tuple(sorted((remap[a], remap[b]))) for a, b in self.sharp_edges if a in remap and b in remap}
        mesh.edges.foreach_set("use_edge_sharp", [tuple(sorted(edge.vertices)) in sharp for edge in mesh.edges])
        mesh.update()
        problems = mesh.validate()
        if problems:
            raise ContractError(name, ["the generated mesh had invalid geometry"])
        return mesh, [self.point_bones[i] for i in used]


class Part:
    """Primitive shapes that all end up skinned to one bone in one material."""

    def __init__(self, builder: MeshBuilder, bone: str, material: str):
        self.builder = builder
        self.bone = bone
        self.material = material

    # -- plumbing ----------------------------------------------------------

    def _emit(self, point: Vector, matrix: Matrix | None = None, floor: float | None = None) -> int:
        point = Vector(point) if matrix is None else matrix @ Vector(point)
        if floor is not None and point.z < floor:
            point.z = floor
        self.builder.points.append(point)
        self.builder.point_bones.append(self.bone)
        return len(self.builder.points) - 1

    def _face(self, indices: Sequence[int], swatch: Swatch, smooth: bool, away_from: Vector | None = None,
              facing: Vector | None = None) -> None:
        """Adds a polygon, wound so its normal points away from a point or along a direction."""
        indices = [index for n, index in enumerate(indices) if index != indices[n - 1]]
        if len(indices) < 3:
            return
        points = [self.builder.points[i] for i in indices]
        centroid = sum(points, Vector()) / len(points)
        # Newell's method: also right for the concave and slightly bent
        # polygons that a triangle cross product gets wrong.
        normal = Vector()
        for a, b in zip(points, points[1:] + points[:1]):
            normal += (a - centroid).cross(b - centroid)
        if normal.length < 1e-12:
            return
        if away_from is not None:
            facing = centroid - Vector(away_from)
        if facing is not None and normal.dot(facing) < 0.0:
            indices.reverse()
        name = swatch(centroid) if callable(swatch) else swatch
        self.builder.palette.uv(name)
        if self.material == GLOW and not self.builder.palette.is_emissive(name):
            raise ValueError(f"{name!r} is not an emissive swatch; light is drawn only with the glow row")
        self.builder.faces.append(_Face(tuple(indices), name, self.material, smooth))

    def _crease(self, loop: Sequence[int], closed: bool = True) -> None:
        pairs = zip(loop, list(loop[1:]) + ([loop[0]] if closed else []))
        self.builder.sharp_edges.update(tuple(sorted(pair)) for pair in pairs if pair[0] != pair[1])

    @staticmethod
    def _band_swatches(swatches, count: int) -> list[Swatch]:
        if isinstance(swatches, (list, tuple)):
            if len(swatches) != count:
                raise ValueError(f"{count} bands need {count} swatches, got {len(swatches)}")
            return list(swatches)
        return [swatches] * count

    def cull_enclosed(self, volumes: Sequence[Volume]) -> None:
        """Drops this part's faces that lie wholly inside one of `volumes`.

        Overlapping puffs are the cheapest way to draw a cloud, but most of
        each puff ends up inside its neighbours; those triangles are better
        spent on the outside. Only this part's faces are considered: faces
        on another bone can move out of a volume they start inside.
        """
        points, bones = self.builder.points, self.builder.point_bones

        def buried(face: _Face) -> bool:
            if any(bones[i] != self.bone for i in face.vertices):
                return False
            return any(all(volume.encloses(points[i]) for i in face.vertices) for volume in volumes)

        self.builder.faces = [face for face in self.builder.faces if not buried(face)]

    # -- shapes ------------------------------------------------------------

    def lathe(self, origin, axis: Vector, profile: Sequence[tuple[float, float]], swatches, segments: int, *,
              phase: float = 0.0, sharp: str | Iterable[int] = "all", cap_start: Swatch | None = None,
              cap_end: Swatch | None = None, matrix: Matrix | None = None, floor: float | None = None) -> None:
        """Revolves a profile of (distance along axis, radius) stations about `axis`.

        Walk the profile the way the outline runs with the solid on its
        inside: from the back, out and forward along the outside, and back
        in for a recess. Faces then come out facing the right way everywhere,
        including inside a cup. A radius of zero closes the shape in a point.

        `swatches` is one swatch or one per band between stations. `sharp`
        names the stations that get a hard edge: "all" suits turned, machined
        shapes, "none" suits domes and balls. `cap_start` and `cap_end` close
        an open end with a flat polygon in the given swatch.
        """
        u, v, w = frame(axis)
        origin = Vector(origin)
        bands = self._band_swatches(swatches, len(profile) - 1)
        rings = []
        for distance, radius in profile:
            centre = origin + w * distance
            if radius <= 1e-9:
                rings.append([self._emit(centre, matrix, floor)])
            else:
                rings.append([self._emit(p, matrix, floor)
                              for p in ring_points(centre, u, v, radius, radius, segments, phase)])

        for (near, far), swatch in zip(zip(rings, rings[1:]), bands):
            for k in range(segments):
                k1 = (k + 1) % segments
                if len(near) == 1 and len(far) == 1:
                    continue
                if len(near) == 1:
                    corners = (near[0], far[k1], far[k])
                elif len(far) == 1:
                    corners = (near[k], near[k1], far[0])
                else:
                    corners = (near[k], near[k1], far[k1], far[k])
                self._face(corners, swatch, smooth=True)

        stations = range(len(rings)) if sharp == "all" else () if sharp == "none" else sharp
        for index in stations:
            if len(rings[index]) > 1:
                self._crease(rings[index])
        if cap_start is not None:
            self._face(rings[0][::-1], cap_start, smooth=False)
            self._crease(rings[0])
        if cap_end is not None:
            self._face(rings[-1], cap_end, smooth=False)
            self._crease(rings[-1])

    def ellipsoid(self, center, radii, swatches, segments: int = 8, rings: int = 5, *,
                  rotation: Matrix | None = None, floor: float | None = None, phase: float = 0.0) -> Volume:
        """A soft ball with its poles along up (before `rotation`), bands listed bottom to top.

        `floor` flattens whatever would hang below that height, which is how
        a cloud gets its level underside. Returns the space it fills, for
        `cull_enclosed`.
        """
        profile = [(-math.cos(math.pi * j / rings), math.sin(math.pi * j / rings)) for j in range(rings + 1)]
        matrix = Matrix.Translation(Vector(center))
        if rotation is not None:
            matrix = matrix @ rotation.to_4x4()
        matrix = matrix @ Matrix.Diagonal((*radii, 1.0))
        self.lathe((0.0, 0.0, 0.0), UP, profile, swatches, segments, phase=phase, sharp="none",
                   matrix=matrix, floor=floor)
        inscribed = math.cos(math.pi / segments) * math.cos(math.pi / (2 * rings))
        return Volume(matrix.inverted(), floor, inscribed - 0.02)

    def sweep(self, path: Sequence, normal: Vector, section: Sequence[tuple[float, float]], swatches, *,
              closed: bool = False, hard_section: bool = False, cap_ends: bool = True) -> None:
        """Runs a cross-section along a path that lies in the plane with this `normal`.

        `section` is a polygon of (across, along normal) offsets: `across`
        lies in the path's plane, pointing to the right of travel when seen
        from the tip of `normal`. `swatches` is one swatch or one per side of
        the section. `hard_section` keeps the section's corners crisp along
        the whole run, for a faceted bar instead of a round tube.
        """
        normal = Vector(normal).normalized()
        path = [Vector(p) for p in path]
        sides = self._band_swatches(swatches, len(section))
        count = len(path)
        rings = []
        for i, point in enumerate(path):
            before = path[i - 1] if (closed or i > 0) else point
            after = path[(i + 1) % count] if (closed or i < count - 1) else point
            tangent = (after - before).normalized()
            across = tangent.cross(normal)
            # Widen the section at a bend so the bar keeps its thickness
            # between corners instead of pinching at each one.
            turn = (point - before).angle(after - point, 0.0) if before != point and after != point else 0.0
            mitre = 1.0 / max(math.cos(turn / 2.0), 0.5)
            rings.append([self._emit(point + across * (x * mitre) + normal * y) for x, y in section])

        spans = count if closed else count - 1
        for i in range(spans):
            near, far = rings[i], rings[(i + 1) % count]
            middle = (path[i] + path[(i + 1) % count]) / 2.0
            for j in range(len(section)):
                j1 = (j + 1) % len(section)
                self._face((near[j], near[j1], far[j1], far[j]), sides[j], smooth=True, away_from=middle)
        if hard_section:
            for j in range(len(section)):
                self._crease([ring[j] for ring in rings], closed=closed)
        if not closed and cap_ends:
            for ring, point, inward in ((rings[0], path[0], path[1]), (rings[-1], path[-1], path[-2])):
                self._face(ring, sides[0], smooth=False, facing=point - inward)
                self._crease(ring)

    def loft(self, rings: Sequence[Sequence], swatches, *, cap_start: Swatch | None = None,
             cap_end: Swatch | None = None, smooth: bool = False) -> None:
        """Skins a stack of outlines that each have the same number of points.

        Flat-faced by default, for boxy parts; `smooth` rounds it around the
        outlines while keeping each outline a hard edge.
        """
        bands = self._band_swatches(swatches, len(rings) - 1)
        loops = [[self._emit(p) for p in ring] for ring in rings]
        centres = [sum((Vector(p) for p in ring), Vector()) / len(ring) for ring in rings]
        for i, swatch in enumerate(bands):
            near, far = loops[i], loops[i + 1]
            middle = (centres[i] + centres[i + 1]) / 2.0
            for j in range(len(near)):
                j1 = (j + 1) % len(near)
                self._face((near[j], near[j1], far[j1], far[j]), swatch, smooth=smooth, away_from=middle)
        if smooth:
            for loop in loops:
                self._crease(loop)
        if cap_start is not None:
            self._face(loops[0], cap_start, smooth=False, facing=centres[0] - centres[1])
        if cap_end is not None:
            self._face(loops[-1], cap_end, smooth=False, facing=centres[-1] - centres[-2])

    def polygon(self, points: Sequence, swatch: Swatch, facing: Vector) -> None:
        """One flat face, visible from the side `facing` points to."""
        self._face([self._emit(p) for p in points], swatch, smooth=False, facing=Vector(facing))

    def fan(self, center, rim: Sequence, swatch: Swatch, facing: Vector) -> None:
        """Triangles from a centre to a closed rim: stars, and discs that need a middle vertex."""
        hub = self._emit(center)
        loop = [self._emit(p) for p in rim]
        for a, b in zip(loop, loop[1:] + loop[:1]):
            self._face((hub, a, b), swatch, smooth=False, facing=Vector(facing))


# --------------------------------------------------------------------------
# House motifs
# --------------------------------------------------------------------------


@dataclasses.dataclass(frozen=True)
class Puff:
    """One ball of a cloud."""

    center: tuple[float, float, float]
    radii: tuple[float, float, float]
    segments: int = 8
    rings: int = 5


def cloud_tones(underside: float, crown: float) -> Callable[[Vector], str]:
    """The wirenook cloud's colouring by height: lavender below, ice between, white on top."""
    def tone(point: Vector) -> str:
        return "lavender" if point.z < underside else "ice" if point.z < crown else "white"
    return tone


def cloud(part: Part, puffs: Sequence[Puff], floor: float, tones: Swatch) -> None:
    """A cartoon cloud: overlapping puffs sharing one level underside at `floor`."""
    volumes = [part.ellipsoid(puff.center, puff.radii, tones, puff.segments, puff.rings, floor=floor)
               for puff in puffs]
    part.cull_enclosed(volumes)


def rabbit_badge(part: Part, center, facing: Vector, size: float, swatch: Swatch) -> None:
    """The wirenook rabbit mark, flat: a round head of radius `size` and two splayed ears.

    Three polygons facing `facing`, meant to float a hair off a flat surface.
    """
    center, facing = Vector(center), Vector(facing).normalized()
    side = facing.cross(UP).normalized()
    part.polygon(ring_points(center, side, UP, size, size * 0.9, 8, 0.5), swatch, facing)
    for lean in (-1.0, 1.0):
        along = UP * math.cos(math.radians(20.0)) + side * (lean * math.sin(math.radians(20.0)))
        across = facing.cross(along)
        root = center + side * (lean * size * 0.5) + UP * (size * 0.5)
        part.polygon(ring_points(root + along * size, along, across, size * 1.1, size * 0.4, 6), swatch, facing)


# --------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------


def _palette_image(path: pathlib.Path, colorspace: str) -> bpy.types.Image:
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = colorspace
    return image


def _fresh_node_tree(material: bpy.types.Material) -> bpy.types.NodeTree:
    if material.node_tree is None:
        material.use_nodes = True
    material.node_tree.nodes.clear()
    return material.node_tree


def _swatch_texture(tree: bpy.types.NodeTree, image: bpy.types.Image) -> bpy.types.Node:
    node = tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    # Cells are solid; filtering would only smear one swatch into the next.
    node.interpolation = "Closest"
    return node


def ensure_materials(palette: Palette) -> dict[str, bpy.types.Material]:
    """The two shared materials, as close to their Unity look as a preview needs.

    Unity swaps both for its own assets by name, so only the names reach the
    game; the node trees are there so the .blend and the renders show the
    real colours.
    """
    if SOLID in bpy.data.materials and GLOW in bpy.data.materials:
        return {SOLID: bpy.data.materials[SOLID], GLOW: bpy.data.materials[GLOW]}

    colour = _palette_image(palette.image_path, "sRGB")
    emission_mask = _palette_image(palette.emission_path, "Non-Color")

    solid = bpy.data.materials.new(SOLID)
    tree = _fresh_node_tree(solid)
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    shader = tree.nodes.new("ShaderNodeBsdfPrincipled")
    swatch = _swatch_texture(tree, colour)
    mask = _swatch_texture(tree, emission_mask)
    shader.inputs["Roughness"].default_value = 1.0
    shader.inputs["Specular IOR Level"].default_value = 0.0
    tree.links.new(swatch.outputs["Color"], shader.inputs["Base Color"])
    tree.links.new(swatch.outputs["Color"], shader.inputs["Emission Color"])
    tree.links.new(mask.outputs["Color"], shader.inputs["Emission Strength"])
    tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    # Unity's solid material is single-sided, so a face wound the wrong way
    # has to show up as a hole here too.
    solid.use_backface_culling = True

    glow = bpy.data.materials.new(GLOW)
    tree = _fresh_node_tree(glow)
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    light = tree.nodes.new("ShaderNodeEmission")
    clear = tree.nodes.new("ShaderNodeBsdfTransparent")
    add = tree.nodes.new("ShaderNodeAddShader")
    swatch = _swatch_texture(tree, colour)
    light.name = GLOW_NODE
    light.inputs["Strength"].default_value = GLOW_STRENGTH
    tree.links.new(swatch.outputs["Color"], light.inputs["Color"])
    tree.links.new(light.outputs["Emission"], add.inputs[0])
    tree.links.new(clear.outputs["BSDF"], add.inputs[1])
    tree.links.new(add.outputs["Shader"], output.inputs["Surface"])
    glow.surface_render_method = "BLENDED"
    glow.use_backface_culling = False
    glow.use_transparent_shadow = True
    return {SOLID: solid, GLOW: glow}


# --------------------------------------------------------------------------
# Rig and assembly
# --------------------------------------------------------------------------


def reset_scene() -> None:
    """An empty file, so nothing from a previous prop or the user's startup file leaks in."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Building twice would otherwise leave .blend1 backups beside the sources.
    bpy.context.preferences.filepaths.save_version = 0


def build_rig(spec: PropSpec, heads: dict[str | None, Sequence[float]]) -> bpy.types.Object:
    """The armature: root at the origin, one bone per part at the head given for it."""
    missing = set(spec.parts) - set(heads)
    if missing:
        raise KeyError(f"{spec.name}: no bone position given for {sorted(missing)}")
    armature = bpy.data.armatures.new(spec.rig_name)
    rig = bpy.data.objects.new(spec.rig_name, armature)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, parent_name in spec.bone_parents.items():
        part = None if parent_name is None else bone_name[len(spec.name) + 1:]
        head = Vector((0.0, 0.0, 0.0)) if part is None else Vector(heads[part])
        bone = armature.edit_bones.new(bone_name)
        bone.head = head
        bone.tail = head + UP * BONE_LENGTH
        bone.roll = 0.0
        bone.use_connect = False
        if parent_name is not None:
            bone.parent = armature.edit_bones[parent_name]
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def assemble(builder: MeshBuilder, heads: dict[str, Sequence[float]]) -> tuple[bpy.types.Object, bpy.types.Object]:
    """Turns the collected faces into the rig and its skinned mesh."""
    spec = builder.spec
    rig = build_rig(spec, heads)
    mesh, vertex_bones = builder.to_mesh(spec.name)
    obj = bpy.data.objects.new(spec.name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = rig
    obj.modifiers.new("Armature", "ARMATURE").object = rig
    for bone_name in spec.bone_parents:
        group = obj.vertex_groups.new(name=bone_name)
        group.add([i for i, bone in enumerate(vertex_bones) if bone == bone_name], 1.0, "REPLACE")
    return rig, obj


def find_prop(spec: PropSpec) -> tuple[bpy.types.Object | None, bpy.types.Object | None]:
    return bpy.data.objects.get(spec.rig_name), bpy.data.objects.get(spec.name)


def save_blend(path: pathlib.Path) -> None:
    """Saves the scene with the palette referenced relative to the file.

    The sources are checked in, and the repos sit side by side wherever they
    are cloned; an absolute texture path would only work on one machine.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(path))
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_mainfile()


# --------------------------------------------------------------------------
# Validation
# --------------------------------------------------------------------------


def _is_identity(matrix: Matrix, tolerance: float = 1e-6) -> bool:
    return all(abs(matrix[r][c] - (1.0 if r == c else 0.0)) <= tolerance for r in range(4) for c in range(4))


def triangle_count(mesh: bpy.types.Mesh) -> int:
    return sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)


def bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ v.co for v in obj.data.vertices]
    return (Vector(min(p[axis] for p in points) for axis in range(3)),
            Vector(max(p[axis] for p in points) for axis in range(3)))


def _validate_objects(spec: PropSpec, rig, obj) -> list[str]:
    errors = []
    if rig is None or rig.type != "ARMATURE":
        errors.append(f"no armature object named {spec.rig_name}")
    if obj is None or obj.type != "MESH":
        errors.append(f"no mesh object named {spec.name}")
    if errors:
        return errors
    if obj.parent is not rig:
        errors.append(f"{spec.name} is not parented to {spec.rig_name}")
    if rig.parent is not None:
        errors.append(f"{spec.rig_name} has a parent; it must be a root object")
    for item in (rig, obj):
        if not _is_identity(item.matrix_world):
            errors.append(f"{item.name} has an unapplied transform")
    modifiers = [(m.type, getattr(m, "object", None)) for m in obj.modifiers]
    if modifiers != [("ARMATURE", rig)]:
        errors.append(f"{spec.name} needs exactly one modifier, an Armature aimed at {spec.rig_name}; "
                      "apply any others before exporting")
    if obj.data.shape_keys is not None:
        errors.append(f"{spec.name} has shape keys; props are rigid")
    return errors


def _validate_bones(spec: PropSpec, rig) -> list[str]:
    errors = []
    bones = rig.data.bones
    expected = spec.bone_parents
    if set(bones.keys()) != set(expected):
        extra = sorted(set(bones.keys()) - set(expected))
        missing = sorted(set(expected) - set(bones.keys()))
        errors.append(f"bones differ from the contract (missing {missing}, unexpected {extra})")
        return errors
    upright = Matrix.Rotation(math.pi / 2.0, 3, "X")
    for name, parent in expected.items():
        bone = bones[name]
        actual_parent = bone.parent.name if bone.parent else None
        if actual_parent != parent:
            errors.append(f"bone {name} is parented to {actual_parent}, expected {parent}")
        if not bone.use_deform:
            errors.append(f"bone {name} is not a deform bone and would not be exported")
        # Pointing up with no roll is the one orientation that Unity reads as
        # no rotation at all, which is what makes local Z the prop's forward.
        rotation = bone.matrix_local.to_3x3()
        if any(abs(rotation[r][c] - upright[r][c]) > 1e-5 for r in range(3) for c in range(3)):
            errors.append(f"bone {name} is rotated; every bone must point along +Z with no roll")
    if bones[spec.name].head_local.length > 1e-6:
        errors.append(f"root bone {spec.name} is not at the origin (the hold point)")
    return errors


def _validate_skin(spec: PropSpec, rig, obj) -> list[str]:
    errors = []
    group_names = {group.index: group.name for group in obj.vertex_groups}
    unweighted = unnormalised = crowded = shared = stray = 0
    for vertex in obj.data.vertices:
        weights = [(group_names[g.group], g.weight) for g in vertex.groups if g.weight > 0.0]
        if not weights:
            unweighted += 1
            continue
        if any(name not in rig.data.bones for name, _ in weights):
            stray += 1
        if len(weights) > MAX_INFLUENCES:
            crowded += 1
        if abs(sum(weight for _, weight in weights) - 1.0) > WEIGHT_TOLERANCE:
            unnormalised += 1
        if len(weights) > 1:
            shared += 1
    if unweighted:
        errors.append(f"{unweighted} vertices have no bone weight")
    if stray:
        errors.append(f"{stray} vertices are weighted to a group that is not a bone")
    if crowded:
        errors.append(f"{crowded} vertices have more than {MAX_INFLUENCES} influences")
    if unnormalised:
        errors.append(f"{unnormalised} vertices have weights that do not sum to 1")
    if shared:
        errors.append(f"{shared} vertices are split between bones; each must follow exactly one")
    return errors


def _validate_surface(spec: PropSpec, obj, palette: Palette) -> list[str]:
    errors = []
    mesh = obj.data
    slots = [material.name if material else None for material in mesh.materials]
    if tuple(slots) != spec.materials:
        errors.append(f"material slots are {slots}, expected {list(spec.materials)}")
        return errors
    if [layer.name for layer in mesh.uv_layers] != [UV_LAYER]:
        errors.append(f"UV layers are {[layer.name for layer in mesh.uv_layers]}, expected only {UV_LAYER}")
        return errors

    uvs = mesh.uv_layers[UV_LAYER].uv
    off_swatch = mixed = dark_glow = 0
    used_slots = set()
    for polygon in mesh.polygons:
        used_slots.add(polygon.material_index)
        names = {palette.swatch_at(uvs[loop].vector) for loop in polygon.loop_indices}
        if None in names:
            off_swatch += 1
        elif len(names) > 1:
            mixed += 1
        elif slots[polygon.material_index] == GLOW and not palette.is_emissive(next(iter(names))):
            dark_glow += 1
    if off_swatch:
        errors.append(f"{off_swatch} faces have UVs that are not on a swatch centre")
    if mixed:
        errors.append(f"{mixed} faces span more than one swatch")
    if dark_glow:
        errors.append(f"{dark_glow} {GLOW} faces use a swatch that is not emissive")
    unused = [slots[i] for i in range(len(slots)) if i not in used_slots]
    if unused:
        errors.append(f"material slots {unused} are not used by any face")

    triangles = triangle_count(mesh)
    if triangles > spec.triangle_budget:
        errors.append(f"{triangles} triangles, over the budget of {spec.triangle_budget}")
    return errors


def _validate_beams(spec: PropSpec, rig, obj) -> list[str]:
    errors = []
    for part, length in spec.beams.items():
        bone = spec.bone(part)
        group = obj.vertex_groups.get(bone)
        head = rig.data.bones[bone].head_local
        reach = [(vertex.co - head).dot(FORWARD) for vertex in obj.data.vertices
                 if group is not None and any(g.group == group.index and g.weight > 0.0 for g in vertex.groups)]
        if not reach:
            errors.append(f"nothing is skinned to {bone}")
        elif abs(min(reach)) > LENGTH_TOLERANCE or abs(max(reach) - length) > LENGTH_TOLERANCE:
            errors.append(f"{bone} runs from {min(reach):.5f} to {max(reach):.5f} m forward of its bone, "
                          f"expected 0 to {length:.5f}")
    return errors


def validate(spec: PropSpec, palette: Palette) -> list[str]:
    """Every way the open scene's copy of this prop breaks the contract."""
    rig, obj = find_prop(spec)
    errors = _validate_objects(spec, rig, obj)
    if errors:
        return errors
    errors = _validate_bones(spec, rig)
    if errors:
        return errors
    return (_validate_skin(spec, rig, obj) + _validate_surface(spec, obj, palette)
            + _validate_beams(spec, rig, obj))


# --------------------------------------------------------------------------
# FBX export
# --------------------------------------------------------------------------


@dataclasses.dataclass(frozen=True)
class ExportFacts:
    """What must survive the trip through FBX, measured in Blender before export."""

    bone_heads: dict[str, tuple[float, float, float]]
    bone_parents: dict[str, str | None]
    triangles: int
    low: tuple[float, float, float]
    high: tuple[float, float, float]
    materials: tuple[str, ...]


def _facts(rig, obj) -> ExportFacts:
    low, high = bounds(obj)
    bones = rig.data.bones
    return ExportFacts(
        {bone.name: tuple(rig.matrix_world @ bone.head_local) for bone in bones},
        {bone.name: bone.parent.name if bone.parent else None for bone in bones},
        triangle_count(obj.data), tuple(low), tuple(high),
        tuple(material.name for material in obj.data.materials))


def _write_fbx(rig, obj, path: pathlib.Path) -> None:
    """Exports the loaded copy of a prop. One-way: the scene is left in export space.

    The exporter turns Z-up into Y-up by rotating each root object, which
    Unity shows as -90 degrees on the rig with a matching counter-rotation
    on the root bone. Rotating the armature and mesh data the same way first,
    and the rig object back, leaves the prop exactly where it was while the
    exporter's rotation cancels out: the file then carries no rotation on any
    object or bone, with forward along +Z and up along +Y.
    """
    rig.data.transform(BLENDER_TO_FBX)
    obj.data.transform(BLENDER_TO_FBX)
    rig.matrix_world = BLENDER_TO_FBX.inverted()
    # Pose bones keep their old matrices until the armature is tagged, and
    # the exporter writes each bone's transform from its pose, not its rest.
    rig.data.update_tag()
    obj.data.update()
    bpy.context.view_layer.update()

    # Unity gives both materials its own palette. A texture reference in the
    # file would do nothing there except record where this machine keeps its
    # files.
    for material in obj.data.materials:
        for node in material.node_tree.nodes:
            if node.type == "TEX_IMAGE":
                node.image = None

    for item in bpy.context.scene.objects:
        item.select_set(item in (rig, obj))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        # Writes metres with a unit factor Unity honours, so nothing comes
        # in at scale 100 or 0.01.
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        use_mesh_modifiers=False,
        mesh_smooth_type="OFF",
        # Unity would triangulate anyway; doing it here makes the count that
        # was validated the count that ships.
        use_triangles=True,
        use_armature_deform_only=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        bake_anim=False,
        embed_textures=False,
        path_mode="STRIP",
        use_custom_props=False,
        use_metadata=False,
    )


def _fbx_properties(element) -> dict[str, tuple]:
    properties = next((child for child in element.elems if child.id == b"Properties70"), None)
    if properties is None:
        return {}
    return {child.props[0].decode(): tuple(child.props[4:]) for child in properties.elems}


def _fbx_name(element) -> str:
    return element.props[1].split(b"\x00")[0].decode()


def _differs(actual: Sequence[float], expected: Sequence[float], tolerance: float = 1e-4) -> bool:
    return any(abs(a - b) > tolerance for a, b in zip(actual, expected))


def _verify_fbx_file(spec: PropSpec, path: pathlib.Path, expected: ExportFacts) -> list[str]:
    """Checks the numbers Unity will read, straight from the file.

    A re-import cannot stand in for this: an importer converts the file's
    axes and units back, and rebuilds bones from the bind pose, so a file
    that Unity shows lying on its side can still round-trip perfectly. What
    Unity uses is each node's own translation, rotation and scale and the
    raw vertex positions, and those have to be upright as written: no
    rotation or scale anywhere, forward along +Z, up along +Y.
    """
    from io_scene_fbx import parse_fbx

    root, _version = parse_fbx.parse(str(path))
    errors = []
    sections = {element.id: element for element in root.elems}

    settings = _fbx_properties(sections[b"GlobalSettings"])
    axes = {key: settings.get(key, (None,))[0] for key in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign")}
    if axes != {"UpAxis": 1, "UpAxisSign": 1, "FrontAxis": 2, "FrontAxisSign": 1}:
        errors.append(f"FBX axes are {axes}, expected Y up and Z front")
    unit = settings.get("UnitScaleFactor", (None,))[0]
    if unit is None or abs(unit - 100.0) > 1e-6:
        errors.append(f"FBX unit scale factor is {unit}, expected 100 (metres)")

    # The models are published; nothing in them should say where they were made.
    header = next(child for child in sections[b"FBXHeaderExtension"].elems if child.id == b"SceneInfo")
    if _fbx_properties(header).get("Original|ApplicationNativeFile", ("",))[0]:
        errors.append("FBX records the path of the .blend it was exported from")
    if any(element.id in (b"Texture", b"Video") for element in sections[b"Objects"].elems):
        errors.append("FBX references texture files; the palette comes from Unity's shared materials")

    def in_fbx(point) -> Vector:
        return BLENDER_TO_FBX @ Vector(point)

    objects = sections[b"Objects"].elems
    bones_seen = set()
    for model in (element for element in objects if element.id == b"Model"):
        name, is_bone = _fbx_name(model), model.props[2] == b"LimbNode"
        label = f"FBX {'bone' if is_bone else 'object'} {name}"
        transform = _fbx_properties(model)
        offset = Vector((0.0, 0.0, 0.0))
        if is_bone:
            if name not in expected.bone_heads:
                errors.append(f"{label} is not a bone of the prop")
                continue
            bones_seen.add(name)
            parent = expected.bone_parents[name]
            offset = in_fbx(expected.bone_heads[name]) - in_fbx(expected.bone_heads[parent] if parent else offset)
        for key, rest in (("Lcl Rotation", 0.0), ("PreRotation", 0.0), ("Lcl Scaling", 1.0)):
            if _differs(transform.get(key, (rest,) * 3), (rest,) * 3):
                errors.append(f"{label} has {key} {transform[key]}; Unity would show it on the imported prop")
        position = transform.get("Lcl Translation", (0.0,) * 3)
        if _differs(position, offset):
            errors.append(f"{label} is at {position}, expected {tuple(offset)}")
    if bones_seen != set(expected.bone_heads):
        errors.append(f"FBX is missing bones {sorted(set(expected.bone_heads) - bones_seen)}")

    upright = (1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0)
    for cluster in (element for element in objects if element.id == b"Deformer" and element.props[2] == b"Cluster"):
        bind = next(child for child in cluster.elems if child.id == b"TransformLink").props[0]
        if _differs(bind[:12], upright) or _differs(bind[12:15], in_fbx(expected.bone_heads[_fbx_name(cluster)])):
            errors.append(f"FBX bind pose of {_fbx_name(cluster)} is rotated or displaced")

    geometry = [element for element in objects if element.id == b"Geometry"]
    if len(geometry) != 1:
        errors.append(f"FBX has {len(geometry)} meshes, expected 1")
    else:
        flat = next(child for child in geometry[0].elems if child.id == b"Vertices").props[0]
        corners = [in_fbx(expected.low), in_fbx(expected.high)]
        for axis, label in enumerate("XYZ"):
            values = flat[axis::3]
            wanted = sorted(corner[axis] for corner in corners)
            if _differs((min(values), max(values)), wanted):
                errors.append(f"FBX mesh spans {min(values):.4f} to {max(values):.4f} on {label}, "
                              f"expected {wanted[0]:.4f} to {wanted[1]:.4f}")
    return errors


def _verify_fbx_import(spec: PropSpec, path: pathlib.Path, expected: ExportFacts) -> list[str]:
    """Imports the file into an empty scene and compares it with what was exported."""
    reset_scene()
    bpy.ops.wm.fbx_import(filepath=str(path))
    errors = []
    names = sorted((item.type, item.name) for item in bpy.data.objects)
    if names != [("ARMATURE", spec.rig_name), ("MESH", spec.name)]:
        errors.append(f"re-import produced {names}")
        return errors
    rig, obj = find_prop(spec)
    if obj.parent is not rig or not any(m.type == "ARMATURE" and m.object is rig for m in obj.modifiers):
        errors.append("re-imported mesh is not skinned to the rig")
    actual = _facts(rig, obj)
    if sorted(actual.bone_heads) != sorted(expected.bone_heads):
        errors.append(f"re-imported bones are {sorted(actual.bone_heads)}, exported {sorted(expected.bone_heads)}")
    else:
        moved = [name for name in expected.bone_heads if _differs(actual.bone_heads[name], expected.bone_heads[name])]
        if moved:
            errors.append(f"re-imported bones {moved} are not where they were exported")
    if actual.triangles != expected.triangles:
        errors.append(f"re-imported mesh has {actual.triangles} triangles, exported {expected.triangles}")
    if actual.materials != expected.materials:
        errors.append(f"re-imported materials are {list(actual.materials)}, exported {list(expected.materials)}")

    # Measured on the skinned result, in world space: the mesh as the bones
    # actually pose it, still pointing along -Y with +Z up.
    posed = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    low, high = bounds(posed)
    if _differs(low, expected.low) or _differs(high, expected.high):
        errors.append(f"re-imported prop spans {tuple(round(x, 4) for x in low)} to "
                      f"{tuple(round(x, 4) for x in high)}, exported {tuple(round(x, 4) for x in expected.low)} "
                      f"to {tuple(round(x, 4) for x in expected.high)}")
    return errors


def load_prop(spec: PropSpec, blend: pathlib.Path) -> None:
    """Brings a saved prop into an empty, unsaved scene.

    Every export starts here, from the file on disk: what ships is then
    exactly what is checked in, other objects in the file stay out of it,
    and Blender's exporter, which stamps the path of the open file into
    each FBX it writes, has no path to stamp.
    """
    reset_scene()
    with bpy.data.libraries.load(str(blend)) as (source, target):
        target.objects = [name for name in (spec.rig_name, spec.name) if name in source.objects]
    for item in target.objects:
        bpy.context.scene.collection.objects.link(item)
    bpy.context.view_layer.update()


def _install(candidate: pathlib.Path, destination: pathlib.Path) -> None:
    """Puts a checked FBX in place without the destination ever being absent.

    A running Unity that finds a model missing, even for a moment, deletes
    its .meta, and with it the GUID every prefab refers to the model by. So
    the old file is never removed first: it is replaced in one step, or,
    when the temporary directory is on another volume and a rename cannot
    cross, overwritten where it stands.
    """
    destination.parent.mkdir(parents=True, exist_ok=True)
    try:
        os.replace(candidate, destination)
    except OSError as error:
        if error.errno != errno.EXDEV:
            raise
        shutil.copyfile(candidate, destination)


def export_blend(spec: PropSpec, blend: pathlib.Path, models_dir: pathlib.Path) -> ExportFacts:
    """Validates the prop saved in `blend` and writes its FBX, or raises ContractError.

    Used for generated and hand-edited sources alike. The file is written
    and checked in a temporary directory outside the package and only put
    in place once every check has passed, so a failed export never leaves
    a half-right model where Unity would import it.
    """
    palette = Palette()
    destination = models_dir / f"{spec.stem}.fbx"
    load_prop(spec, blend)
    errors = validate(spec, palette)
    if errors:
        raise ContractError(f"{spec.name} ({blend.name})", errors)
    rig, obj = find_prop(spec)
    expected = _facts(rig, obj)
    with tempfile.TemporaryDirectory(prefix="wirenook-prop-") as scratch:
        candidate = pathlib.Path(scratch) / destination.name
        _write_fbx(rig, obj, candidate)
        errors = _verify_fbx_file(spec, candidate, expected) + _verify_fbx_import(spec, candidate, expected)
        if errors:
            raise ContractError(f"{spec.name} ({destination.name})", errors)
        _install(candidate, destination)
    size = Vector(expected.high) - Vector(expected.low)
    print(f"props: {spec.name}: {expected.triangles}/{spec.triangle_budget} triangles, "
          f"bones {', '.join(expected.bone_heads)}, materials {', '.join(expected.materials)}, "
          f"size {size.x:.3f} x {size.y:.3f} x {size.z:.3f} m -> {destination}")
    return expected


# --------------------------------------------------------------------------
# Previews
# --------------------------------------------------------------------------

SKY = "#5aa8dc"
NIGHT = "#13153f"

# Where the camera stands relative to the prop, by view name.
VIEWS = {
    "quarter": Vector((0.8, -1.0, 0.55)),
    "front": Vector((0.0, -1.0, 0.0)),
    "side": Vector((1.0, 0.0, 0.0)),
}


def _linear(hex_colour: str) -> tuple[float, float, float, float]:
    srgb = [int(hex_colour.lstrip("#")[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return (*(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb), 1.0)


@contextlib.contextmanager
def _preview_scene(objects: Sequence[bpy.types.Object], background: str):
    """A throwaway scene around `objects`, so lights and cameras never reach the source file."""
    scene = bpy.data.scenes.new("Preview")
    for item in objects:
        scene.collection.objects.link(item)
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 32
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False

    world = bpy.data.worlds.new("Preview")
    if world.node_tree is None:
        world.use_nodes = True
    nodes, links = world.node_tree.nodes, world.node_tree.links
    nodes.clear()
    # The camera sees the backdrop colour, but the props are lit by plain
    # white, or a sky backdrop would tint every swatch blue.
    backdrop = nodes.new("ShaderNodeBackground")
    backdrop.inputs["Color"].default_value = _linear(background)
    ambient = nodes.new("ShaderNodeBackground")
    ambient.inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    ambient.inputs["Strength"].default_value = 0.62
    path = nodes.new("ShaderNodeLightPath")
    mix = nodes.new("ShaderNodeMixShader")
    output = nodes.new("ShaderNodeOutputWorld")
    links.new(path.outputs["Is Camera Ray"], mix.inputs["Fac"])
    links.new(ambient.outputs["Background"], mix.inputs[1])
    links.new(backdrop.outputs["Background"], mix.inputs[2])
    links.new(mix.outputs["Shader"], output.inputs["Surface"])
    scene.world = world

    sun_data = bpy.data.lights.new("PreviewSun", "SUN")
    sun_data.energy = 1.5
    sun_data.angle = math.radians(8.0)
    sun = bpy.data.objects.new("PreviewSun", sun_data)
    sun.rotation_euler = Vector((0.35, -0.55, 1.0)).to_track_quat("Z", "Y").to_euler()
    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera_data.type = "ORTHO"
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    for helper in (sun, camera):
        scene.collection.objects.link(helper)
    scene.camera = camera
    try:
        yield scene
    finally:
        bpy.data.scenes.remove(scene)
        bpy.data.objects.remove(sun)
        bpy.data.objects.remove(camera)
        bpy.data.lights.remove(sun_data)
        bpy.data.cameras.remove(camera_data)
        bpy.data.worlds.remove(world)


@contextlib.contextmanager
def _lights_out():
    """Switches the glow material off, to photograph a prop without its beam in the way."""
    emitters = [material.node_tree.nodes[GLOW_NODE] for material in bpy.data.materials
                if material.name.split(".")[0] == GLOW]
    try:
        for emitter in emitters:
            emitter.inputs["Strength"].default_value = 0.0
        yield
    finally:
        for emitter in emitters:
            emitter.inputs["Strength"].default_value = GLOW_STRENGTH


def _material_points(obj: bpy.types.Object, materials: Sequence[str]) -> list[Vector]:
    """World positions of the vertices used by faces in any of `materials`."""
    slots = {i for i, material in enumerate(obj.data.materials) if material.name.split(".")[0] in materials}
    indices = {v for polygon in obj.data.polygons if polygon.material_index in slots for v in polygon.vertices}
    return [obj.matrix_world @ obj.data.vertices[i].co for i in indices]


def _render(scene, points: Sequence[Vector], stand: Vector, path: pathlib.Path, width: int, height: int,
            margin: float = 1.12) -> None:
    """Renders with an orthographic camera at direction `stand`, framed on `points`."""
    camera = scene.camera
    rotation = (-stand).to_track_quat("-Z", "Y")
    right, up = rotation @ Vector((1.0, 0.0, 0.0)), rotation @ Vector((0.0, 1.0, 0.0))
    across = [p.dot(right) for p in points]
    along = [p.dot(up) for p in points]
    centre = sum(points, Vector()) / len(points)
    centre += right * ((min(across) + max(across)) / 2.0 - centre.dot(right))
    centre += up * ((min(along) + max(along)) / 2.0 - centre.dot(up))
    camera.rotation_euler = rotation.to_euler()
    camera.location = centre + stand.normalized() * 20.0
    camera.data.clip_end = 60.0
    # ortho_scale is the width of the view when the image is wider than tall.
    span = max(max(across) - min(across), (max(along) - min(along)) * width / height)
    camera.data.ortho_scale = span * margin if width >= height else span * margin * height / width
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True, scene=scene.name)


def _join_images(paths: Sequence[pathlib.Path], destination: pathlib.Path) -> None:
    """Sets equally tall images side by side in one file."""
    tiles = []
    for path in paths:
        image = bpy.data.images.load(str(path))
        width, height = image.size
        tiles.append(numpy.array(image.pixels[:], dtype=numpy.float32).reshape(height, width, 4))
        bpy.data.images.remove(image)
    sheet = numpy.concatenate(tiles, axis=1)
    image = bpy.data.images.new("Sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    image.pixels.foreach_set(sheet.ravel())
    image.filepath_raw = str(destination)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def render_sheet(spec: PropSpec, out_dir: pathlib.Path, background: str = SKY, size: int = 640) -> pathlib.Path:
    """One image of the prop: three-quarter, front and side, then a fourth view.

    The first three show only the solid parts, because a metre of beam would
    shrink a pocket torch to a dot and hide it from the front. The fourth is
    the three-quarter view again with the light on and everything in frame,
    or, for a prop with no light, the view from behind.
    """
    rig, obj = find_prop(spec)
    out_dir.mkdir(parents=True, exist_ok=True)
    # A prop that is all light (a marker) has no solid parts to frame on or
    # to show alone, so it is drawn lit in every view.
    solid = _material_points(obj, (SOLID,))
    all_light = not solid
    if all_light:
        solid = _material_points(obj, (GLOW,))
    tiles = []
    with _preview_scene((rig, obj), background) as scene:
        with (contextlib.nullcontext() if all_light else _lights_out()):
            for name, stand in VIEWS.items():
                tiles.append(out_dir / f"{spec.stem}-{name}.png")
                _render(scene, solid, stand, tiles[-1], size, size)
        tiles.append(out_dir / f"{spec.stem}-extra.png")
        if GLOW in spec.materials:
            _render(scene, _material_points(obj, (SOLID, GLOW)), VIEWS["quarter"], tiles[-1], size, size)
        else:
            _render(scene, solid, Vector((-0.8, 1.0, 0.55)), tiles[-1], size, size)
    sheet = out_dir / f"{spec.stem}.png"
    _join_images(tiles, sheet)
    for tile in tiles:
        tile.unlink()
    print(f"props: preview {sheet}")
    return sheet


def render_lineup(blends: Sequence[pathlib.Path], out_dir: pathlib.Path, name: str = "lineup",
                  gap: float = 0.12, width: int = 2400, height: int = 1100, lit: bool = True) -> list[pathlib.Path]:
    """The props of the given .blend files in a row at true scale, smallest first.

    Rendered once on sky blue and once on night, since light effects are
    additive and look different on each; `lit=False` leaves the effects out
    to compare the solid shapes alone. Each prop is stood on the ground
    rather than left at its hold point, so hand-held props do not hang below
    the floor line of the bigger ones.
    """
    reset_scene()
    props = []
    for blend in blends:
        with bpy.data.libraries.load(str(blend)) as (source, target):
            target.objects = list(source.objects)
        rig = next(item for item in target.objects if item.type == "ARMATURE")
        obj = next(item for item in target.objects if item.type == "MESH")
        solid = _material_points(obj, (SOLID,)) or _material_points(obj, (GLOW,))
        low = Vector(min(p[axis] for p in solid) for axis in range(3))
        high = Vector(max(p[axis] for p in solid) for axis in range(3))
        props.append((rig, obj, low, high))

    # Beams point at the camera and so run down and to the left of the
    # picture; with the small torches at the left they cross nothing.
    cursor = 0.0
    for rig, _obj, low, high in sorted(props, key=lambda prop: prop[3].z - prop[2].z):
        rig.location = Vector((cursor - low.x, 0.0, -low.z))
        cursor += (high.x - low.x) + gap
    placed = [item for rig, obj, _low, _high in props for item in (rig, obj)]

    out_dir.mkdir(parents=True, exist_ok=True)
    renders = []
    for label, background in (("sky", SKY), ("night", NIGHT)):
        with _preview_scene(placed, background) as scene, (contextlib.nullcontext() if lit else _lights_out()):
            scene.view_layers[0].update()
            shown = (SOLID, GLOW) if lit else (SOLID,)
            points = [p for item in placed if item.type == "MESH" for p in _material_points(item, shown)]
            renders.append(out_dir / f"{name}-{label}.png")
            _render(scene, points, Vector((0.35, -1.0, 0.28)), renders[-1], width, height, margin=1.04)
            print(f"props: line-up {renders[-1]}")
    return renders


# --------------------------------------------------------------------------
# Whole-repo drivers
# --------------------------------------------------------------------------


Builder = Callable[[MeshBuilder], dict]


def build_prop(spec: PropSpec, build: Builder, blend_dir: pathlib.Path, models_dir: pathlib.Path,
               preview_dir: pathlib.Path | None = None) -> ExportFacts:
    """Models one prop from code, saves its .blend, optionally renders it, and exports the FBX.

    `build` adds the prop's faces to the MeshBuilder it is handed and returns
    the head position of each child bone by part name.
    """
    reset_scene()
    builder = MeshBuilder(spec, Palette())
    assemble(builder, build(builder))
    blend = blend_dir / f"{spec.stem}.blend"
    save_blend(blend)
    # Rendered before validation, so a prop that fails can still be looked at.
    if preview_dir is not None:
        render_sheet(spec, preview_dir)
    return export_blend(spec, blend, models_dir)


def _arguments(description: str, *flags: tuple[str, dict]):
    import argparse
    import sys

    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description=description)
    for flag, options in flags:
        parser.add_argument(flag, **options)
    return parser.parse_args(argv)


def _run(jobs: Sequence[Callable[[], object]]) -> None:
    """Runs every job, then exits non-zero if any broke the contract.

    Blender exits 0 even when its script raises, so a build has to set the
    exit code itself for a failure to stop a shell script or CI.
    """
    failures = []
    for job in jobs:
        try:
            job()
        except ContractError as error:
            failures.append(error)
    for failure in failures:
        print(f"props: FAILED {failure.subject}")
        for line in failure.errors:
            print(f"props:   - {line}")
    if failures:
        raise SystemExit(1)


def main_build(props: Sequence[tuple[PropSpec, Builder]], blend_dir: pathlib.Path, models_dir: pathlib.Path) -> None:
    """Command line of a repo's build_props.py."""
    args = _arguments(
        "Builds this repo's props from code: saves the .blend sources and exports the FBX models.",
        ("--only", {"metavar": "STEM", "help": "build just this prop, e.g. flashlight"}),
        ("--previews", {"metavar": "DIR", "type": pathlib.Path, "help": "also render a preview sheet of each prop here"}),
    )
    chosen = [(spec, build) for spec, build in props if args.only in (None, spec.stem)]
    if not chosen:
        raise SystemExit(f"no prop named {args.only!r}; this repo has {[spec.stem for spec, _ in props]}")
    _run([lambda spec=spec, build=build: build_prop(spec, build, blend_dir, models_dir, args.previews)
          for spec, build in chosen])


def main_export(props: Sequence[tuple[PropSpec, Builder]], models_dir: pathlib.Path) -> None:
    """Command line of a repo's export.py."""
    args = _arguments(
        "Validates hand-edited .blend sources and exports them to the package's Models folder.",
        ("blends", {"metavar": "FILE.blend", "nargs": "+", "type": pathlib.Path}),
    )
    specs = {spec.stem: spec for spec, _ in props}
    unknown = [str(blend) for blend in args.blends if blend.stem not in specs]
    if unknown:
        raise SystemExit(f"not props of this repo: {unknown}; expected one of {sorted(specs)}")
    _run([lambda blend=blend: export_blend(specs[blend.stem], blend.resolve(), models_dir) for blend in args.blends])
