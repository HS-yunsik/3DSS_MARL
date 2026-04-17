# ML-Agents & Project Commands

## Environment Setup
```bash
conda activate mlagents
cd C:\Users\CYS\Unityprojects\3DSceneSynthesis_Using-RL
```

## Data Preprocessing
```bash
# Test run (50 scenes per category)
python DataPipeline/preprocess_scenes.py --test

# Full run (6214 scenes -> D:\3D-Front-dataset\processed\)
python DataPipeline/preprocess_scenes.py

# Build 3D-FUTURE model catalog (16563 models)
python DataPipeline/build_model_catalog.py
```

## Training
```bash
# New training run
mlagents-learn Config/training_config.yaml --run-id=run_01

# Resume training from checkpoint
mlagents-learn Config/training_config.yaml --run-id=run_01 --resume

# Force restart (overwrite existing run)
mlagents-learn Config/training_config.yaml --run-id=run_01 --force

# Specify Unity executable (standalone build)
mlagents-learn Config/training_config.yaml --run-id=run_01 --env=Build/TrainingScene
```

## TensorBoard (training progress visualization)
```bash
tensorboard --logdir results
# Open browser: http://localhost:6006
```

## Training Results Location
```
results/
  run_01/
    FurnitureAgent/          <- checkpoints (.pt)
    FurnitureAgent.onnx      <- exported model for Unity inference
```

## Data Paths
- Raw 3D-FRONT:     D:\3D-Front-dataset\3D-FRONT-preproccessed\
- Raw 3D-FUTURE:    D:\3D-Front-dataset\3D-FUTURE-model\
- Processed data:   D:\3D-Front-dataset\processed\
- Data config:      Assets\StreamingAssets\training_data_config.json

## Unity Play Mode (Editor Training)
1. Run `mlagents-learn` command above -> wait for "Listening on port 5004"
2. Press Play in Unity Editor
3. Training starts automatically
4. Ctrl+C to stop -> model saved to results/

## Notes
- Python env: mlagents (conda)
- Unity version: 6000.3.10f1
- ML-Agents Unity package: 4.0.2
- ML-Agents Python: 1.1.0 (API 1.5.0)
- ONNX export error on Ctrl+C is a known torch 2.11 compatibility issue
  -> training and .pt checkpoint are unaffected
