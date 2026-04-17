using System;
using UnityEngine;

namespace SceneSynthesis.Data
{
    [Serializable]
    public class FurnitureItemData
    {
        public string uid;
        public string jid;
        public string category;
        public int categoryIndex;
        public float posX, posY, posZ;
        public float sizeX, sizeY, sizeZ;
        public float angle; // Y축 회전 (라디안)

        public Vector3 Position => new Vector3(posX, posY, posZ);
        public Vector3 HalfSize => new Vector3(sizeX, sizeY, sizeZ);
        public Vector3 FullSize => new Vector3(sizeX * 2f, sizeY * 2f, sizeZ * 2f);
    }

    [Serializable]
    public class RoomBounds
    {
        public float minX, maxX, minZ, maxZ, width, depth;
    }

    [Serializable]
    public class RoomData
    {
        public float[][] vertices; // [[x,z], ...]
        public float[] centroid;   // [x, z]
        public RoomBounds bounds;
    }

    [Serializable]
    public class SceneData
    {
        public string sceneId;
        public string sceneType;
        public RoomData room;
        public FurnitureItemData[] objects;
    }

    [Serializable]
    public class RoomStats
    {
        public string roomType;
        public string[] classLabels;
        public float[] boundsTranslations; // [minX,minY,minZ,maxX,maxY,maxZ]
        public float[] boundsSizes;
        public float[] boundsAngles;
    }

    [Serializable]
    public class SceneCatalog
    {
        public string[] bedroom;
        public string[] livingroom;
        public string[] diningroom;
        public string[] library;

        public string[] GetSceneIds(string roomType) => roomType switch
        {
            "bedroom"    => bedroom,
            "livingroom" => livingroom,
            "diningroom" => diningroom,
            "library"    => library,
            _            => Array.Empty<string>(),
        };
    }

    [Serializable]
    public class ModelInfo
    {
        public string superCategory;
        public string category;
        public string style;
        public string modelPath;
    }
}
