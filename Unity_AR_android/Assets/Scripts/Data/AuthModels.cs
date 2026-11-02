using System;

namespace ParkInspect.Data
{
    /// <summary>登录用户信息（契约：POST /api/v1/auth/login 响应 data.user）。</summary>
    [Serializable]
    public class UserData
    {
        public string username;
        public string displayName;
        /// <summary>admin | inspector</summary>
        public string role;
    }

    /// <summary>登录成功后的 data：{token, user}（A1）。</summary>
    [Serializable]
    public class LoginData
    {
        public string token;
        public UserData user;
    }

    /// <summary>登录请求体 {username, password}。</summary>
    [Serializable]
    public class LoginRequest
    {
        public string username;
        public string password;

        public LoginRequest(string username, string password)
        {
            this.username = username;
            this.password = password;
        }
    }

    /// <summary>
    /// 登录响应壳（JsonUtility 不支持泛型，登录/列表与创建响应各自定壳，
    /// data 类型不同不能共用 ApiResponse）。
    /// </summary>
    [Serializable]
    public class LoginResponse
    {
        public int code;
        public string message;
        public LoginData data;
    }

    /// <summary>
    /// 历史问题列表项（契约：GET /api/v1/issues 的 items 元素，字段取自 model.Issue 的子集）。
    /// 仅取 AR 端展示/回显所需字段；多余字段 JsonUtility 自动忽略。
    /// </summary>
    [Serializable]
    public class IssueListItem
    {
        public string id;
        public string title;
        public string description;
        /// <summary>wall_damage | equipment_fault | safety_hazard | other</summary>
        public string category;
        /// <summary>low | medium | high</summary>
        public string priority;
        /// <summary>open | in_progress | resolved</summary>
        public string status;
        public Vector3Data position;
        public string reporterName;
        /// <summary>RFC3339 时间串，展示时截取前 16 位即可。</summary>
        public string createdAt;
    }

    /// <summary>列表响应 data：{items,total,page,pageSize}。</summary>
    [Serializable]
    public class IssueListData
    {
        public IssueListItem[] items;
        public int total;
        public int page;
        public int pageSize;
    }

    /// <summary>列表响应壳。</summary>
    [Serializable]
    public class IssueListResponse
    {
        public int code;
        public string message;
        public IssueListData data;
    }
}
