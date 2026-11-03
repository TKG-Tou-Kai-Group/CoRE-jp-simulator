#!/usr/bin/env python3
"""CoRE-1 2027 フィールドを、公式ルールナビの簡略 3D モデルからゲーム用に取り込む。

入力は core.scramble-robot.org/rule/core-1-rulenavi/ に埋め込まれている `window.F3D`
(フィールド CAD から作られた簡略メッシュ。頂点は uint16 ×3 を scale で割り off を引いた mm、
面は uint16 のインデックス)。tools/data/core1_2027_field_f3d.json に保存してある。

出力:
  Assets/Resources/Field2027/<zone>_<material>.stl   ROS 慣習 (x 前, y 左, z 上, m) のバイナリ STL。
                                                   StlImporter が Unity 座標へ直す
  Assets/Resources/Field2027/field2027.json          色、当たり判定の有無、陣地のダメージパネル、
                                                   バンカー上面、スタート/補給/進入禁止ゾーン、開始位置、
                                                   ブロック・コンテナの初期位置、スポット・ビルドサイト・
                                                   防御ポート・高台の谷部の置き場所 (ビルダー用)

座標: F3D は x 0..24000 (長辺)、y 上、z 0..15000。フィールド中央を原点にして
Unity = ((x-12000)/1000, y/1000, -(z-7500)/1000)。赤スタートゾーンが Unity の (-x, -z) 側、
青が (+x, +z) 側になる (ルールナビの上面図で左下が赤、右上が青)。
"""
import argparse
import base64
import json
import os
import struct

import numpy as np

PALETTE = {  # ルールナビの light テーマの色
    "floor": "#c3c8d2", "strider": "#d4d9e1", "line_red": "#d7263d", "line_blue": "#2356cf",
    "line_yel": "#e0a800", "zone_red": "#e7a3ad", "zone_blue": "#a7b9e6", "alu": "#b8bfc8",
    "glass": "#cfdbee", "plate": "#eef0f3", "black": "#2d3139", "dark": "#5b606b", "red": "#d7263d",
    "blue": "#2356cf", "white": "#f7f7f7", "cbody": "#e6e9ee", "wood": "#e3c48f", "dmg": "#575b61",
    "obst": "#e3d6b8", "noentry": "#efe6d3", "fence": "#c8b28a",
}
# 床の線やゾーン塗りは当たり判定を持たせない (床は別のプレーンが受け持つ)
NO_COLLIDER = {("battle", "line_red"), ("battle", "line_blue"), ("battle", "line_yel"), ("battle", "plate"),
               ("base", "dmg"),   # ダメージパネルは BasePanel の当たり判定が受け持つ
               ("supply", "floor"), ("supply", "zone_red"), ("supply", "zone_blue"),
               ("strider", "strider"), ("strider", "red"), ("strider", "blue"), ("noentry", "noentry")}
CENTER = np.array([12000.0, 0.0, 7500.0])
# ブロック (wood) はラウンド中に動かせる物体なので、固定メッシュには含めず位置だけ書き出す
DYNAMIC = {("blockzone", "wood"), ("highland", "wood")}
BLOCK = (300.0, 200.0, 300.0)       # 4.4 幅 x 高さ x 奥行き [mm]
CONTAINER = 150.0                   # 4.3 一辺 [mm]


def to_unity(p):
    p = np.asarray(p, dtype=float)
    return np.array([(p[0] - CENTER[0]) / 1000.0, p[1] / 1000.0, -(p[2] - CENTER[2]) / 1000.0])


def to_ros(points):
    """F3D(mm) → ROS(m)。Unity = (-ros.y, ros.z, ros.x) なので ros = (unity.z, -unity.x, unity.y)。回転なので巻き順は保つ。"""
    u = np.stack([(points[:, 0] - CENTER[0]) / 1000.0, points[:, 1] / 1000.0, -(points[:, 2] - CENTER[2]) / 1000.0], axis=1)
    return np.stack([u[:, 2], -u[:, 0], u[:, 1]], axis=1)


def decode(group, scale, off):
    v = np.frombuffer(base64.b64decode(group["p"]), dtype="<u2").astype(float).reshape(-1, 3) / scale - off
    f = np.frombuffer(base64.b64decode(group["i"]), dtype="<u2").astype(int).reshape(-1, 3)
    return v, f


def write_stl(path, tris):
    tris = tris.astype(np.float32)
    n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    ln = np.linalg.norm(n, axis=1, keepdims=True)
    n = np.where(ln > 0, n / np.maximum(ln, 1e-12), 0).astype(np.float32)
    dt = np.dtype([("n", "<f4", 3), ("v", "<f4", (3, 3)), ("a", "<u2")])
    out = np.zeros(len(tris), dtype=dt)
    out["n"] = n
    out["v"] = tris
    with open(path, "wb") as fp:
        fp.write(b"CoRE-1 2027 field from rule navigator F3D".ljust(80, b"\0"))
        fp.write(struct.pack("<I", len(out)))
        fp.write(out.tobytes())


def clusters(v, f, merge_mm=50.0):
    """頂点を共有する三角形をまとめる。近い頂点 (同じ位置) も同一視する。"""
    parent = list(range(len(v)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        parent[find(a)] = find(b)

    key = {}
    for i, p in enumerate(v):
        k = tuple(np.round(p / 2.0))
        if k in key:
            union(i, key[k])
        else:
            key[k] = i
    for t in f:
        union(t[0], t[1])
        union(t[1], t[2])
    groups = {}
    for t in f:
        groups.setdefault(find(t[0]), []).append(t)
    out = []
    for tris in groups.values():
        tris = np.array(tris)
        pts = v[np.unique(tris.ravel())]
        out.append({"tris": tris, "min": pts.min(0), "max": pts.max(0), "center": pts.mean(0)})
    # 隣り合う塊 (別の頂点だが接している) を結合
    merged = True
    while merged:
        merged = False
        for i in range(len(out)):
            for j in range(i + 1, len(out)):
                a, b = out[i], out[j]
                if np.all(a["min"] <= b["max"] + merge_mm) and np.all(b["min"] <= a["max"] + merge_mm):
                    tris = np.vstack([a["tris"], b["tris"]])
                    pts = v[np.unique(tris.ravel())]
                    out[i] = {"tris": tris, "min": pts.min(0), "max": pts.max(0), "center": pts.mean(0)}
                    del out[j]
                    merged = True
                    break
            if merged:
                break
    return out


def convex_hull(points):
    """2D の凸包 (Andrew のモノトーンチェーン)。"""
    pts = sorted(set((float(x), float(y)) for x, y in points))
    if len(pts) <= 2:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def oriented_box(poly):
    """凸多角形を囲む、最長辺に沿った長方形 (中心, Unity の Y 回転 [deg], 長さ, 幅)。"""
    best = None
    n = len(poly)
    for i in range(n):
        x0, z0 = poly[i]
        x1, z1 = poly[(i + 1) % n]
        ex, ez = x1 - x0, z1 - z0
        el = (ex * ex + ez * ez) ** 0.5
        if el < 1e-6:
            continue
        ux, uz = ex / el, ez / el           # 辺の向き
        vx, vz = -uz, ux                    # 直交
        us = [px * ux + pz * uz for px, pz in poly]
        vs = [px * vx + pz * vz for px, pz in poly]
        area = (max(us) - min(us)) * (max(vs) - min(vs))
        if best is None or area < best[0]:
            cu, cv = (max(us) + min(us)) / 2, (max(vs) + min(vs)) / 2
            cx, cz = cu * ux + cv * vx, cu * uz + cv * vz
            length, width = max(us) - min(us), max(vs) - min(vs)
            # Unity: forward (+z) を長さ方向にする回転。yaw = atan2(ux, uz)
            import math
            yaw = math.degrees(math.atan2(ux, uz))
            best = (area, cx, cz, yaw, length, width)
    _, cx, cz, yaw, length, width = best
    return cx, cz, yaw, length, width


def unity_yaw(dx_f3d, dz_f3d):
    """F3D の xz 平面の向きベクトルを Unity の Y 回転 [deg] にする (Unity は z が反転)。
    返す値は箱の辺の向きなので、90 度の整数倍は同一視して -45..45 に収める。"""
    import math
    ux, uz = dx_f3d, -dz_f3d
    yaw = math.degrees(math.atan2(ux, uz))
    while yaw > 45.0:
        yaw -= 90.0
    while yaw <= -45.0:
        yaw += 90.0
    return yaw


def top_quads(v, f, y, min_size=100.0, by_cluster=False):
    """高さ y [mm] にある水平な面を、箱ごとに分けて (中心, 辺の向き, 幅, 奥行き) で返す。
    巻き順はグループごとに違うので法線の上下は問わない。隣り合う箱の面は頂点を共有することが
    あるので、既定では三角形の重心が近いものだけをまとめる (1 箱 = 2 三角形のとき)。
    by_cluster のときは頂点を共有する面をまとめる (棚板のように多くの三角形でできた面)。"""
    tris = []
    for t in f:
        a, b, c = v[t]
        n = np.cross(b - a, c - a)
        l = np.linalg.norm(n)
        if l > 0 and abs(n[1]) / l > 0.99 and abs(a[1] - y) < 2 and abs(b[1] - y) < 2 and abs(c[1] - y) < 2:
            tris.append(t)
    groups = []
    if by_cluster:
        for c in clusters(v, np.array(tris), merge_mm=5.0):
            groups.append(v[np.unique(c["tris"].ravel())])
    else:
        used = [False] * len(tris)
        cents = [v[t].mean(0) for t in tris]
        for i, t in enumerate(tris):
            if used[i]:
                continue
            group = [i]
            used[i] = True
            for j in range(i + 1, len(tris)):
                if not used[j] and np.linalg.norm(cents[i] - cents[j]) < 0.55 * min_size:
                    group.append(j)
                    used[j] = True
            groups.append(v[np.unique(np.array([tris[k] for k in group]).ravel())])
    out = []
    for pts in groups:
        xz = [(float(p[0]), float(p[2])) for p in pts]
        hull = convex_hull(xz)
        if len(hull) < 3:
            continue
        cx, cz, _, length, width = oriented_box(hull)
        if max(length, width) < min_size * 0.6:
            continue
        best = None
        for k in range(len(hull)):
            x0, z0 = hull[k]
            x1, z1 = hull[(k + 1) % len(hull)]
            el = ((x1 - x0) ** 2 + (z1 - z0) ** 2) ** 0.5
            if best is None or el > best[0]:
                best = (el, x1 - x0, z1 - z0)
        out.append({"center": np.array([cx, y, cz]), "dir": (best[1], best[2]), "length": length, "width": width})
    return out


def _dir_from_rot(rot_deg):
    """cont.at の向き (F3D の y 軸まわり [deg]) を xz の向きベクトルにする。"""
    import math
    r = math.radians(float(rot_deg))
    return (math.cos(r), math.sin(r))


def prop_entry(kind, side, center_f3d, yaw, extra=None):
    u = to_unity(center_f3d)
    d = {"kind": kind, "side": side, "x": round(float(u[0]), 4), "y": round(float(u[1]), 4), "z": round(float(u[2]), 4), "yawDeg": round(float(yaw), 2)}
    if extra:
        d.update(extra)
    return d


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=os.path.join(os.path.dirname(__file__), "data", "core1_2027_field_f3d.json"))
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Field2027"))
    args = ap.parse_args()
    d = json.load(open(args.src))
    scale, off = d["scale"], np.array(d["off"])
    os.makedirs(args.out, exist_ok=True)

    # Unity の JsonUtility で読むので、辞書と入れ子のリストは使わない (配列と平らなオブジェクトだけ)。
    manifest = {"source": "CoRE-1 Rule Navigator (Rulebook V27.1.3) embedded field model, converted by tools/import_field2027.py",
                "fieldWidth": 24.0, "fieldDepth": 15.0, "groups": [],
                "palette": [{"name": k, "hex": v} for k, v in PALETTE.items()],
                "basePanels": [], "bunkerTops": [], "startZones": [], "supplyZones": [], "noEntryZones": [], "spawns": [],
                "props": [], "slots": [], "carriers": []}

    for g in d["groups"]:
        v, f = decode(g, scale, off)
        name = f"{g['z']}_{g['m']}"
        if (g["z"], g["m"]) in DYNAMIC:
            # ブロック: 上面 (200 mm) の四角形ごとに 1 個。ブロックゾーンの 12 個 x 2 と高台の谷部の 2 個 x 2
            for q in top_quads(v, f, BLOCK[1], min_size=BLOCK[0]):
                c = q["center"]
                side = "red" if c[0] < 12000 else "blue"
                place = "valley" if g["z"] == "highland" else "zone"
                if place == "valley":
                    side = "red" if c[2] > 7500 else "blue"   # 赤サイド側 (z 大) の高台 / 青サイド側
                manifest["props"].append(prop_entry("block", side, [c[0], 0.0, c[2]], unity_yaw(*q["dir"]), {"origin": place}))
                if place == "valley":
                    n_valley = sum(1 for sl in manifest["slots"] if sl["kind"] == "valley" and sl["side"] == side)
                    manifest["slots"].append(prop_entry("valley", side, [c[0], 0.0, c[2]], unity_yaw(*q["dir"]), {"index": n_valley + 1, "levels": 1}))
            stale = os.path.join(args.out, name + ".stl")
            if os.path.exists(stale):
                os.remove(stale)
            if g["z"] == "highland":
                # 谷部の置き場所を溝に沿って壁から遠い側へ 2 つ足す (開始時は空)。2 個 (0.6 m) だけでは通路が狭く、
                # ブロックゾーンのブロックを足して広げたいため (7.3 は個数を限っていない)
                for side in ("red", "blue"):
                    vs = sorted((sl for sl in manifest["slots"] if sl["kind"] == "valley" and sl["side"] == side), key=lambda sl: sl["index"])
                    if len(vs) != 2:
                        continue
                    dx, dz = vs[1]["x"] - vs[0]["x"], vs[1]["z"] - vs[0]["z"]
                    for k in (1, 2):
                        extra = dict(vs[1])
                        extra["x"] = round(vs[1]["x"] + dx * k, 4)
                        extra["z"] = round(vs[1]["z"] + dz * k, 4)
                        extra["index"] = 2 + k
                        manifest["slots"].append(extra)
            continue
        write_stl(os.path.join(args.out, name + ".stl"), to_ros(v)[f])
        manifest["groups"].append({"file": name, "zone": g["z"], "material": g["m"], "triangles": int(len(f)),
                                   "collider": (g["z"], g["m"]) not in NO_COLLIDER})

        if g["z"] == "blockzone" and g["m"] == "alu":
            # ブロックキャリアー (7.4): 700 x 800 x 88 の台。各ブロックゾーンに 1 台
            for c in clusters(v, f, merge_mm=30.0):
                size = c["max"] - c["min"]
                if size[0] < 500 or size[2] < 500:
                    continue
                center = c["center"]
                side = "red" if center[0] < 12000 else "blue"
                manifest["carriers"].append(prop_entry("carrier", side, [center[0], 0.0, center[2]], 0.0,
                                                       {"width": round(float(size[0]) / 1000, 3), "depth": round(float(size[2]) / 1000, 3),
                                                        "height": round(float(c["max"][1]) / 1000, 3)}))
        if g["z"] == "buildsite" and g["m"] == "plate":
            # ビルドサイト (3.5): 透明な箱 (50..470 mm) の位置が 2 個積む場所。床は 50 mm の板の上
            sites = []
            for c in clusters(v, f, merge_mm=5.0):
                size = c["max"] - c["min"]
                if size[0] < 300 or size[2] < 300:
                    continue
                sites.append(c)
            for side, sx in (("red", lambda c: c["center"][0] < 12000), ("blue", lambda c: c["center"][0] >= 12000)):
                mine = [c for c in sites if sx(c)]
                # 向き: 4 つの中心が並ぶ向き。番号は「正面から見て左から」だが向きの定義が無いので、
                # 自同盟のスタートゾーンに近い方を 1 とする (仮定。docs/TOURNAMENT_RULES.md)
                mine.sort(key=lambda c: -c["center"][2] if side == "red" else c["center"][2])
                if len(mine) >= 2:
                    d0 = mine[-1]["center"] - mine[0]["center"]
                    yaw = unity_yaw(d0[0], d0[2])
                else:
                    yaw = 0.0
                for i, c in enumerate(mine):
                    cc = c["center"]
                    manifest["slots"].append(prop_entry("site", side, [cc[0], float(c["min"][1]), cc[2]], yaw, {"index": i + 1, "levels": 2}))
        if g["z"] == "spot" and g["m"] == "plate":
            # スポット (3.4): 3 段の棚板 (300 / 575 / 850 mm)。棚板を長さ方向に 6 等分して各列の中心を置き場所にする
            for y, level in ((300, 0), (575, 1), (850, 2)):
                for q in top_quads(v, f, y, min_size=800.0, by_cluster=True):
                    c = q["center"]
                    side = "red" if c[0] < 12000 else "blue"
                    dx, dz = q["dir"]
                    el = (dx * dx + dz * dz) ** 0.5
                    ux, uz = dx / el, dz / el
                    # 列 1 は自同盟のスタートゾーン側 (赤: z 大、青: z 小) から数える (仮定)
                    if (uz > 0) != (side == "red"):
                        ux, uz = -ux, -uz
                    for col in range(6):
                        off_ = (col - 2.5) * q["length"] / 6.0
                        px, pz = c[0] + ux * off_, c[2] + uz * off_
                        index = level * 6 + col + 1
                        manifest["slots"].append(prop_entry("spot", side, [px, float(y), pz], unity_yaw(dx, dz),
                                                            {"index": index, "levels": 1, "post": index in (1, 5, 8, 12, 15, 16)}))
        if g["z"] == "base" and g["m"] == "white":
            # 防御ポート (3.7): 陣地のフィールド中央側にある 204 mm 角の白い印 2 つ
            for c in clusters(v, f, merge_mm=5.0):
                size = c["max"] - c["min"]
                if size[0] < 150 or size[2] < 150:
                    continue
                cc = c["center"]
                side = "red" if cc[0] < 12000 else "blue"
                n_port = sum(1 for sl in manifest["slots"] if sl["kind"] == "port" and sl["side"] == side)
                manifest["slots"].append(prop_entry("port", side, [cc[0], float(c["min"][1]), cc[2]], 0.0, {"index": n_port + 1, "levels": 1}))

        if g["z"] == "base" and g["m"] == "dmg":
            for c in clusters(v, f, merge_mm=5.0):
                size = c["max"] - c["min"]
                if size.max() < 40:
                    continue   # 表示灯などの小片
                best, area = None, 0.0
                for t in c["tris"]:
                    a, b, cc = v[t]
                    n = np.cross(b - a, cc - a)
                    l = np.linalg.norm(n)
                    if l > area:
                        area, best = l, n / l
                center = c["center"]
                # 法線は陣地の中心から外を向く向きにそろえる
                side = "red" if center[0] < 12000 else "blue"
                high = c["min"][1] > 500
                manifest["basePanels"].append({
                    "side": side, "high": bool(high),
                    "center": to_unity(center).round(4).tolist(),
                    "normal": [round(float(best[0]), 4), round(float(best[1]), 4), round(float(-best[2]), 4)],
                    "size": [round(float(size[0]) / 1000, 4), round(float(size[1]) / 1000, 4), round(float(size[2]) / 1000, 4)],
                })
        if g["z"] == "bunker" and g["m"] == "dark":
            # 上面 = バンカーごとの「いちばん高い平らな部分」(7.6)。中央の 2 基は 290 mm (2 本の帯)、
            # 陣地横の 2 基は 150 mm。中央の 150 mm の面はスロープの踊り場で、75 mm の段差ともども含めない。
            flats = {}
            for t in f:
                a, b, c = v[t]
                n = np.cross(b - a, c - a)
                l = np.linalg.norm(n)
                if l > 0 and n[1] / l > 0.99 and abs(a[1] - b[1]) < 1 and abs(a[1] - c[1]) < 1:
                    y = int(round(a[1]))
                    if y in (150, 290):
                        flats.setdefault(y, []).append(t)
            rects = []
            for y, tris in flats.items():
                for c in clusters(v, np.array(tris), merge_mm=50.0):
                    size = c["max"] - c["min"]
                    if size[0] < 200 or size[2] < 200:
                        continue   # 外周の細い縁など
                    rects.append((y, c["min"], c["max"]))
            tops = [r for r in rects if r[0] == 290]
            for y, mn, mx in rects:
                if y == 150 and any(mn[0] <= t[2][0] and t[1][0] <= mx[0] and mn[2] <= t[2][2] and t[1][2] <= mx[2] for t in tops):
                    continue   # 290 の帯と重なる 150 の面は踊り場
                if y == 150:
                    tops.append((y, mn, mx))
            for y, mn, mx in tops:
                umn, umx = to_unity(mn), to_unity(mx)
                manifest["bunkerTops"].append({"xMin": round(min(umn[0], umx[0]), 3), "zMin": round(min(umn[2], umx[2]), 3),
                                               "xMax": round(max(umn[0], umx[0]), 3), "zMax": round(max(umn[2], umx[2]), 3),
                                               "y": round(float(y) / 1000, 3)})
        if g["z"] == "noentry":
            # 進入禁止ゾーンはフィールドの軸に対して回転した細長い台 2 つ x 2 か所。外接矩形にすると床に
            # 何もない所まで反則になるので、底面 (下向きの面) ごとに凸包を取り、回転した四角形として書き出す。
            bottoms = []
            for t in f:
                a, b, c = v[t]
                n = np.cross(b - a, c - a)
                l = np.linalg.norm(n)
                if l > 0 and n[1] / l < -0.99:
                    bottoms.append(t)
            for c in clusters(v, np.array(bottoms), merge_mm=5.0):
                pts = v[np.unique(c["tris"].ravel())][:, [0, 2]]
                hull = convex_hull(pts)
                poly = [to_unity([p[0], 0.0, p[1]]) for p in hull]
                xz = []
                for p in poly:
                    xz += [round(float(p[0]), 3), round(float(p[2]), 3)]
                # 向きのある箱 (NavMesh の立体用): 最長辺の向きを長さ方向にする
                cx, cz, yaw, length, width = oriented_box([(p[0], p[2]) for p in poly])
                manifest["noEntryZones"].append({"xz": xz, "cx": round(cx, 3), "cz": round(cz, 3), "yawDeg": round(yaw, 2),
                                                 "length": round(length, 3), "width": round(width, 3)})
        if g["z"] == "supply" and g["m"].startswith("zone_"):
            mn, mx = to_unity(v.min(0)), to_unity(v.max(0))
            manifest["supplyZones"].append({"side": g["m"].split("_")[1],
                                            "xMin": round(min(mn[0], mx[0]), 3), "zMin": round(min(mn[2], mx[2]), 3),
                                            "xMax": round(max(mn[0], mx[0]), 3), "zMax": round(max(mn[2], mx[2]), 3)})

    # コンテナ (4.3 / 7.1): 置き場 A 4 個、B 5 個、C 5 個 (各サイド)、共通 4 個。[置き場, 色, x, y(底面), z, 向き]
    cont = d.get("cont", {})
    if cont.get("at"):
        at = cont["at"]
        bc = {}
        for i, (zone, color, x, y, z, rot) in enumerate(at):
            side = {"red": "red", "blue": "blue"}.get(color, "none")
            entry = prop_entry("container", side, [x, y, z], unity_yaw(*_dir_from_rot(rot)), {"origin": zone, "color": color})
            manifest["props"].append(entry)
            if zone == "holderBC":
                bc.setdefault(color, []).append(entry)
        # 置き場 B と C: 正面から見て左が B。各サイド 10 個を並びの向きで前半 / 後半に分ける
        for color, items in bc.items():
            items.sort(key=lambda e: e["z"] if color == "red" else -e["z"])
            for k, e in enumerate(items):
                e["origin"] = "holderB" if k < 5 else "holderC"

    for o in d.get("overlays", []):
        if o["z"] == "start":
            xz = []
            for x, z in o["pts"]:
                xz += [round(float(x - CENTER[0]) / 1000, 3), round(float(-(z - CENTER[2])) / 1000, 3)]
            manifest["startZones"].append({"side": "red" if o["s"] == "r" else "blue", "xz": xz})

    # 開始位置: スタートゾーンの中に 2 列 × 4 行。向きは短辺方向 (赤は Unity +z、青は -z)。
    # 長辺方向 (フィールドの奥) を向かせていたが、短辺方向を向いて直進するとそのまま中央の
    # 開けた場所へ入れるので、操縦の最初の一手が楽になる。列は奥 (中央側) から詰める。
    for side, sign in (("red", -1.0), ("blue", 1.0)):
        pts = []
        for i in range(8):
            col, row = i % 2, i // 2
            fx = 2200 - row * 400          # 0..3500 の中 (中央側から)
            fz = 12600 + col * 1300        # 11000..15000 の中
            x, z = (fx, fz) if side == "red" else (24000 - fx, 15000 - fz)
            u = to_unity([x, 0.0, z])
            pts.append({"x": round(u[0], 3), "y": 0.15, "z": round(u[2], 3), "yawDeg": 0.0 if side == "red" else 180.0})
        manifest["spawns"].append({"side": side, "points": pts})

    manifest["basePanels"].sort(key=lambda p: (p["side"], p["high"], p["center"][0], p["center"][2]))
    with open(os.path.join(args.out, "field2027.json"), "w", encoding="utf-8") as fp:
        json.dump(manifest, fp, ensure_ascii=False, indent=1)
    print(f"groups {len(manifest['groups'])}, triangles {sum(g['triangles'] for g in manifest['groups'])}, "
          f"base panels {len(manifest['basePanels'])}, bunker tops {len(manifest['bunkerTops'])}, "
          f"no-entry zones {len(manifest['noEntryZones'])}, supply zones {len(manifest['supplyZones'])}")
    for p in manifest["basePanels"]:
        print(" panel", p["side"], "high" if p["high"] else "low", p["center"], p["normal"])
    for b in manifest["bunkerTops"]:
        print(" bunker top", b)
    for z in manifest["noEntryZones"]:
        print(" no-entry", z)
    for z in manifest["supplyZones"]:
        print(" supply", z)
    from collections import Counter
    print(" props", Counter((p["kind"], p["side"], p.get("origin")) for p in manifest["props"]))
    print(" slots", Counter((s["kind"], s["side"]) for s in manifest["slots"]))
    print(" carriers", manifest["carriers"])
    for sl in manifest["slots"]:
        if sl["kind"] in ("site", "port", "valley") or sl["index"] in (1, 6, 13):
            print("  slot", sl)


if __name__ == "__main__":
    main()
