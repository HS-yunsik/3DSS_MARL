"""
boxes.npz 파일들을 Unity가 StreamingAssets에서 읽을 수 있는 JSON 형태로 변환.

출력 구조:
  StreamingAssets/SceneData/
    catalog.json                    # 전체 씬 목록
    bedroom/stats.json              # 카테고리 정보 + 정규화 bounds
    bedroom/{scene_id}.json         # 씬 데이터
    livingroom/...
    ...
"""

import os, json, sys
import numpy as np
from config import DATASET_BASE, OUTPUT_BASE, ROOM_TYPES, ROOM_DIR_MAP


def load_stats(room_type: str) -> dict:
    stats_path = os.path.join(DATASET_BASE, ROOM_DIR_MAP[room_type], "dataset_stats.txt")
    with open(stats_path) as f:
        return json.load(f)


def get_unique_floor_vertices(vertices_np: np.ndarray, centroid: list) -> list:
    """floor_plan_vertices (Nx3) → centroid 기준 상대좌표 [[x,z], ...]
    translations도 centroid 기준이므로 좌표계를 통일한다.
    """
    seen = set()
    result = []
    for v in vertices_np:
        x = float(v[0]) - centroid[0]
        z = float(v[2]) - centroid[1]
        key = (round(x, 6), round(z, 6))
        if key not in seen:
            seen.add(key)
            result.append([x, z])
    return result


def compute_room_bounds(vertices: list) -> dict:
    xs = [v[0] for v in vertices]
    zs = [v[1] for v in vertices]
    return {
        "minX": min(xs), "maxX": max(xs),
        "minZ": min(zs), "maxZ": max(zs),
        "width": max(xs) - min(xs),
        "depth": max(zs) - min(zs),
    }


def process_scene(npz_path: str, scene_id: str, room_type: str, stats: dict) -> dict:
    data = np.load(npz_path, allow_pickle=True)

    class_labels_list = stats["class_labels"]
    # start/end 제외한 실제 카테고리 인덱스만 사용
    valid_labels = [l for l in class_labels_list if l not in ("start", "end")]

    centroid  = [float(data["floor_plan_centroid"][0]), float(data["floor_plan_centroid"][2])]
    vertices  = get_unique_floor_vertices(data["floor_plan_vertices"], centroid)
    bounds    = compute_room_bounds(vertices)

    room_w = bounds["width"]
    room_d = bounds["depth"]

    objects = []
    n_objs = data["translations"].shape[0]
    for i in range(n_objs):
        cat_vec   = data["class_labels"][i]
        cat_idx   = int(np.argmax(cat_vec))
        cat_name  = class_labels_list[cat_idx] if cat_idx < len(class_labels_list) else "unknown"

        if cat_name in ("start", "end"):
            continue

        t = data["translations"][i]
        s = data["sizes"][i]
        a = float(data["angles"][i][0])

        # sizes are half-extents; full footprint must fit within 90% of room
        full_w = float(s[0]) * 2
        full_d = float(s[2]) * 2
        if full_w > room_w * 0.9 or full_d > room_d * 0.9:
            continue

        objects.append({
            "uid":           str(data["uids"][i]),
            "jid":           str(data["jids"][i]),
            "category":      cat_name,
            "categoryIndex": cat_idx,
            "posX": float(t[0]),
            "posY": float(t[1]),
            "posZ": float(t[2]),
            "sizeX": float(s[0]),
            "sizeY": float(s[1]),
            "sizeZ": float(s[2]),
            "angle": a,
        })

    return {
        "sceneId":   scene_id,
        "sceneType": room_type,
        "room": {
            "vertices": vertices,
            "centroid": centroid,
            "bounds":   bounds,
        },
        "objects": objects,
    }


def preprocess_room(room_type: str, stats: dict, max_scenes: int = None) -> list:
    cat_dir  = os.path.join(DATASET_BASE, ROOM_DIR_MAP[room_type])
    out_dir  = os.path.join(OUTPUT_BASE, room_type)
    os.makedirs(out_dir, exist_ok=True)

    scenes = sorted([
        d for d in os.listdir(cat_dir)
        if os.path.isdir(os.path.join(cat_dir, d))
    ])
    if max_scenes:
        scenes = scenes[:max_scenes]

    scene_ids = []
    for i, scene_id in enumerate(scenes):
        npz_path = os.path.join(cat_dir, scene_id, "boxes.npz")
        if not os.path.exists(npz_path):
            continue

        scene_data = process_scene(npz_path, scene_id, room_type, stats)
        if not scene_data["objects"]:
            continue

        out_path = os.path.join(out_dir, f"{scene_id}.json")
        with open(out_path, "w") as f:
            json.dump(scene_data, f, separators=(",", ":"))

        scene_ids.append(scene_id)

        if (i + 1) % 200 == 0:
            print(f"  [{room_type}] {i+1}/{len(scenes)} processed")

    return scene_ids


def write_stats(room_type: str, stats: dict):
    out_dir = os.path.join(OUTPUT_BASE, room_type)
    os.makedirs(out_dir, exist_ok=True)

    valid_labels = [l for l in stats["class_labels"] if l not in ("start", "end")]
    out = {
        "roomType":        room_type,
        "classLabels":     valid_labels,
        "boundsTranslations": stats["bounds_translations"],
        "boundsSizes":     stats["bounds_sizes"],
        "boundsAngles":    stats["bounds_angles"],
    }
    with open(os.path.join(out_dir, "stats.json"), "w") as f:
        json.dump(out, f, indent=2)


def main():
    os.makedirs(OUTPUT_BASE, exist_ok=True)

    # max_scenes=None → 전체, 테스트 시 작은 숫자 사용
    max_scenes = None
    if "--test" in sys.argv:
        max_scenes = 50
        print("[TEST MODE] Processing 50 scenes per category")

    catalog = {}
    for room_type in ROOM_TYPES:
        print(f"\nProcessing {room_type}...")
        stats = load_stats(room_type)
        write_stats(room_type, stats)
        scene_ids = preprocess_room(room_type, stats, max_scenes=max_scenes)
        catalog[room_type] = scene_ids
        print(f"  Done: {len(scene_ids)} scenes")

    catalog_path = os.path.join(OUTPUT_BASE, "catalog.json")
    with open(catalog_path, "w") as f:
        json.dump(catalog, f, indent=2)

    total = sum(len(v) for v in catalog.values())
    print(f"\nDone. Catalog saved: {total} scenes total")
    print(f"  Output: {OUTPUT_BASE}")


if __name__ == "__main__":
    main()
