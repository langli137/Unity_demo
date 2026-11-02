package handler

import (
	"encoding/csv"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

// ---------- A4 删除 ----------

// DeleteIssue DELETE /api/v1/issues/{id}（admin only，角色校验在 router 包的 RequireRole）。
func (h *IssueHandler) DeleteIssue(w http.ResponseWriter, r *http.Request) {
	if err := h.svc.DeleteIssue(r.Context(), r.PathValue("id")); err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, nil)
}

// ---------- B6 照片 ----------

// UploadPhoto POST /api/v1/issues/{id}/photo（raw body 即图片字节，Content-Type: image/jpeg）。
// Unity 端用 UploadHandlerRaw 直传，避免 multipart 拼装。
func (h *IssueHandler) UploadPhoto(w http.ResponseWriter, r *http.Request) {
	r.Body = http.MaxBytesReader(w, r.Body, 9<<20) // 略大于 service 上限，防超大 body 直接打爆内存
	data, err := io.ReadAll(r.Body)
	if err != nil {
		writeError(w, http.StatusBadRequest, "read photo body failed")
		return
	}
	if err := h.svc.SaveIssuePhoto(r.Context(), r.PathValue("id"), data); err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, nil)
}

// GetPhoto GET /api/v1/issues/{id}/photo：返回图片字节（image/jpeg）。
// 注意：此接口直接写字节流，不套 {code,message} 响应壳（浏览器 <img> 直接消费）。
func (h *IssueHandler) GetPhoto(w http.ResponseWriter, r *http.Request) {
	data, err := h.svc.GetIssuePhoto(r.Context(), r.PathValue("id"))
	if err != nil {
		writeServiceError(w, err)
		return
	}
	w.Header().Set("Content-Type", "image/jpeg")
	w.Header().Set("Cache-Control", "private, max-age=300")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(data)
}

// ---------- B7 智能优先级建议 ----------

type suggestPriorityRequest struct {
	Title       string `json:"title"`
	Description string `json:"description"`
}

type suggestPriorityResponse struct {
	Priority model.Priority `json:"priority"`
	Reason   string         `json:"reason"`
	Matched  []string       `json:"matched"`
}

// SuggestPriority POST /api/v1/issues/suggest-priority：规则引擎建议，纯计算不落库。
func (h *IssueHandler) SuggestPriority(w http.ResponseWriter, r *http.Request) {
	var req suggestPriorityRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	res := h.svc.SuggestPriority(req.Title, req.Description)
	writeOK(w, suggestPriorityResponse{Priority: res.Priority, Reason: res.Reason, Matched: res.Matched})
}
