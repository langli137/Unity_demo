// Package handler 只做 HTTP 编解码与错误映射，不写业务判断。
package handler

import (
	"encoding/json"
	"errors"
	"net/http"
	"strconv"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
	"park-inspect/internal/service"
)

type IssueHandler struct {
	svc *service.IssueService
}

func NewIssueHandler(svc *service.IssueService) *IssueHandler {
	return &IssueHandler{svc: svc}
}

// 请求结构体就地定义在 handler 包（修正①：不设独立 dto 包）。
type createIssueRequest struct {
	Title        string         `json:"title"`
	Description  string         `json:"description"`
	Category     model.Category `json:"category"`
	Priority     model.Priority `json:"priority"`
	Position     model.Vector3  `json:"position"`
	Anchor       model.Anchor   `json:"anchor"`
	Location     model.Location `json:"location"`
	ReporterName string         `json:"reporterName"`
}

type updateStatusRequest struct {
	Status model.Status `json:"status"`
}

// updateIssueRequest 字段编辑请求体：仅含可编辑字段，position/anchor/status 不接受修改。
type updateIssueRequest struct {
	Title        string         `json:"title"`
	Description  string         `json:"description"`
	Category     model.Category `json:"category"`
	Priority     model.Priority `json:"priority"`
	ReporterName string         `json:"reporterName"`
}

type apiResponse struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
	Data    any    `json:"data,omitempty"`
}

type listData struct {
	Items    []*model.Issue `json:"items"`
	Total    int            `json:"total"`
	Page     int            `json:"page"`
	PageSize int            `json:"pageSize"`
}

func (h *IssueHandler) CreateIssue(w http.ResponseWriter, r *http.Request) {
	var req createIssueRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	issue, err := h.svc.CreateIssue(r.Context(), service.CreateIssueInput{
		Title:        req.Title,
		Description:  req.Description,
		Category:     req.Category,
		Priority:     req.Priority,
		Position:     req.Position,
		Anchor:       req.Anchor,
		Location:     req.Location,
		ReporterName: req.ReporterName,
	})
	if err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, issue)
}

func (h *IssueHandler) ListIssues(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	f := repository.ListFilter{
		Status:   model.Status(q.Get("status")),
		Category: model.Category(q.Get("category")),
		Page:     atoiOr(q.Get("page"), 1),
		PageSize: atoiOr(q.Get("pageSize"), 20),
	}
	items, total, err := h.svc.ListIssues(r.Context(), f)
	if err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, listData{Items: items, Total: total, Page: f.Page, PageSize: f.PageSize})
}

func (h *IssueHandler) GetIssue(w http.ResponseWriter, r *http.Request) {
	issue, err := h.svc.GetIssue(r.Context(), r.PathValue("id"))
	if err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, issue)
}

// UpdateIssue 字段编辑：PATCH /api/v1/issues/{id}。
// 只覆盖标题/描述/分类/优先级/上报人；状态流转走 UpdateIssueStatus。
func (h *IssueHandler) UpdateIssue(w http.ResponseWriter, r *http.Request) {
	var req updateIssueRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	issue, err := h.svc.UpdateIssue(r.Context(), r.PathValue("id"), service.UpdateIssueInput{
		Title:        req.Title,
		Description:  req.Description,
		Category:     req.Category,
		Priority:     req.Priority,
		ReporterName: req.ReporterName,
	})
	if err != nil {
		writeServiceError(w, err)
		return
	}
	writeOK(w, issue)
}

func (h *IssueHandler) UpdateIssueStatus(w http.ResponseWriter, r *http.Request) {
	var req updateStatusRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	issue, err := h.svc.UpdateStatus(r.Context(), r.PathValue("id"), req.Status)
	if err != nil {
		// 409 时把最新状态带回，前端刷新后重试。
		if errors.Is(err, service.ErrConflict) && issue != nil {
			writeJSON(w, http.StatusConflict, apiResponse{
				Code: http.StatusConflict, Message: service.ErrConflict.Error(), Data: issue,
			})
			return
		}
		writeServiceError(w, err)
		return
	}
	writeOK(w, issue)
}

// writeServiceError 领域错误 → HTTP 状态码的唯一映射处。
// v1.1：409 仅表示并发冲突；枚举/参数非法一律 400（不再有"非法流转"错误）。
func writeServiceError(w http.ResponseWriter, err error) {
	switch {
	case errors.Is(err, service.ErrValidation):
		writeError(w, http.StatusBadRequest, err.Error())
	case errors.Is(err, service.ErrNotFound):
		writeError(w, http.StatusNotFound, err.Error())
	case errors.Is(err, service.ErrConflict):
		writeError(w, http.StatusConflict, err.Error())
	default:
		writeError(w, http.StatusInternalServerError, "internal error")
	}
}

func writeOK(w http.ResponseWriter, data any) {
	writeJSON(w, http.StatusOK, apiResponse{Code: 0, Message: "ok", Data: data})
}

func writeError(w http.ResponseWriter, status int, msg string) {
	writeJSON(w, status, apiResponse{Code: status, Message: msg})
}

func writeJSON(w http.ResponseWriter, status int, body apiResponse) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func atoiOr(s string, def int) int {
	if s == "" {
		return def
	}
	n, err := strconv.Atoi(s)
	if err != nil {
		return def
	}
	return n
}
