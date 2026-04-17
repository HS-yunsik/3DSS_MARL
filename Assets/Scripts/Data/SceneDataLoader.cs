using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SceneSynthesis.Data;

namespace SceneSynthesis.Data
{
    public static class SceneDataLoader
    {
        static string _dataRoot;
        static SceneCatalog _catalog;
        static readonly Dictionary<string, RoomStats> _statsCache = new();

        static string DataRoot
        {
            get
            {
                if (_dataRoot != null) return _dataRoot;

                string configPath = Path.Combine(Application.streamingAssetsPath, "training_data_config.json");
                if (File.Exists(configPath))
                {
                    var cfg = JsonUtility.FromJson<DataConfig>(File.ReadAllText(configPath));
                    _dataRoot = cfg.sceneDataPath;
                }
                else
                {
                    // fallback: StreamingAssets 내부 (구버전 호환)
                    _dataRoot = Path.Combine(Application.streamingAssetsPath, "SceneData");
                    Debug.LogWarning("[SceneDataLoader] training_data_config.json not found, using StreamingAssets fallback.");
                }
                return _dataRoot;
            }
        }

        public static SceneCatalog LoadCatalog()
        {
            if (_catalog != null) return _catalog;
            string path = Path.Combine(DataRoot, "catalog.json");
            _catalog = JsonUtility.FromJson<SceneCatalog>(File.ReadAllText(path));
            return _catalog;
        }

        public static RoomStats LoadRoomStats(string roomType)
        {
            if (_statsCache.TryGetValue(roomType, out var cached)) return cached;
            string path = Path.Combine(DataRoot, roomType, "stats.json");
            var stats = JsonUtility.FromJson<RoomStats>(File.ReadAllText(path));
            _statsCache[roomType] = stats;
            return stats;
        }

        public static SceneData LoadScene(string roomType, string sceneId)
        {
            string path = Path.Combine(DataRoot, roomType, $"{sceneId}.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SceneDataLoader] Scene not found: {path}");
                return null;
            }
            return JsonUtility.FromJson<SceneData>(File.ReadAllText(path));
        }

        public static SceneData LoadRandomScene(string roomType)
        {
            var catalog = LoadCatalog();
            var ids = catalog.GetSceneIds(roomType);
            if (ids == null || ids.Length == 0) return null;
            string id = ids[Random.Range(0, ids.Length)];
            return LoadScene(roomType, id);
        }

        [System.Serializable]
        class DataConfig { public string sceneDataPath; }
    }
}
