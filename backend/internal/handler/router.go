package handler

import (
	"net/http"

	"park-inspect/internal/middleware"
	"park-inspect/internal/model"
)

// NewRouter 固定 API 清单。
// v1.3（A1）：除 /health 与 /auth/login 外全部要求 Authorization: Bearer <jwt>；
// DELETE 仅 admin。角色校验顺序：RequireAuth（外层，写入 context）→ RequireRole（内层）。
func NewRouter(h *IssueHandler, ah *AuthHandler, jwtSecret, allowedOrigin string) http.Handler {
	mux := http.NewServeMux()

	// 公开端：健康检查、登录。
	mux.HandleFunc("GET /api/v1/health", func(w http.ResponseWriter, r *http.Request) {
		writeOK(w, map[string]string{"status": "up"})
	})
	mux.HandleFunc("POST /api/v1/auth/login", ah.Login)

	authed := func(minRole model.Role, fn http.HandlerFunc) http.Handler {
		// 顺序关键：RequireAuth 在外（解析令牌写入 context），RequireRole 在内（读 context 判角色）。
		return middleware.RequireAuth(jwtSecret, middleware.RequireRole(minRole, fn))
	}

	// 需登录端点（inspector 及以上）。
	mux.Handle("POST /api/v1/issues", authed(model.RoleInspector, h.CreateIssue))
	mux.Handle("GET /api/v1/issues", authed(model.RoleInspector, h.ListIssues))
	mux.Handle("GET /api/v1/issues/{id}", authed(model.RoleInspector, h.GetIssue))
	// 两条 PATCH 互不冲突：/status 命中更长路径规则；短路径 /{id} 为字段编辑。
	mux.Handle("PATCH /api/v1/issues/{id}", authed(model.RoleInspector, h.UpdateIssue))
	mux.Handle("PATCH /api/v1/issues/{id}/status", authed(model.RoleInspector, h.UpdateIssueStatus))
	// 仅 admin：删除。
	mux.Handle("DELETE /api/v1/issues/{id}", authed(model.RoleAdmin, h.DeleteIssue))

	var handler http.Handler = mux
	handler = middleware.CORS(allowedOrigin, handler)
	handler = middleware.Recovery(handler)
	handler = middleware.Logging(handler)
	return handler
}
