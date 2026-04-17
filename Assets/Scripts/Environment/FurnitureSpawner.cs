using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using SceneSynthesis.Data;
using SceneSynthesis.Agents;

namespace SceneSynthesis.Environment
{
    public class FurnitureSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject furniturePrefab;

        [Header("Materials")]
        public Material[] categoryMaterials;

        readonly List<FurnitureAgent> _agents = new();
        public IReadOnlyList<FurnitureAgent> SpawnedAgents => _agents;

        public void SpawnFurniture(SceneData sceneData, RoomBounds roomBounds)
        {
            ClearAll();
            foreach (var item in sceneData.objects)
            {
                var agent = SpawnOne(item, roomBounds);
                if (agent != null) _agents.Add(agent);
            }
        }

        public void ClearAll()
        {
            foreach (var a in _agents)
                if (a != null) Destroy(a.gameObject);
            _agents.Clear();
        }

        FurnitureAgent SpawnOne(FurnitureItemData item, RoomBounds roomBounds)
        {
            float sizeX = Mathf.Max(item.sizeX, 0.1f);
            float sizeZ = Mathf.Max(item.sizeZ, 0.1f);
            float randX = Random.Range(roomBounds.minX + sizeX, roomBounds.maxX - sizeX);
            float randZ = Random.Range(roomBounds.minZ + sizeZ, roomBounds.maxZ - sizeZ);
            // Random.Range가 min>max 일 때 중앙으로 fallback
            if (randX > roomBounds.maxX - sizeX)
                randX = (roomBounds.minX + roomBounds.maxX) * 0.5f;
            if (randZ > roomBounds.maxZ - sizeZ)
                randZ = (roomBounds.minZ + roomBounds.maxZ) * 0.5f;

            float initY = Mathf.Max(item.sizeY, 0.1f);
            var spawnPos = transform.position + new Vector3(randX, initY, randZ);

            GameObject go;
            if (furniturePrefab != null)
            {
                go = Instantiate(furniturePrefab, spawnPos, Quaternion.identity, transform);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform, false);
                go.transform.position = spawnPos;
                go.AddComponent<Rigidbody>().isKinematic = true;

                // BehaviorParameters/Agent 추가 전 비활성화
                // → OnEnable() 이전에 BrainParameters 크기를 설정하기 위함
                go.SetActive(false);

                var bp = go.AddComponent<BehaviorParameters>();
                bp.BehaviorName = "FurnitureAgent";
                bp.BrainParameters.VectorObservationSize = FurnitureAgent.OBS_SIZE;
                bp.BrainParameters.NumStackedVectorObservations = 1;
                bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(3);

                go.AddComponent<FurnitureAgent>();

                var dr = go.AddComponent<DecisionRequester>();
                dr.DecisionPeriod = 5;
                dr.TakeActionsBetweenDecisions = true;

                go.SetActive(true); // 여기서 OnEnable() 실행 → 올바른 크기로 등록
            }

            go.name = $"{item.category}_{item.uid}";
            go.transform.localScale = item.FullSize;
            go.transform.rotation = Quaternion.Euler(0f, item.angle * Mathf.Rad2Deg, 0f);
            go.layer = LayerMask.NameToLayer("Furniture");

            ApplyCategoryMaterial(go, item.categoryIndex);

            var agent = go.GetComponent<FurnitureAgent>();
            agent.Initialize(item, roomBounds);
            return agent;
        }

        void ApplyCategoryMaterial(GameObject go, int categoryIndex)
        {
            if (categoryMaterials == null || categoryMaterials.Length == 0) return;
            var mat = categoryMaterials[categoryIndex % categoryMaterials.Length];
            if (mat != null)
            {
                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.material = mat;
            }
        }
    }
}
