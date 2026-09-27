"""
Vector network cleanup for extracted road centerlines.

Two passes, applied after `mask_to_linestrings` has turned the probability
mask into one LineString per skeleton-graph edge:

1. :func:`straighten` - removes "dents": interior vertices that kink by a few
   degrees over a short baseline. The imagery this service consumes is coarse
   (~20 m/px), so a skeletonized centerline traces the *edge* of the road
   ribbon and wobbles by several metres between neighbouring pixels.
   Douglas-Peucker cannot remove that wobble: the deviation is larger than the
   simplify tolerance, so the kink is real geometry as far as DP is concerned.
   An angle test is the right criterion, capped by how far the shortcut may
   stray from the traced centerline so genuine curves survive.

2. :func:`node_network` - turns every geometric crossing into a shared vertex,
   so two roads that cross meet at a node instead of blindly passing through
   each other. Skeleton edges are independent LineStrings, so a crossing that
   is not an sknw junction node leaves two lines that intersect mid-segment
   with no node in common. Splitting both at a common point is what makes the
   output a network rather than a pile of independent strokes.

Both passes work in degrees (GeoJSON coordinates) but measure in metres, via a
metres-per-degree factor supplied by the caller, because a tolerance that is
meaningful in metres is meaningless in degrees.

Splitting deliberately avoids GEOS: `shapely.ops.split` silently returns the
input unsplit for short segments at large coordinate magnitudes (these lines
are ~1e-5 deg long at ~36 deg latitude), so the cut points are located and the
pieces assembled directly.
"""

from __future__ import annotations

import logging
import math
from bisect import bisect_left
from dataclasses import dataclass
from itertools import pairwise

from shapely.errors import GEOSException
from shapely.geometry import LineString, Point
from shapely.strtree import STRtree

__all__ = [
    "METRES_PER_DEGREE_LAT",
    "RoadNetOptions",
    "metres_per_degree_lon",
    "node_network",
    "straighten",
]

logger = logging.getLogger("nars-segma.roadnet")

# Length of one degree of latitude in metres (spherical earth). Longitude
# scales by cos(latitude) and is derived per call.
METRES_PER_DEGREE_LAT = 111_320.0

# Guard against a degenerate metric: at the poles cos(latitude) -> 0 would make
# the longitude scale vanish and every distance test collapse to "far apart".
_MIN_COS_LAT = 1e-6


def metres_per_degree_lon(lat: float) -> float:
    """Metres per degree of longitude at `lat`, floored away from the poles."""
    return max(METRES_PER_DEGREE_LAT * math.cos(math.radians(lat)), _MIN_COS_LAT)


@dataclass(frozen=True)
class RoadNetOptions:
    """Tuning for :func:`straighten` and :func:`node_network`.

    `max_angle_deg` / `max_deviation_ratio` are the straighten pass (0 disables
    it); `node_tolerance_m` is the noding pass (0 disables it). Defaults come
    from tuning against real tiles: 20 deg with a 0.30 deviation cap removed
    ~95% of the curvature sign flips (the visible zig-zag) while holding the
    straightened centerline within ~14 m of the traced one, and 1 m of noding
    tolerance was enough to make every crossing a shared vertex.

    `min_node_angle_deg` is the gate that keeps noding away from shallow grazes.
    Measured on the commune's own road drafts, ~99.6% of all line-line crossings
    meet at under 10 deg: they are not roads meeting, they are twin centrelines
    of the same road traced a few metres apart. Cutting those is actively
    harmful - it multiplies collinear overlaps (50 -> 1065 on the commune's
    drafts) and shatters the network into sub-`MinRoadLengthM` fragments that the
    API then deletes. Only crossings sharp enough to be a real junction are
    noded.
    """

    max_angle_deg: float = 20.0
    max_deviation_ratio: float = 0.30
    max_passes: int = 8
    node_tolerance_m: float = 1.0
    min_node_angle_deg: float = 20.0

    @property
    def straighten_enabled(self) -> bool:
        return self.max_angle_deg > 0.0 and self.max_deviation_ratio > 0.0

    @property
    def node_enabled(self) -> bool:
        return self.node_tolerance_m > 0.0


def _to_metres(
    coords: list[tuple[float, float]], mpd_lon: float
) -> list[tuple[float, float]]:
    return [(lon * mpd_lon, lat * METRES_PER_DEGREE_LAT) for lon, lat in coords]


def straighten(
    coords: list[tuple[float, float]],
    *,
    mpd_lon: float,
    options: RoadNetOptions,
) -> list[tuple[float, float]]:
    """Drop shallow-kink interior vertices from one centerline.

    A vertex is a dent - and is removed - when it bends the path by less than
    `options.max_angle_deg` *and* the shortcut stays within
    `options.max_deviation_ratio` of the shorter neighbouring segment. The
    second condition is what keeps real curvature: a 20 deg bend spread over a
    200 m baseline is a curve worth keeping, the same bend over a 15 m baseline
    is pixel noise.

    Endpoints are never touched, so the line keeps meeting the network where it
    originally did. Removal repeats until a pass changes nothing, because
    dropping one vertex can make its neighbours shallow enough to drop too.
    """
    if not options.straighten_enabled or len(coords) < 3:
        return list(coords)

    points = _to_metres(coords, mpd_lon)
    for _ in range(max(options.max_passes, 1)):
        removed = False
        i = 1
        while i < len(points) - 1:
            (ax, ay), (bx, by), (cx, cy) = points[i - 1], points[i], points[i + 1]
            abx, aby = bx - ax, by - ay
            bcx, bcy = cx - bx, cy - by
            len_ab = math.hypot(abx, aby)
            len_bc = math.hypot(bcx, bcy)
            if len_ab > 1e-9 and len_bc > 1e-9:
                cos_a = (abx * bcx + aby * bcy) / (len_ab * len_bc)
                angle = math.degrees(math.acos(max(-1.0, min(1.0, cos_a))))
                # Perpendicular distance from the middle vertex to the chord
                # joining its neighbours, via the triangle area.
                deviation = abs(abx * bcy - aby * bcx) / len_bc
                if (
                    angle < options.max_angle_deg
                    and deviation <= options.max_deviation_ratio * min(len_ab, len_bc)
                ):
                    del points[i]
                    removed = True
                    continue
            i += 1
        if not removed:
            break

    return [(x / mpd_lon, y / METRES_PER_DEGREE_LAT) for x, y in points]


def _cumulative_lengths(coords: list[tuple[float, float]]) -> list[float]:
    lengths = [0.0]
    for i in range(1, len(coords)):
        lengths.append(lengths[-1] + math.dist(coords[i - 1], coords[i]))
    return lengths


def _direction_at(
    coords: list[tuple[float, float]], lengths: list[float], distance: float
) -> tuple[float, float] | None:
    """Unit direction of the segment containing `distance` along the line."""
    for i in range(len(coords) - 1):
        span = lengths[i + 1] - lengths[i]
        if distance < lengths[i] or distance > lengths[i + 1] or span <= 0.0:
            continue
        dx = coords[i + 1][0] - coords[i][0]
        dy = coords[i + 1][1] - coords[i][1]
        norm = math.hypot(dx, dy)
        if norm > 0.0:
            return dx / norm, dy / norm
    return None


def _crossing_angle_deg(
    direction_a: tuple[float, float] | None,
    direction_b: tuple[float, float] | None,
) -> float:
    """Angle between two road directions at a crossing, in 0..90 degrees.

    The absolute value of the dot product makes the result independent of which
    way each line happens to be stored. An undeterminable direction reports 90
    degrees so the :func:`node_network` gate never silently discards a real
    crossing on incomplete input.
    """
    if direction_a is None or direction_b is None:
        return 90.0
    dot = abs(direction_a[0] * direction_b[0] + direction_a[1] * direction_b[1])
    return math.degrees(math.acos(max(-1.0, min(1.0, dot))))


def _split_line(
    line: LineString, cuts: list[tuple[float, Point | None]]
) -> list[LineString]:
    """Cut `line` at the given along-line distances.

    Each cut is a `(distance, node)` pair. `node` is the shared junction
    coordinate both sides of the cut must end on, which is the representative of
    the merged node group - see :func:`node_network`. It is passed in rather than
    recomputed from `line.interpolate` because the representative is not always
    exactly on this particular line (see the tolerance note there), and both
    pieces of a cut have to end on the *same* coordinate or the junction is not
    shared after all.

    Hand-rolled rather than `shapely.ops.split`: GEOS's line splitter returns
    its input untouched for the very short segments this service produces
    (~1e-5 deg at ~36 deg latitude), which would silently leave every crossing
    un-noded.
    """
    coords = list(line.coords)
    lengths = _cumulative_lengths(coords)
    total = lengths[-1]
    # The two sentinels close the first and last piece; a real cut carries the
    # node it must end on, a sentinel carries None and is interpolated.
    edges: list[tuple[float, Point | None]] = [
        (0.0, None),
        *sorted(cuts),
        (total, None),
    ]
    pieces: list[LineString] = []
    for (start, start_node), (end, end_node) in pairwise(edges):
        if end - start <= 0.0:
            continue
        points = [start_node or line.interpolate(start)]
        # Original vertices strictly between the two cuts, found by binary
        # search on the precomputed cumulative lengths (O(log n) each) instead
        # of projecting every vertex onto the line.
        lo = max(bisect_left(lengths, start), 1)
        hi = min(bisect_left(lengths, end), len(coords) - 1)
        points.extend(Point(coords[v]) for v in range(lo, hi))
        points.append(end_node or line.interpolate(end))
        if len(points) >= 2:
            pieces.append(LineString(points))
    return pieces


def node_network(
    lines: list[LineString],
    *,
    mpd_lon: float,
    options: RoadNetOptions,
) -> list[list[LineString]]:
    """Make every crossing between `lines` a shared vertex.

    Returns one group per input line, in input order, holding the pieces that
    line was cut into - so a caller that carries per-line payload (a skeleton
    pixel set, say) can zip it back onto the right parent. A line with nothing
    to cut yields a single-element group holding the original object, so an
    un-crossed road is never needlessly fragmented.

    Each distinct crossing point is collected once, then projected onto every
    line it belongs to and used as a cut. Points within `node_tolerance_m` of
    each other are merged into one representative first: at a junction where
    three or more roads meet, each pair intersects at a slightly different
    floating-point location, and without merging them the junction would come
    out as several nodes a few centimetres apart.

    The cut is then made *at* that representative rather than at the
    representative's projection onto each line. Re-projecting would undo the
    merge: two roads 20 cm apart both crossing a third would be cut 20 cm apart
    again, leaving the very sliver the tolerance exists to remove. Snapping moves
    a road by at most the tolerance (1 m by default, well under one 20 m pixel)
    and is what makes the junction a single shared coordinate.
    """
    single: list[list[LineString]] = [[line] for line in lines]
    if not options.node_enabled:
        return single

    tolerance_deg_lon = options.node_tolerance_m / mpd_lon
    tolerance_deg_lat = options.node_tolerance_m / METRES_PER_DEGREE_LAT

    # 1. Crossing points, tagged with the lines they belong to.
    nodes: dict[tuple[float, float], list[float]] = {}
    per_line: dict[int, set[tuple[float, float]]] = {}
    all_coords = [list(line.coords) for line in lines]
    all_lengths = [_cumulative_lengths(coords) for coords in all_coords]
    tree = STRtree(lines)
    for i, line in enumerate(lines):
        for j in tree.query(line):
            j = int(j)
            # Each unordered pair once, but keep j == i: a road that crosses
            # *itself* is a genuine defect of the same kind, and skipping the
            # self-comparison would leave the loop visibly crossing forever.
            if j < i:
                continue
            other = lines[j]
            try:
                inter = line.intersection(other)
            except GEOSException:
                logger.info("Skipping un-intersectable road pair", exc_info=True)
                continue
            if inter.is_empty:
                continue
            parts = list(inter.geoms) if hasattr(inter, "geoms") else [inter]
            for part in parts:
                if part.geom_type == "Point":
                    probe = part
                    hits = [(i, part), (j, part)]
                elif part.geom_type == "LineString":
                    # A collinear overlap is a LineString, not a Point. Its
                    # direction is parallel by construction, so the angle gate
                    # below rejects it: overlapping centrelines are duplicates
                    # to be de-duplicated, not a junction to be cut.
                    ends = (Point(part.coords[0]), Point(part.coords[-1]))
                    probe = ends[0]
                    hits = [(i, ends[0]), (i, ends[1]), (j, ends[0]), (j, ends[1])]
                else:
                    continue
                # A self-intersection is exempt from the gate: a line measured
                # against itself is always parallel, so the gate would discard
                # every self-crossing, which is precisely a defect worth cutting.
                if options.min_node_angle_deg > 0.0 and j != i:
                    try:
                        angle = _crossing_angle_deg(
                            _direction_at(
                                all_coords[i], all_lengths[i], line.project(probe)
                            ),
                            _direction_at(
                                all_coords[j], all_lengths[j], other.project(probe)
                            ),
                        )
                    except GEOSException:
                        logger.info("Skipping un-projectable road pair", exc_info=True)
                        continue
                    if angle < options.min_node_angle_deg:
                        continue
                for line_index, point in hits:
                    key = (round(point.x, 9), round(point.y, 9))
                    nodes.setdefault(key, [point.x, point.y])
                    per_line.setdefault(line_index, set()).add(key)

    if not nodes:
        return single

    # 2. Merge crossing points that are within tolerance of each other.
    parent = {key: key for key in nodes}

    def find(key):
        while parent[key] != key:
            parent[key] = parent[parent[key]]
            key = parent[key]
        return key

    grid = 1.0 / max(tolerance_deg_lon, 1e-12)
    cells: dict[tuple[int, int], list[tuple[float, float]]] = {}
    for key in nodes:
        cell = (int(key[0] * grid), int(key[1] * grid))
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for neighbour in cells.get((cell[0] + dx, cell[1] + dy), ()):
                    other = nodes[neighbour]
                    if (
                        abs(key[0] - other[0]) <= tolerance_deg_lon
                        and abs(key[1] - other[1]) <= tolerance_deg_lat
                    ):
                        root_a, root_b = find(key), find(neighbour)
                        if root_a != root_b:
                            parent[root_b] = root_a
        cells.setdefault(cell, []).append(key)

    sums: dict[tuple[float, float], list[float]] = {}
    for key, (x, y) in nodes.items():
        entry = sums.setdefault(find(key), [0.0, 0.0, 0.0])
        entry[0] += x
        entry[1] += y
        entry[2] += 1.0
    representative = {root: (s[0] / s[2], s[1] / s[2]) for root, s in sums.items()}

    # 3. Cut each line at its nodes.
    grouped: list[list[LineString]] = []
    for i, line in enumerate(lines):
        keys = per_line.get(i)
        if not keys:
            grouped.append([line])
            continue
        total = _cumulative_lengths(list(line.coords))[-1]
        cuts: dict[float, Point] = {}
        for key in keys:
            x, y = representative[find(key)]
            node = Point(x, y)
            try:
                distance = line.project(node)
            except GEOSException:
                continue
            # Only a cut strictly inside the line changes anything; a node that
            # is already an endpoint is a junction the skeleton already resolved.
            if distance is not None and 1e-12 < distance < total - 1e-12:
                cuts[round(distance, 9)] = node
        pieces = _split_line(line, sorted(cuts.items())) if cuts else []
        grouped.append(pieces or [line])
    return grouped
