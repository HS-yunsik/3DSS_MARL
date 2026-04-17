using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using SceneSynthesis.Data;
using SceneSynthesis.Agents;

namespace SceneSynthesis.Environment
{
    public class FurnitureSpawner : MonoBehaviour
    {
        [Header("Pool")]
        [Tooltip("Max furniture items across all scene types. Excess agents park out of sight.")]
        public int maxPoolSize = 15;

        [Header("Materials")]
        public Material[] categoryMaterials;

        readonly List<FurnitureAgent> _pool = new();
        public IReadOnlyList<FurnitureAgent> PooledAgents => _pool;

        // Called once on Start to create the fixed agent pool
        public void InitializePool()
        {
            for (int i = 0; i < maxPoolSize; i++)
                _pool.Add(CreatePoolAgent(i));
        }

        // Called each episode with new scene data (before EndEpisode on agents)
        public void AssignScene(SceneData sceneData, RoomBounds roomBounds)
        {
            int poolIdx = 0;
            if (sceneData.objects != null)
            {
                foreach (var item in sceneData.objects)
                {
                    if (poolIdx >= _pool.Count) break;
                    // Skip items whose footprint exceeds 95% of the room in either dimension
                    if (item.sizeX * 2f > roomBounds.width  * 0.95f ||
                        item.sizeZ * 2f > roomBounds.depth  * 0.95f)
                        continue;
                    _pool[poolIdx].Reassign(item, roomBounds);
                    ApplyMaterial(_pool[poolIdx].gameObject, item.categoryIndex);
                    poolIdx++;
                }
            }
            // Deactivate remaining pool agents
            for (int i = poolIdx; i < _pool.Count; i++)
                _pool[i].Reassign(null, null);
        }

        FurnitureAgent CreatePoolAgent(int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"FurnitureAgent_{index}";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, -100f, 0f);

            go.AddComponent<Rigidbody>().isKinematic = true;

            // Configure BehaviorParameters BEFORE SetActive to avoid OnEnable before brain is set
            go.SetActive(false);

            var bp = go.AddComponent<BehaviorParameters>();
            bp.BehaviorName = "FurnitureAgent";
            bp.BrainParameters.VectorObservationSize = FurnitureAgent.OBS_SIZE;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(3);

            go.AddComponent<FurnitureAgent>();

            var dr = go.AddComponent<Unity.MLAgents.DecisionRequester>();
            dr.DecisionPeriod = 5;
            dr.TakeActionsBetweenDecisions = true;

            go.SetActive(true); // OnEnable fires here with correct BrainParameters

            return go.GetComponent<FurnitureAgent>();
        }

        void ApplyMaterial(GameObject go, int categoryIndex)
        {
            if (categoryMaterials == null || categoryMaterials.Length == 0) return;
            var mat = categoryMaterials[categoryIndex % categoryMaterials.Length];
            if (mat == null) return;
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material = mat;
        }
    }
}
