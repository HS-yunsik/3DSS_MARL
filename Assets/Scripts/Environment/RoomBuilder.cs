using UnityEngine;
using SceneSynthesis.Data;

namespace SceneSynthesis.Environment
{
    /// <summary>
    /// RoomData의 floor_plan_vertices를 바탕으로 벽과 바닥을 생성.
    /// 좌표계: ATISS translations은 방 centroid 기준 상대좌표 (미터 단위).
    /// Unity씬에서 TrainingEnvironment 위치를 원점으로 사용.
    /// </summary>
    public class RoomBuilder : MonoBehaviour
    {
        [Header("Dimensions")]
        public float wallHeight = 3.0f;
        public float wallThickness = 0.2f;

        [Header("Materials")]
        public Material wallMaterial;
        public Material floorMaterial;

        GameObject _wallParent;
        GameObject _floor;

        public RoomBounds CurrentBounds { get; private set; }

        public void Build(RoomData roomData)
        {
            Clear();
            CurrentBounds = roomData.bounds;

            _wallParent = new GameObject("Walls");
            _wallParent.transform.SetParent(transform, false);

            BuildFloor(roomData.bounds);
            BuildWalls(roomData.bounds);
        }

        public void Clear()
        {
            if (_wallParent != null) DestroyImmediate(_wallParent);
            if (_floor != null)     DestroyImmediate(_floor);
        }

        void BuildFloor(RoomBounds b)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "Floor";
            _floor.transform.SetParent(transform, false);
            _floor.transform.localPosition = new Vector3(
                (b.minX + b.maxX) * 0.5f,
                -0.05f,
                (b.minZ + b.maxZ) * 0.5f
            );
            _floor.transform.localScale = new Vector3(b.width + wallThickness, 0.1f, b.depth + wallThickness);

            SetupColliderAndMaterial(_floor, floorMaterial, "Floor");
        }

        void BuildWalls(RoomBounds b)
        {
            float cx = (b.minX + b.maxX) * 0.5f;
            float cz = (b.minZ + b.maxZ) * 0.5f;
            float halfH = wallHeight * 0.5f;

            // 남(+Z), 북(-Z), 동(+X), 서(-X)
            CreateWall("Wall_North", new Vector3(cx, halfH, b.minZ),
                new Vector3(b.width + wallThickness, wallHeight, wallThickness));
            CreateWall("Wall_South", new Vector3(cx, halfH, b.maxZ),
                new Vector3(b.width + wallThickness, wallHeight, wallThickness));
            CreateWall("Wall_West",  new Vector3(b.minX, halfH, cz),
                new Vector3(wallThickness, wallHeight, b.depth));
            CreateWall("Wall_East",  new Vector3(b.maxX, halfH, cz),
                new Vector3(wallThickness, wallHeight, b.depth));
        }

        void CreateWall(string wallName, Vector3 localPos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = wallName;
            wall.transform.SetParent(_wallParent.transform, false);
            wall.transform.localPosition = localPos;
            wall.transform.localScale    = scale;
            SetupColliderAndMaterial(wall, wallMaterial, "Wall");
        }

        static void SetupColliderAndMaterial(GameObject go, Material mat, string layerName)
        {
            if (mat != null)
                go.GetComponent<Renderer>().material = mat;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) go.layer = layer;
        }
    }
}
