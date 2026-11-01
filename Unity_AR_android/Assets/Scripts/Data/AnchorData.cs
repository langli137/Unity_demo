using System;

namespace ParkInspect.Data
{
    /// <summary>
    /// AR 锚点扩展信息（契约：anchor{rotation{x,y,z}, planeId}）。
    /// 位置在 IssueDraft 顶层 position 字段，不放在 anchor 里。
    /// </summary>
    [Serializable]
    public class AnchorData
    {
        public Vector3Data rotation = new Vector3Data();
        public string planeId;
    }
}
