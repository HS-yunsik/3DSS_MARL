import os

DATASET_BASE   = r"D:\3D-Front-dataset\3D-FRONT-preproccessed"
MODEL_BASE     = r"D:\3D-Front-dataset\3D-FUTURE-model"
PROJECT_ROOT   = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_BASE    = r"D:\3D-Front-dataset\processed"

ROOM_TYPES = ["bedroom", "livingroom", "diningroom", "library"]

ROOM_DIR_MAP = {
    "bedroom":    "3D-FRONT-bedroom",
    "livingroom": "3D-FRONT-livingroom",
    "diningroom": "3D-FRONT-diningroom",
    "library":    "3D-FRONT-library",
}

MAX_NEIGHBORS = 5  # agents observe N nearest neighbors
