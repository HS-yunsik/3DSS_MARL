"""
3D-FUTURE model_info.json → Unity용 model_catalog.json 생성.
각 모델의 jid, super-category, category, 모델 경로를 포함.
"""

import os, json
from config import MODEL_BASE, OUTPUT_BASE


def main():
    info_path = os.path.join(MODEL_BASE, "model_info.json")
    with open(info_path) as f:
        model_info = json.load(f)

    catalog = {}
    for item in model_info:
        jid = item["model_id"]
        model_dir = os.path.join(MODEL_BASE, jid)
        obj_path  = os.path.join(model_dir, "normalized_model.obj")
        catalog[jid] = {
            "superCategory": item.get("super-category", ""),
            "category":      item.get("category", ""),
            "style":         item.get("style", ""),
            "modelPath":     obj_path if os.path.exists(obj_path) else "",
        }

    os.makedirs(OUTPUT_BASE, exist_ok=True)
    out_path = os.path.join(OUTPUT_BASE, "model_catalog.json")
    with open(out_path, "w") as f:
        json.dump(catalog, f, separators=(",", ":"))

    print(f"Done. Model catalog saved: {len(catalog)} models -> {out_path}")


if __name__ == "__main__":
    main()
