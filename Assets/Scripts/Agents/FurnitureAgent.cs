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
    public class FurnitureAgent : Agent
    {
        public const int MAX_CATEGORIES = 24;
        public const int MAX_NEIGHBORS  = 5;
        public const int OBS_SIZE       = 8 + MAX_CATEGORIES + MAX_NEIGHBORS * 7 + 4 + 2; // 73

        [Header("Movement")]
        public float moveSpeed     = 0.05f;
        public float rotationSpeed = 5f;   // degrees per action unit per step

        [Header("Reward")]
        public float collisionPenalty   = -0.3f;
        public float outOfBoundsPenalty = -1.0f;
        public float settleReward       = +0.01f;
        public float timePenalty        = -0.005f;

        [Header("Settle Detection")]
        public int settleFrames = 30;

        // Data
        FurnitureItemData _itemData;
        RoomBounds _roomBounds;
        IReadOnlyList<FurnitureAgent> _allAgents;
        Collider _collider;

        // State
        Vector3 _prevPos;
        int _stillFrames;
        bool _isSettled;
        bool _isActive;

        public bool IsSettled => _isSettled || !_isActive;
        public FurnitureItemData ItemData => _itemData;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            _collider = GetComponent<Collider>();
            gameObject.layer = LayerMask.NameToLayer("Furniture");
        }

        // Called by FurnitureSpawner once after pool init
        public void SetNeighborList(IReadOnlyList<FurnitureAgent> allAgents)
        {
            _allAgents = allAgents;
        }

        // Called by TrainingEnvironment before each EndEpisode()
        public void Reassign(FurnitureItemData newData, RoomBounds newBounds)
        {
            _itemData  = newData;
            _roomBounds = newBounds;
            _isActive  = newData != null;

            if (_isActive)
            {
                transform.localScale = newData.FullSize;
                gameObject.layer = LayerMask.NameToLayer("Furniture");
            }
        }

        // ── ML-Agents overrides ────────────────────────────────────────────────

        public override void OnEpisodeBegin()
        {
            if (!_isActive || _itemData == null || _roomBounds == null)
            {
                transform.localPosition = new Vector3(0f, -100f, 0f);
                _isSettled = true;
                return;
            }

            var b = _roomBounds;
            float minX = b.minX + _itemData.sizeX;
            float maxX = b.maxX - _itemData.sizeX;
            float minZ = b.minZ + _itemData.sizeZ;
            float maxZ = b.maxZ - _itemData.sizeZ;

            // Fallback to center if room is too small for this item
            float rx = (minX < maxX) ? Random.Range(minX, maxX) : (b.minX + b.maxX) * 0.5f;
            float rz = (minZ < maxZ) ? Random.Range(minZ, maxZ) : (b.minZ + b.maxZ) * 0.5f;

            transform.localPosition = new Vector3(rx, _itemData.sizeY, rz);
            transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _stillFrames = 0;
            _isSettled   = false;
            _prevPos     = transform.localPosition;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (!_isActive || _itemData == null || _roomBounds == null)
            {
                for (int i = 0; i < OBS_SIZE; i++) sensor.AddObservation(0f);
                return;
            }

            var b = _roomBounds;
            float roomW = Mathf.Max(b.width,  0.1f);
            float roomD = Mathf.Max(b.depth,  0.1f);

            // Self (8)
            var p = transform.localPosition;
            float cx = (b.minX + b.maxX) * 0.5f;
            float cz = (b.minZ + b.maxZ) * 0.5f;
            float normX = (p.x - cx) / (roomW * 0.5f);
            float normZ = (p.z - cz) / (roomD * 0.5f);
            float angle = transform.eulerAngles.y * Mathf.Deg2Rad;
            sensor.AddObservation(normX);
            sensor.AddObservation(normZ);
            sensor.AddObservation(Mathf.Sin(angle));
            sensor.AddObservation(Mathf.Cos(angle));
            sensor.AddObservation(_itemData.sizeX / roomW);
            sensor.AddObservation(_itemData.sizeZ / roomD);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);

            // Category one-hot (24)
            for (int i = 0; i < MAX_CATEGORIES; i++)
                sensor.AddObservation(i == _itemData.categoryIndex ? 1f : 0f);

            // Nearest neighbors (5 × 7)
            var neighbors = GetNearestNeighbors(MAX_NEIGHBORS);
            for (int i = 0; i < MAX_NEIGHBORS; i++)
            {
                if (i < neighbors.Count && neighbors[i]._itemData != null)
                {
                    var n = neighbors[i];
                    Vector3 rel = transform.InverseTransformPoint(n.transform.position);
                    float na = n.transform.eulerAngles.y * Mathf.Deg2Rad;
                    sensor.AddObservation(rel.x / roomW);
                    sensor.AddObservation(rel.z / roomD);
                    sensor.AddObservation(Mathf.Sin(na));
                    sensor.AddObservation(Mathf.Cos(na));
                    sensor.AddObservation(n._itemData.sizeX / roomW);
                    sensor.AddObservation(n._itemData.sizeZ / roomD);
                    sensor.AddObservation(IsSameCategory(n) ? 1f : 0f);
                }
                else
                {
                    for (int j = 0; j < 7; j++) sensor.AddObservation(0f);
                }
            }

            // Wall distances (4) via raycast
            float maxRay = Mathf.Max(roomW, roomD);
            sensor.AddObservation(RaycastWall(Vector3.right,   maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.left,    maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.forward, maxRay) / maxRay);
            sensor.AddObservation(RaycastWall(Vector3.back,    maxRay) / maxRay);

            // Room size (2)
            sensor.AddObservation(roomW);
            sensor.AddObservation(roomD);
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!_isActive || _itemData == null || _roomBounds == null) return;

            var ca = actionBuffers.ContinuousActions;
            float dx = ca[0] * moveSpeed;
            float dz = ca[1] * moveSpeed;
            float dr = ca[2] * rotationSpeed;

            Vector3 newPos = transform.localPosition + new Vector3(dx, 0f, dz);
            newPos.y = _itemData.sizeY;
            transform.localPosition = newPos;
            transform.Rotate(0f, dr, 0f);

            float reward = timePenalty;

            bool inBounds = IsInRoomBounds();
            if (!inBounds)
            {
                reward += outOfBoundsPenalty;
                ClampToBounds();
            }

            int collisions = CountCollisions();
            if (collisions > 0)
                reward += collisionPenalty * collisions;

            if (inBounds && collisions == 0)
                reward += settleReward;

            AddReward(reward);
            UpdateSettleState();
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
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

        // ── Helpers ───────────────────────────────────────────────────────────

        List<FurnitureAgent> GetNearestNeighbors(int count)
        {
            var ranked = new List<(float dist, FurnitureAgent a)>();
            if (_allAgents == null) return new List<FurnitureAgent>();
            foreach (var other in _allAgents)
            {
                if (other == this || !other._isActive) continue;
                ranked.Add((Vector3.Distance(transform.position, other.transform.position), other));
            }
            ranked.Sort((a, b) => a.dist.CompareTo(b.dist));

            var result = new List<FurnitureAgent>();
            for (int i = 0; i < Mathf.Min(count, ranked.Count); i++)
                result.Add(ranked[i].a);
            return result;
        }

        bool IsSameCategory(FurnitureAgent other) =>
            _itemData.category == other._itemData.category;

        float RaycastWall(Vector3 dir, float maxDist)
        {
            int mask = LayerMask.GetMask("Wall");
            return Physics.Raycast(transform.position, dir, out var hit, maxDist, mask)
                ? hit.distance : maxDist;
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
            var hits = Physics.OverlapBox(
                transform.position,
                _itemData.HalfSize * 0.9f,
                transform.rotation,
                LayerMask.GetMask("Furniture")
            );
            int count = 0;
            foreach (var h in hits)
                if (h.gameObject != gameObject) count++;
            return count;
        }

        void UpdateSettleState()
        {
            if (!_isActive) return;
            float moved = Vector3.Distance(transform.localPosition, _prevPos);
            _stillFrames = moved < 0.001f ? _stillFrames + 1 : 0;
            _isSettled   = _stillFrames >= settleFrames && IsInRoomBounds() && CountCollisions() == 0;
            _prevPos     = transform.localPosition;
        }
    }
}
