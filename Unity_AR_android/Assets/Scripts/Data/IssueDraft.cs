using System;

namespace ParkInspect.Data
{
    /// <summary>
    /// 上报问题的请求体（契约：POST /api/v1/issues，camelCase）。
    /// 字段名与后端 createIssueRequest 的 json tag 逐字一致；
    /// id/status/createdAt/updatedAt 由服务端生成，客户端不得发送。
    /// location 为可选字段，Unity 端不采集 GPS，故不包含（后端解出零值）。
    /// 需求文档 v1.1 D3/D6：必填仅 title、priority、position；
    /// category 选填（缺省 other），anchor.planeId 选填（便于编辑器调试）。
    /// </summary>
    [Serializable]
    public class IssueDraft
    {
        public string title;
        public string description;
        /// <summary>wall_damage | equipment_fault | safety_hazard | other（选填，缺省 other）</summary>
        public string category = "other";
        /// <summary>low | medium | high</summary>
        public string priority;
        public Vector3Data position = new Vector3Data();
        public AnchorData anchor = new AnchorData();
        public string reporterName;

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                error = "标题不能为空";
                return false;
            }
            // category 选填：未采集时保持 other；提供则必须是合法枚举（非法值后端也会 400）。
            if (!string.IsNullOrEmpty(category) &&
                category != "wall_damage" && category != "equipment_fault" &&
                category != "safety_hazard" && category != "other")
            {
                error = "分类不合法";
                return false;
            }
            if (priority != "low" && priority != "medium" && priority != "high")
            {
                error = "优先级不合法";
                return false;
            }
            // anchor.planeId 选填，不再作为提交门槛（Q3 已决策）。
            error = null;
            return true;
        }
    }
}
