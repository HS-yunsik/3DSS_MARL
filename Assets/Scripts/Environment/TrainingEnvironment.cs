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
        public int maxStepsPerEpisode = 5000;

        SceneData _currentScene;
        int _stepCount;
        bool _episodeEnded;

        void Awake()
        {
            if (roomBuilder      == null) roomBuilder      = GetComponentInChildren<RoomBuilder>();
            if (furnitureSpawner == null) furnitureSpawner = GetComponentInChildren<FurnitureSpawner>();
        }

        void Start()
        {
            SceneDataLoader.LoadCatalog();
            StartEpisode();
        }

        // Update: 에이전트 수에 무관하게 매 프레임 1회만 종료 판정
        void Update()
        {
            if (_episodeEnded) return;

            _stepCount++;

            bool allSettled = true;
            foreach (var agent in furnitureSpawner.SpawnedAgents)
            {
                if (!agent.IsSettled) { allSettled = false; break; }
            }

            if (allSettled || _stepCount >= maxStepsPerEpisode)
                EndEpisode(allSettled);
        }

        public void StartEpisode()
        {
            _currentScene = SceneDataLoader.LoadRandomScene(roomType);
            if (_currentScene == null)
            {
                Debug.LogError($"[TrainingEnvironment] No scene loaded for {roomType}");
                return;
            }

            roomBuilder.Build(_currentScene.room);
            furnitureSpawner.SpawnFurniture(_currentScene, _currentScene.room.bounds);

            foreach (var agent in furnitureSpawner.SpawnedAgents)
                agent.SetEnvironment(this, furnitureSpawner.SpawnedAgents);

            _stepCount    = 0;
            _episodeEnded = false;
        }

        // 에피소드 종료: 보너스 지급 후 새 씬으로 리셋
        void EndEpisode(bool success)
        {
            _episodeEnded = true;
            float bonus = success ? sceneCompleteBonus : 0f;

            foreach (var agent in furnitureSpawner.SpawnedAgents)
                agent.EndEpisodeWithBonus(bonus);

            // ML-Agents가 OnEpisodeBegin() 호출 후 새 씬 로드
            StartEpisode();
        }

        public IReadOnlyList<FurnitureAgent> GetAllAgents() => furnitureSpawner.SpawnedAgents;
        public SceneData CurrentScene => _currentScene;
    }
}
