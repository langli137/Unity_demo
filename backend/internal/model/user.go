package model

// Role 角色（A1 登录与角色权限）：admin 可删除，inspector 仅上报/查看/编辑。
type Role string

const (
	RoleAdmin     Role = "admin"
	RoleInspector Role = "inspector"
)

func ValidRole(r Role) bool {
	return r == RoleAdmin || r == RoleInspector
}

// User 登录用户（不含口令散列——散列只在 repository/auth 层出现）。
type User struct {
	Username    string `json:"username"`
	DisplayName string `json:"displayName"`
	Role        Role   `json:"role"`
}
