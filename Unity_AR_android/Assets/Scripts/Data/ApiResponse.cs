using System;

namespace ParkInspect.Data
{
    /// <summary>创建成功后服务端返回的 Issue 关键字段（完整字段按需扩展）。</summary>
    [Serializable]
    public class IssueResult
    {
        public string id;
        public string status;
        public string createdAt;
    }

    /// <summary>
    /// 统一响应壳（契约：{code,message,data}，code==0 成功）。
    /// JsonUtility 不支持泛型与 object，data 固定为 IssueResult：
    /// 错误响应 data 可能缺省（解析为 null）或为其他对象（多余字段被忽略），均安全。
    /// </summary>
    [Serializable]
    public class ApiResponse
    {
        public int code;
        public string message;
        public IssueResult data;

        public bool IsSuccess => code == 0;
    }
}
