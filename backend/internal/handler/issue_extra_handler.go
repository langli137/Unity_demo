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

// ---------- B8 CSV 导出 ----------

// ExportCSV GET /api/v1/issues/export.csv?status=&category=&priority=&from=2026-01-01&to=2026-01-31
// 按筛选条件导出全量巡检报表（UTF-8 BOM，Excel 直接可读）。
func (h *IssueHandler) ExportCSV(w http.ResponseWriter, r *http.Request) {
	q := r.URL.Query()
	since, err := parseDateMs(q.Get("from"), false)
	if err != nil {
		writeError(w, http.StatusBadRequest, "from 日期格式非法（应为 YYYY-MM-DD）")
		return
	}
	until, err := parseDateMs(q.Get("to"), true)
	if err != nil {
		writeError(w, http.StatusBadRequest, "to 日期格式非法（应为 YYYY-MM-DD）")
		return
	}
	f := repository.ListFilter{
		Status:   model.Status(q.Get("status")),
		Category: model.Category(q.Get("category")),
		Priority: model.Priority(q.Get("priority")),
		Since:    since,
		Until:    until,
	}
	items, err := h.svc.ListAllForExport(r.Context(), f)
	if err != nil {
		writeServiceError(w, err)
		return
	}

	// UTF-8 BOM：Excel 打开中文不乱码。
	w.Header().Set("Content-Type", "text/csv; charset=utf-8")
	w.Header().Set("Content-Disposition",
		fmt.Sprintf(`attachment; filename="inspection_%s.csv"`, time.Now().Format("20060102_150405")))
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("\xEF\xBB\xBF"))

	cw := csv.NewWriter(w)
	defer cw.Flush()
	_ = cw.Write([]string{
		"ID", "标题", "描述", "分类", "优先级", "状态",
		"位置X", "位置Y", "位置Z", "平面ID", "上报人",
		"上报时间", "更新时间", "解决时间",
	})
	for _, it := range items {
		_ = cw.Write([]string{
			it.ID, it.Title, it.Description,
			categoryLabel(it.Category), priorityLabel(it.Priority), statusLabel(it.Status),
			strconv.FormatFloat(it.Position.X, 'f', 3, 64),
			strconv.FormatFloat(it.Position.Y, 'f', 3, 64),
			strconv.FormatFloat(it.Position.Z, 'f', 3, 64),
			it.Anchor.PlaneID, it.ReporterName,
			formatTime(it.CreatedAt), formatTime(it.UpdatedAt),
			formatNullableTime(it.ResolvedAt),
		})
	}
}

// parseDateMs 解析 YYYY-MM-DD → 当日 Unix 毫秒；endOfDay=true 时取当日 23:59:59.999。
// 空串返回 0（不限）；格式非法返回 error。
func parseDateMs(s string, endOfDay bool) (int64, error) {
	s = strings.TrimSpace(s)
	if s == "" {
		return 0, nil
	}
	t, err := time.ParseInLocation("2006-01-02", s, time.Local)
	if err != nil {
		return 0, err
	}
	if endOfDay {
		t = t.Add(24*time.Hour - time.Millisecond)
	}
	return t.UnixMilli(), nil
}

func formatTime(t time.Time) string { return t.Local().Format("2006-01-02 15:04:05") }

func formatNullableTime(t *time.Time) string {
	if t == nil {
		return ""
	}
	return formatTime(*t)
}

// CSV 枚举列直接写中文标签（报表读者是业务人员）。
func categoryLabel(c model.Category) string {
	switch c {
	case model.CategoryWallDamage:
		return "墙面破损"
	case model.CategoryEquipmentFault:
		return "设备异常"
	case model.CategorySafetyHazard:
		return "安全隐患"
	default:
		return "其他"
	}
}

func priorityLabel(p model.Priority) string {
	switch p {
	case model.PriorityHigh:
		return "高"
	case model.PriorityMedium:
		return "中"
	default:
		return "低"
	}
}

func statusLabel(s model.Status) string {
	switch s {
	case model.StatusOpen:
		return "待处理"
	case model.StatusInProgress:
		return "处理中"
	default:
		return "已解决"
	}
}

