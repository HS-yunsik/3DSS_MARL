using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using SceneSynthesis.Data;
using SceneSynthesis.Environment;

namespace SceneSynthesis.Agents
{
    /// <summary>
    /// 가구 에이전트.
    ///
    /// Observation (총 73차원):
    ///   Self     (8):  norm_x, norm_z, sin_angle, cos_angle, norm_sizeX, norm_sizeZ, 0, 0
    ///   Category (24): one-hot (최대 24 카테고리, bedroom=22+padding)
    ///   Neighbors(35): 최근접 5개 × 7 [rel_x, rel_z, sin_a, cos_a, sizeX, sizeZ, same_super]
    ///   Walls    (4):  ±X, ±Z 방향 raycast 거리 (정규화)
    ///   Room     (2):  norm_room_width, norm_room_depth
    ///
    /// Action (Continuous 3):
    ///   [0] move_x   [-1,1]
    ///   [1] move_z   [-1,1]
    ///   [2] rotate   [-1,1]
    ///
    /// Reward:
    ///   매 스텝: 시간 패널티 + 충돌 패널티 + 경계 이탈 패널티 + 안정 보상
    ///   에피소드 종료: 씬 완성 보너스
    /// </summary>
    public class FurnitureAgent : Agent
    {
        public const int MAX_CATEGORIES = 24;
        public const int MAX_NEIGHBORS  = 5;
        public const int OBS_SIZE       = 8 + MAX_CATEGORIES + MAX_NEIGHBORS * 7 + 4 + 2; // 73

        [Header("Movement")]
        public float moveSpeed      = 0.1f;
        public float rotationSpeed  = 15f;  // degrees per action unit

        [Header("Reward")]
        public float collisionPenaltyPerObject = -0.3f;
        public float outOfBoundsPenalty        = -1.0f;
        public float settleRewardPerStep       = +0.01f;
        public float timePenaltyPerStep        = -0.005f;

        [Header("Settle Detection")]
        public int settleFrames = 30; // N 프레임 동안 움직임 없으면 settled

        // 내부 상태
        FurnitureItemData _itemData;
        RoomBounds _roomBounds;
        TrainingEnvironment _env;
        IReadOnlyList<FurnitureAgent> _allAgents;
        BoxCollider _collider;

        Vector3 _prevPos;
        int _stillFrames;
        bool _isSettled;

        public bool IsSettled => _isSettled;
        public FurnitureItemData ItemData => _itemData;

        // ── 초기화 ──────────────────────────────────────────────────────────

        public void Initialize(FurnitureItemData itemData, RoomBounds roomBounds)
        {
            _itemData   = itemData;
            _roomBounds = roomBounds;
            _collider   = GetComponent<BoxCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<BoxCollider>();

            // layer 설정 (BehaviorParameters는 FurnitureSpawner에서 SetActive 전에 설정)
            gameObject.layer = LayerMask.NameToLayer("Furniture");
        }

        public void SetEnvironment(TrainingEnvironment env, IReadOnlyList<FurnitureAgent> allAgents)
        {
            _env       = env;
            _allAgents = allAgents;
        }

        // ── ML-Agents 오버라이드 ─────────────────────────────────────────────

        public override void OnEpisodeBegin()
        {
            // _itemData가 없으면 아직 Initialize() 전이므로 스킵
            if (_itemData == null || _roomBounds == null) return;

            // 방 안 랜덤 위치로 리셋
            float randX = Random.Range(_roomBounds.minX + _itemData.sizeX, _roomBounds.maxX - _itemData.sizeX);
            float randZ = Random.Range(_roomBounds.minZ + _itemData.sizeZ, _roomBounds.maxZ - _itemData.sizeZ);
            transform.localPosition = new Vector3(randX, _itemData.sizeY, randZ);
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            _stillFrames = 0;
            _isSettled   = false;
            _prevPos     = transform.position;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            var b = _roomBounds;
            float roomW = b.width  > 0 ? b.width  : 1f;
            float roomD = b.depth  > 0 ? b.depth  : 1f;

            // --- Self (8) ---
            float normX = (transform.localPosition.x - (b.minX + b.maxX) * 0.5f) / (roomW * 0.5f);
            float normZ = (transform.localPosition.z - (b.minZ + b.maxZ) * 0.5f) / (roomD * 0.5f);
            float angle  = transform.eulerAngles.y * Mathf.Deg2Rad;
            sensor.AddObservation(normX);
            sensor.AddObservation(normZ);
            sensor.AddObservation(Mathf.Sin(angle));
            sensor.AddObservation(Mathf.Cos(angle));
            sensor.AddObservation(_itemData.sizeX / roomW);
            sensor.AddObservation(_itemData.sizeZ / roomD);
            sensor.AddObservation(0f); // padding
            sensor.AddObservation(0f);

            // --- Category one-hot (24) ---
            for (int i = 0; i < MAX_CATEGORIES; i++)
                sensor.AddObservation(i == _itemData.categoryIndex ? 1f : 0f);

            // --- Nearest N neighbors (5 × 7) ---
            var neighbors = GetNearestNeighbors(MAX_NEIGHBORS);
            for (int i = 0; i < MAX_NEIGHBORS; i++)
            {
                if (i < neighbors.Count)
                {
                    var n = neighbors[i];
                    Vector3 rel = transform.InverseTransformPoint(n.transform.position);
                    float nAngle = n.transform.eulerAngles.y * Mathf.Deg2Rad;
                    sensor.AddObservation(rel.x / roomW);
                    sensor.AddObservation(rel.z / roomD);
                    sensor.AddObservation(Mathf.Sin(nAngle));
                    sensor.AddObservation(Mathf.Cos(nAngle));
                    sensor.AddObservation(n._itemData.sizeX / roomW);
                    sensor.AddObservation(n._itemData.sizeZ / roomD);
                    sensor.AddObservation(IsSameCategory(n) ? 1f : 0f);
                }
                else
                {
                    // 없는 이웃은 zero padding
                    for (int j = 0; j < 7; j++) sensor.AddObservation(0f);
                }
            }

            // --- Wall distances (4) via Raycast ---
            float maxRay = Mathf.Max(roomW, roomD);
            sensor.AddObservation(RaycastWall(Vector3.right,   maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.left,    maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.forward, maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.back,    maxRay) / maxRay);

            // --- Room size (2) ---
            sensor.AddObservation(roomW);
            sensor.AddObservation(roomD);
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            var ca = actionBuffers.ContinuousActions;
            float dx = ca[0] * moveSpeed;
            float dz = ca[1] * moveSpeed;
            float dr = ca[2] * rotationSpeed;

            // 이동 적용
            Vector3 newPos = transform.localPosition + new Vector3(dx, 0f, dz);
            newPos.y = _itemData.sizeY; // 항상 바닥 위
            transform.localPosition = newPos;
            transform.Rotate(0f, dr, 0f);

            // ─ 보상 계산 ─
            float reward = timePenaltyPerStep;

            // 경계 이탈 확인
            bool inBounds = IsInRoomBounds();
            if (!inBounds)
            {
                reward += outOfBoundsPenalty;
                ClampToBounds();
            }

            // 충돌 확인 (OverlapBox)
            int collisions = CountCollisions();
            if (collisions > 0)
                reward += collisionPenaltyPerObject * collisions;

            // 충돌 없고 경계 내부면 settle 보상
            if (inBounds && collisions == 0)
                reward += settleRewardPerStep;

            AddReward(reward);

            // Settle 감지 (종료 판정은 TrainingEnvironment.Update()에서)
            UpdateSettleState();
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // 키보드 테스트용
            var ca = actionsOut.ContinuousActions;
            ca[0] = Input.GetAxis("Horizontal");
            ca[1] = Input.GetAxis("Vertical");
            ca[2] = 0f;
        }

        public void EndEpisodeWithBonus(float bonus)
        {
            AddReward(bonus);
            EndEpisode();
        }

        // ── 내부 헬퍼 ────────────────────────────────────────────────────────

        List<FurnitureAgent> GetNearestNeighbors(int count)
        {
            var result = new List<(float dist, FurnitureAgent agent)>();
            foreach (var other in _allAgents)
            {
                if (other == this) continue;
                float d = Vector3.Distance(transform.position, other.transform.position);
                result.Add((d, other));
            }
            result.Sort((a, b) => a.dist.CompareTo(b.dist));

            var agents = new List<FurnitureAgent>();
            for (int i = 0; i < Mathf.Min(count, result.Count); i++)
                agents.Add(result[i].agent);
            return agents;
        }

        bool IsSameCategory(FurnitureAgent other) =>
            _itemData.category == other._itemData.category;

        float RaycastWall(Vector3 dir, float maxDist)
        {
            int wallMask = LayerMask.GetMask("Wall");
            if (Physics.Raycast(transform.position, dir, out var hit, maxDist, wallMask))
                return hit.distance;
            return maxDist;
        }

        bool IsInRoomBounds()
        {
            var p = transform.localPosition;
            var b = _roomBounds;
            return p.x >= b.minX + _itemData.sizeX && p.x <= b.maxX - _itemData.sizeX
                && p.z >= b.minZ + _itemData.sizeZ && p.z <= b.maxZ - _itemData.sizeZ;
        }

        void ClampToBounds()
        {
            var p = transform.localPosition;
            var b = _roomBounds;
            p.x = Mathf.Clamp(p.x, b.minX + _itemData.sizeX, b.maxX - _itemData.sizeX);
            p.z = Mathf.Clamp(p.z, b.minZ + _itemData.sizeZ, b.maxZ - _itemData.sizeZ);
            transform.localPosition = p;
        }

        int CountCollisions()
        {
            // OverlapBox로 겹치는 Collider 수 확인 (자기 자신 제외)
            int count = 0;
            var hits = Physics.OverlapBox(
                transform.position,
                _itemData.HalfSize * 0.9f, // 살짝 줄여서 벽 접촉은 무시
                transform.rotation,
                LayerMask.GetMask("Furniture")
            );
            foreach (var h in hits)
            {
                if (h.gameObject != gameObject) count++;
            }
            return count;
        }

        void UpdateSettleState()
        {
            float moved = Vector3.Distance(transform.position, _prevPos);
            if (moved < 0.001f)
                _stillFrames++;
            else
                _stillFrames = 0;

            _isSettled = _stillFrames >= settleFrames && IsInRoomBounds() && CountCollisions() == 0;
            _prevPos   = transform.position;
        }
    }
}
