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
}
