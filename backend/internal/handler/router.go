package handler

import (
	"net/http"

	"park-inspect/internal/middleware"
)

// NewRouter 固定 API 清单（扩展需用户明确批准；v1.2 用户要求"增加编辑功能"新增 PATCH 字段编辑）。
func NewRouter(h *IssueHandler, allowedOrigin string) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("POST /api/v1/issues", h.CreateIssue)
	mux.HandleFunc("GET /api/v1/issues", h.ListIssues)
	mux.HandleFunc("GET /api/v1/issues/{id}", h.GetIssue)
	// 两条 PATCH 互不冲突：/status 命中更长路径规则；短路径 /{id} 为字段编辑。
	mux.HandleFunc("PATCH /api/v1/issues/{id}", h.UpdateIssue)
	mux.HandleFunc("PATCH /api/v1/issues/{id}/status", h.UpdateIssueStatus)
	mux.HandleFunc("GET /api/v1/health", func(w http.ResponseWriter, r *http.Request) {
		writeOK(w, map[string]string{"status": "up"})
	})

	var handler http.Handler = mux
	handler = middleware.CORS(allowedOrigin, handler)
	handler = middleware.Recovery(handler)
	handler = middleware.Logging(handler)
	return handler
}
