using System;
using System.Collections.Generic;
using UnityEngine;
using SceneSynthesis.Data;
using SceneSynthesis.Agents;

namespace SceneSynthesis.Environment
{
    public class TrainingEnvironment : MonoBehaviour
    {
        [Header("Room Type")]
        public string roomType = "bedroom";

        [Header("Components")]
        public RoomBuilder roomBuilder;
        public FurnitureSpawner furnitureSpawner;

        [Header("Reward")]
        public float sceneCompleteBonus = 2.0f;

        [Header("Episode Settings")]
        [Tooltip("Max Unity Update() frames per episode (~1000 frames ≈ 17s at 60fps)")]
        public int maxStepsPerEpisode = 1000;

        SceneData _currentScene;
        int _stepCount;
        bool _episodeActive;

        void Awake()
        {
            if (roomBuilder      == null) roomBuilder      = GetComponentInChildren<RoomBuilder>();
            if (furnitureSpawner == null) furnitureSpawner = GetComponentInChildren<FurnitureSpawner>();
        }

        void Start()
        {
            SceneDataLoader.LoadCatalog();
            furnitureSpawner.InitializePool();
            foreach (var agent in furnitureSpawner.PooledAgents)
                agent.SetNeighborList(furnitureSpawner.PooledAgents);
            LoadNextScene();
            _stepCount    = 0;
            _episodeActive = true;
        }

        void Update()
        {
            if (!_episodeActive) return;
            _stepCount++;

            // Only check settle when at least one agent is active
            int activeCount = 0;
            bool allSettled = true;
            foreach (var agent in furnitureSpawner.PooledAgents)
            {
                if (!agent.IsActive) continue;
                activeCount++;
                if (!agent.IsSettled) allSettled = false;
            }

            bool timedOut = _stepCount >= maxStepsPerEpisode;
            bool success  = activeCount > 0 && allSettled && !timedOut;

            if (timedOut || success)
                EndEpisode(success);
        }

        void LoadNextScene()
        {
            var scene = SceneDataLoader.LoadRandomScene(roomType);
            if (scene == null)
            {
                Debug.LogWarning($"[TrainingEnvironment] LoadRandomScene null for '{roomType}', reusing last scene.");
                if (_currentScene != null)
                    furnitureSpawner.AssignScene(_currentScene, _currentScene.room.bounds);
                return;
            }
            _currentScene = scene;
            roomBuilder.Build(_currentScene.room);
            furnitureSpawner.AssignScene(_currentScene, _currentScene.room.bounds);
        }

        void EndEpisode(bool success)
        {
            _episodeActive = false;
            try
            {
                float bonus = success ? sceneCompleteBonus : 0f;
                LoadNextScene();
                foreach (var agent in furnitureSpawner.PooledAgents)
                    agent.EndEpisodeWithBonus(bonus);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TrainingEnvironment] EndEpisode error: {e}");
                foreach (var agent in furnitureSpawner.PooledAgents)
                    try { agent.EndEpisodeWithBonus(0f); } catch { }
            }
            finally
            {
                // Must always reset — if this stays false, episodes stop permanently
                _stepCount    = 0;
                _episodeActive = true;
            }
        }

        public IReadOnlyList<FurnitureAgent> GetAllAgents() => furnitureSpawner.PooledAgents;
        public SceneData CurrentScene => _currentScene;
    }
}
