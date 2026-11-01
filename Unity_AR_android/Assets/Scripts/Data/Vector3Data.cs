using System;

namespace ParkInspect.Data
{
    /// <summary>
    /// 三维向量。字段名与 JSON 契约一致（x/y/z），JsonUtility 直接序列化。
    /// </summary>
    [Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;

        public Vector3Data() { }

        public Vector3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
