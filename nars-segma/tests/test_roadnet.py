"""Road network cleanup tests: dent removal and crossing noding.

`shapely` is required (the production passes are built on it), so the module is
skipped entirely when it is not installed."""

import math

import pytest

pytest.importorskip("shapely")

from shapely.geometry import LineString, Point

from app.roadnet import (
    METRES_PER_DEGREE_LAT,
    RoadNetOptions,
    metres_per_degree_lon,
    node_network,
    straighten,
)

# A mid-latitude tile: 1 deg longitude is ~90 km, so a test that wants a
# 20 m feature has to place it at ~2.2e-4 deg of longitude. Working in degrees
# with a realistic metres-per-degree factor is the whole point of the tolerance
# handling, so the fixtures stay in degrees too.
LAT = 36.0
MPD_LON = metres_per_degree_lon(LAT)


def _m_to_lon(metres: float) -> float:
    return metres / MPD_LON


def _vertex_angles(coords) -> list[float]:
    """Interior turn angles, in degrees, measured in metres."""
    points = [(lon * MPD_LON, lat * METRES_PER_DEGREE_LAT) for lon, lat in coords]
    out = []
    for i in range(1, len(points) - 1):
        (ax, ay), (bx, by), (cx, cy) = points[i - 1], points[i], points[i + 1]
        abx, aby = bx - ax, by - ay
        bcx, bcy = cx - bx, cy - by
        la = math.hypot(abx, aby)
        lb = math.hypot(bcx, bcy)
        if la < 1e-9 or lb < 1e-9:
            continue
        cos_a = (abx * bcx + aby * bcy) / (la * lb)
        out.append(math.degrees(math.acos(max(-1.0, min(1.0, cos_a)))))
    return out


def _deviation_from_chord(coords, index: int) -> float:
    """Perpendicular distance (m) of vertex `index` from its neighbours' chord."""
    (ax, ay), (bx, by), (cx, cy) = coords[index - 1], coords[index], coords[index + 1]
    bcx, bcy = cx - bx, cy - by
    lb = math.hypot(bcx, bcy)
    if lb < 1e-9:
        return 0.0
    return abs((bx - ax) * bcy - (by - ay) * bcx) / lb


def _dent_line() -> list[tuple[float, float]]:
    """A 300 m straight run with one 15 deg / 3 m dent in the middle."""
    pts = []
    for metres in (0.0, 100.0, 150.0, 200.0, 300.0):
        pts.append((_m_to_lon(metres), LAT))
    # push the middle vertex 3 m north of the straight chord
    return [
        pts[0],
        pts[1],
        (_m_to_lon(150.0), LAT + 3.0 / METRES_PER_DEGREE_LAT),
        *pts[3:],
    ]


def test_straighten_removes_shallow_dent():
    options = RoadNetOptions()
    coords = _dent_line()
    assert _vertex_angles(coords)[1] > 5.0  # the dent is real

    out = straighten(coords, mpd_lon=MPD_LON, options=options)

    assert len(out) < len(coords)
    assert _vertex_angles(out) == []


def test_straighten_keeps_endpoints_where_they_were():
    coords = _dent_line()
    out = straighten(coords, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert out[0] == pytest.approx(coords[0])
    assert out[-1] == pytest.approx(coords[-1])


def test_straighten_keeps_real_curvature():
    """A 15 deg bend is only a dent when the baseline is short. The same bend
    spread over 400 m is a curve, and the deviation cap must keep it."""
    coords = [
        (_m_to_lon(0.0), LAT),
        (_m_to_lon(200.0), LAT),
        (
            _m_to_lon(400.0),
            LAT + 400.0 * math.tan(math.radians(15.0)) / METRES_PER_DEGREE_LAT,
        ),
    ]
    out = straighten(coords, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert len(out) == 3


def test_straighten_stays_within_the_deviation_cap():
    coords = _dent_line()
    out = straighten(coords, mpd_lon=MPD_LON, options=RoadNetOptions())
    ratio = RoadNetOptions().max_deviation_ratio
    for i in range(1, len(out) - 1):
        (ax, ay), (bx, by), (cx, cy) = out[i - 1], out[i], out[i + 1]
        la = math.hypot(bx - ax, by - ay)
        lb = math.hypot(cx - bx, cy - by)
        assert _deviation_from_chord(out, i) <= ratio * min(la, lb) + 1e-6


def test_straighten_is_identity_when_disabled():
    coords = _dent_line()
    for options in (
        RoadNetOptions(max_angle_deg=0.0),
        RoadNetOptions(max_deviation_ratio=0.0),
    ):
        assert straighten(coords, mpd_lon=MPD_LON, options=options) == coords


def test_straighten_handles_two_point_and_degenerate_input():
    options = RoadNetOptions()
    assert straighten(
        [(0.0, LAT), (_m_to_lon(50.0), LAT)], mpd_lon=MPD_LON, options=options
    ) == [
        (0.0, LAT),
        (_m_to_lon(50.0), LAT),
    ]
    assert straighten([], mpd_lon=MPD_LON, options=options) == []


def test_straighten_removes_a_repeated_kink_run():
    """Removal repeats until stable: dropping one kink can make its neighbours
    shallow enough to drop too, and all of them must go in one call."""
    pts = [(_m_to_lon(m * 50.0), LAT) for m in range(6)]
    coords = [
        pts[0],
        (pts[1][0], LAT + 2.0 / METRES_PER_DEGREE_LAT),
        (pts[2][0], LAT),
        (pts[3][0], LAT - 2.0 / METRES_PER_DEGREE_LAT),
        (pts[4][0], LAT),
        pts[5],
    ]
    out = straighten(coords, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert _vertex_angles(out) == []


def _crossing_pair() -> list[LineString]:
    """Two roads that cross in a genuine X: neither has an endpoint there."""
    return [
        LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(200.0), LAT)]),
        LineString(
            [
                (_m_to_lon(100.0), LAT - 100.0 / METRES_PER_DEGREE_LAT),
                (_m_to_lon(100.0), LAT + 100.0 / METRES_PER_DEGREE_LAT),
            ]
        ),
    ]


def _shared_nodes(lines) -> set[tuple[float, float]]:
    """Distinct intersection points that are a vertex of *both* lines.

    Counted as distinct points, not as pairs: once a crossing is noded, all
    four arms of an X meet at one point, so every pair of arms intersects at
    that same point and a pair count would report six nodes for one junction.
    """
    nodes: set[tuple[float, float]] = set()
    for i, a in enumerate(lines):
        for b in lines[i + 1 :]:
            inter = a.intersection(b)
            if inter.is_empty or inter.geom_type != "Point":
                continue
            on_a = any(inter.distance(Point(c)) < 1e-6 for c in a.coords)
            on_b = any(inter.distance(Point(c)) < 1e-6 for c in b.coords)
            if on_a and on_b:
                nodes.add((round(inter.x, 9), round(inter.y, 9)))
    return nodes


def test_node_network_turns_a_crossing_into_a_shared_junction():
    lines = _crossing_pair()
    assert not _shared_nodes(lines)  # precondition: blind X crossing

    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())

    assert len(_shared_nodes([ln for group in grouped for ln in group])) == 1


def test_node_network_cuts_both_arms_at_the_crossing():
    grouped = node_network(_crossing_pair(), mpd_lon=MPD_LON, options=RoadNetOptions())
    pieces = [ln for group in grouped for ln in group]
    # both roads are cut in half: 2 parents -> 4 pieces
    assert [len(group) for group in grouped] == [2, 2]
    assert len(pieces) == 4
    total_before = sum(ln.length for ln in _crossing_pair())
    assert sum(ln.length for ln in pieces) == pytest.approx(total_before, rel=1e-6)


def test_node_network_returns_one_group_per_input_line():
    lines = _crossing_pair() + [
        # an isolated road, far away and sharing nothing
        LineString([(_m_to_lon(5000.0), LAT), (_m_to_lon(5200.0), LAT)]),
    ]
    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert len(grouped) == len(lines)
    # the isolated road is not fragmented and keeps its identity
    assert grouped[2] == [lines[2]]


def test_node_network_merges_a_multi_road_junction_into_one_node():
    """Three roads meeting at a point: pairwise intersections land on slightly
    different floating-point coordinates, so without tolerance merging the
    junction comes out as several near-identical nodes."""
    junction = (_m_to_lon(100.0), LAT)
    eps = 1e-9
    lines = [
        LineString([(junction[0] - 100.0 / MPD_LON, LAT), junction]),
        LineString(
            [
                (junction[0] + eps, LAT - 100.0 / METRES_PER_DEGREE_LAT),
                (junction[0], LAT + 100.0 / METRES_PER_DEGREE_LAT),
            ]
        ),
        LineString([junction, (junction[0] + 100.0 / MPD_LON, LAT)]),
    ]
    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())
    pieces = [ln for group in grouped for ln in group]
    assert len(_shared_nodes(pieces)) == 1


def test_node_network_handles_a_t_junction():
    """A road ending on the middle of another is already a valid network shape;
    the through-road must gain a vertex so the pair is topologically connected."""
    lines = [
        LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(200.0), LAT)]),
        LineString(
            [
                (_m_to_lon(100.0), LAT - 50.0 / METRES_PER_DEGREE_LAT),
                (_m_to_lon(100.0), LAT),
            ]
        ),
    ]
    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())
    pieces = [ln for group in grouped for ln in group]
    assert len(_shared_nodes(pieces)) == 1
    # the through-road is cut in two; the terminating road is not
    assert [len(group) for group in grouped] == [2, 1]


def test_node_network_nodes_a_self_crossing_road():
    """A road that crosses itself is the same defect as two roads crossing, and
    it needs no second line to be detectable. The figure-eight below is traced
    as one skeleton edge, so nothing else in the pipeline will ever split it."""
    loop = LineString(
        [
            (_m_to_lon(0.0), LAT - 100.0 / METRES_PER_DEGREE_LAT),
            (_m_to_lon(200.0), LAT + 100.0 / METRES_PER_DEGREE_LAT),
            (_m_to_lon(0.0), LAT + 100.0 / METRES_PER_DEGREE_LAT),
            (_m_to_lon(200.0), LAT - 100.0 / METRES_PER_DEGREE_LAT),
        ]
    )
    assert not loop.is_simple  # precondition: it really does cross itself

    grouped = node_network([loop], mpd_lon=MPD_LON, options=RoadNetOptions())

    assert len(grouped) == 1
    assert len(grouped[0]) > 1, "the crossing must have been cut"
    for piece in grouped[0]:
        assert piece.is_simple, "no piece may still cross itself"


def test_node_network_leaves_collinear_overlap_for_de_duplication():
    """Two centrelines traced down the same road overlap exactly. That is a
    duplicate to be de-duplicated, not a junction: cutting it at the overlap
    ends multiplies the overlap (measured on the commune's own drafts, 50
    overlaps became 1065) and yields fragments the API then deletes, so the
    angle gate must leave it alone.
    """
    lines = [
        LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(200.0), LAT)]),
        LineString([(_m_to_lon(100.0), LAT), (_m_to_lon(300.0), LAT)]),
    ]
    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())

    assert grouped == [[lines[0]], [lines[1]]]

    # only dropping the gate makes the pass cut them, which is the behaviour the
    # default exists to prevent
    ungated = RoadNetOptions(min_node_angle_deg=0.0)
    assert [len(g) for g in node_network(lines, mpd_lon=MPD_LON, options=ungated)] == [
        2,
        2,
    ]


def test_node_network_merges_crossings_closer_than_the_tolerance():
    """Two roads meeting a third a hand's width apart cross it at two distinct
    points. At a 1 m tolerance those must collapse to one node, otherwise the
    junction reaches the API as a zero-width sliver between two nodes 20 cm
    apart. The tolerance is the only difference between the two runs below."""
    lines = [LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(200.0), LAT)])]
    for offset_m in (0.0, 0.2):
        lines.append(
            LineString(
                [
                    (_m_to_lon(100.0 + offset_m), LAT - 100.0 / METRES_PER_DEGREE_LAT),
                    (_m_to_lon(100.0 + offset_m), LAT + 100.0 / METRES_PER_DEGREE_LAT),
                ]
            )
        )

    merged = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert len(_shared_nodes([ln for g in merged for ln in g])) == 1

    # a tolerance tighter than the 20 cm gap keeps the two crossings apart, so
    # the merge above is genuinely the tolerance's doing
    tight = node_network(
        lines, mpd_lon=MPD_LON, options=RoadNetOptions(node_tolerance_m=0.01)
    )
    assert len(_shared_nodes([ln for g in tight for ln in g])) == 2


def test_node_network_only_nodes_sharp_enough_crossings():
    """The angle gate is what keeps noding off the twin centrelines that make up
    ~99.6% of the commune's crossings. A genuine 90 deg junction must still be
    noded at the default gate."""
    junction = LineString(
        [
            (_m_to_lon(0.0), LAT - 100.0 / METRES_PER_DEGREE_LAT),
            (_m_to_lon(0.0), LAT + 100.0 / METRES_PER_DEGREE_LAT),
        ]
    )
    through = LineString([(_m_to_lon(-100.0), LAT), (_m_to_lon(100.0), LAT)])

    gated = node_network([junction, through], mpd_lon=MPD_LON, options=RoadNetOptions())
    assert len(_shared_nodes([ln for g in gated for ln in g])) == 1

    # a 10 deg graze crossing `through` at its midpoint is below the 20 deg
    # default and must be left alone
    rise = 100.0 * math.tan(math.radians(10.0))
    graze = LineString(
        [
            (_m_to_lon(-100.0), LAT - rise / METRES_PER_DEGREE_LAT),
            (_m_to_lon(100.0), LAT + rise / METRES_PER_DEGREE_LAT),
        ]
    )
    shallow = node_network([through, graze], mpd_lon=MPD_LON, options=RoadNetOptions())
    assert [len(g) for g in shallow] == [1, 1]

    # ...but the same graze is noded once the gate is opened
    opened = node_network(
        [through, graze],
        mpd_lon=MPD_LON,
        options=RoadNetOptions(min_node_angle_deg=0.0),
    )
    assert [len(g) for g in opened] == [2, 2]


def test_node_network_is_a_no_op_when_disabled():
    lines = _crossing_pair()
    for options in (RoadNetOptions(node_tolerance_m=0.0),):
        grouped = node_network(lines, mpd_lon=MPD_LON, options=options)
        assert grouped == [[lines[0]], [lines[1]]]


def test_node_network_leaves_disjoint_lines_alone():
    lines = [
        LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(100.0), LAT)]),
        LineString([(_m_to_lon(0.0), LAT + 1.0), (_m_to_lon(100.0), LAT + 1.0)]),
    ]
    grouped = node_network(lines, mpd_lon=MPD_LON, options=RoadNetOptions())
    assert grouped == [[lines[0]], [lines[1]]]


def test_node_network_passes_through_degenerate_inputs():
    empty: list[LineString] = []
    assert node_network(empty, mpd_lon=MPD_LON, options=RoadNetOptions()) == []

    # a single line with nothing to cross it is returned as-is
    only = [LineString([(_m_to_lon(0.0), LAT), (_m_to_lon(100.0), LAT)])]
    assert node_network(only, mpd_lon=MPD_LON, options=RoadNetOptions()) == [only]


def test_straighten_then_node_keeps_the_junction_intact():
    """The production order is straighten-then-node. Noding introduces shared
    vertices; if straightening ran afterwards it could drop the node from one
    arm of the junction and not the other, tearing the network apart."""
    dented = _dent_line()
    crossing = LineString(
        [
            (_m_to_lon(150.0), LAT - 100.0 / METRES_PER_DEGREE_LAT),
            (_m_to_lon(150.0), LAT + 100.0 / METRES_PER_DEGREE_LAT),
        ]
    )
    options = RoadNetOptions()

    straight = LineString(straighten(dented, mpd_lon=MPD_LON, options=options))
    grouped = node_network([straight, crossing], mpd_lon=MPD_LON, options=options)
    pieces = [ln for group in grouped for ln in group]

    assert len(_shared_nodes(pieces)) == 1
    assert _vertex_angles(list(straight.coords)) == []


def test_metres_per_degree_lon_is_bounded_at_the_poles():
    # cos(90 deg) is ~0 in floating point; the factor must not collapse to 0 or
    # every distance test would report "infinitely far apart".
    assert metres_per_degree_lon(90.0) > 0.0
    assert metres_per_degree_lon(0.0) == pytest.approx(METRES_PER_DEGREE_LAT)
