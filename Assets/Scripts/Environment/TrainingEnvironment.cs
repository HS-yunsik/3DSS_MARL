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

            // Create fixed agent pool once
            furnitureSpawner.InitializePool();

            // Wire up neighbor lists (pool never changes, so set once)
            foreach (var agent in furnitureSpawner.PooledAgents)
                agent.SetNeighborList(furnitureSpawner.PooledAgents);

            // Load first scene and assign data to pool agents
            LoadNextScene();
            _stepCount    = 0;
            _episodeActive = true;
        }

        void Update()
        {
            if (!_episodeActive) return;
            _stepCount++;

            bool allSettled = true;
            foreach (var agent in furnitureSpawner.PooledAgents)
            {
                if (!agent.IsSettled) { allSettled = false; break; }
            }

            if (allSettled || _stepCount >= maxStepsPerEpisode)
                EndEpisode(allSettled);
        }

        void LoadNextScene()
        {
            _currentScene = SceneDataLoader.LoadRandomScene(roomType);
            if (_currentScene == null)
            {
                Debug.LogError($"[TrainingEnvironment] No scene loaded for '{roomType}'");
                return;
            }
            roomBuilder.Build(_currentScene.room);
            furnitureSpawner.AssignScene(_currentScene, _currentScene.room.bounds);
        }

        void EndEpisode(bool success)
        {
            _episodeActive = false;
            float bonus = success ? sceneCompleteBonus : 0f;

            // Reassign data for next scene BEFORE calling EndEpisode on agents.
            // ML-Agents calls OnEpisodeBegin() next FixedUpdate using the updated _itemData.
            LoadNextScene();

            foreach (var agent in furnitureSpawner.PooledAgents)
                agent.EndEpisodeWithBonus(bonus);

            _stepCount    = 0;
            _episodeActive = true;
        }

        public IReadOnlyList<FurnitureAgent> GetAllAgents() => furnitureSpawner.PooledAgents;
        public SceneData CurrentScene => _currentScene;
    }
}
