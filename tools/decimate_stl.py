#!/usr/bin/env python3
"""バイナリ STL を頂点クラスタリングで間引く。

CoRE のステージは USD から変換した STL が 16 枚で 168 万三角形ある。PC なら
そのまま描けるが、Android タブレットでは描画も MeshCollider の生成も重い。
格子 (cell) 単位で頂点をまとめ、潰れた三角形と重複を捨てることで、
見た目の輪郭を保ったまま三角形数を 1 桁落とす。

平面 (床・壁) 上の頂点は同じ平面上の頂点の平均に置き換わるので平面から
外れない。曲面 (フィレット・ボルト穴) は格子の粗さで角張るが、ステージの
背景としては気にならない。

使い方:
  decimate_stl.py --cell 0.03 --min-triangles 20000 in.stl out.stl
  (三角形数が min-triangles 未満のファイルはそのままコピーする)
"""
import argparse
import struct
import sys

import numpy as np

DT = np.dtype([('n', '<f4', 3), ('v', '<f4', (3, 3)), ('a', '<u2')])


def read_stl(path):
    with open(path, 'rb') as f:
        header = f.read(80)
        n = struct.unpack('<I', f.read(4))[0]
        data = np.frombuffer(f.read(n * 50), dtype=DT)
    return header, data['v'].astype(np.float64)


def write_stl(path, tris, header=b''):
    tris = tris.astype(np.float32)
    normals = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    length = np.linalg.norm(normals, axis=1, keepdims=True)
    normals = np.where(length > 0, normals / np.maximum(length, 1e-12), 0).astype(np.float32)
    out = np.zeros(len(tris), dtype=DT)
    out['n'] = normals
    out['v'] = tris
    with open(path, 'wb') as f:
        f.write(header[:80].ljust(80, b'\0'))
        f.write(struct.pack('<I', len(out)))
        f.write(out.tobytes())


def decimate(tris, cell):
    verts = tris.reshape(-1, 3)
    keys = np.floor(verts / cell).astype(np.int64)
    # セルごとに代表点 (平均) を作る
    _, inverse, counts = np.unique(keys, axis=0, return_inverse=True, return_counts=True)
    inverse = inverse.reshape(-1)
    rep = np.zeros((len(counts), 3))
    np.add.at(rep, inverse, verts)
    rep /= counts[:, None]
    ids = inverse.reshape(-1, 3)
    # 2 頂点以上が同じセルに落ちた三角形は潰れているので捨てる
    ok = (ids[:, 0] != ids[:, 1]) & (ids[:, 1] != ids[:, 2]) & (ids[:, 0] != ids[:, 2])
    ids = ids[ok]
    # 同じ 3 セルを使う三角形は 1 枚だけ残す (向きは最初のものを使う)
    sorted_ids = np.sort(ids, axis=1)
    _, first = np.unique(sorted_ids, axis=0, return_index=True)
    ids = ids[np.sort(first)]
    return rep[ids]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('src')
    ap.add_argument('dst')
    ap.add_argument('--cell', type=float, default=0.03, help='格子の一辺 [m]')
    ap.add_argument('--min-triangles', type=int, default=20000,
                    help='これ未満の三角形数のファイルは間引かずにコピーする')
    args = ap.parse_args()

    header, tris = read_stl(args.src)
    before = len(tris)
    if before >= args.min_triangles:
        tris = decimate(tris, args.cell)
    write_stl(args.dst, tris, header)
    print(f'{args.src}: {before} -> {len(tris)} triangles')
    return 0


if __name__ == '__main__':
    sys.exit(main())
