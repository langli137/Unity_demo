package service

import (
	"context"
	"errors"
	"strings"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

// ErrPhotoTooLarge 照片上限 8MB（手机截图 JPEG 通常 200KB~2MB）。
var ErrPhotoTooLarge = errors.New("photo too large (max 8MB)")

const maxPhotoBytes = 8 << 20

// DeleteIssue 删除问题（A4，仅 admin——角色校验在 middleware/handler 层）。
// 级联删照片在 sqlite 实现内完成；mem 实现照片独立（单测各自覆盖）。
func (s *IssueService) DeleteIssue(ctx context.Context, id string) error {
	deleted, err := s.repo.Delete(ctx, id)
	if err != nil {
		return err
	}
	if !deleted {
		return ErrNotFound
	}
	return nil
}

// SaveIssuePhoto 上传/覆盖问题照片（B6）。问题必须已存在（404 兜底）。
func (s *IssueService) SaveIssuePhoto(ctx context.Context, id string, data []byte) error {
	if s.photos == nil {
		return ErrValidation
	}
	if len(data) == 0 {
		return ErrValidation
	}
	if len(data) > maxPhotoBytes {
		return ErrPhotoTooLarge
	}
	if _, err := s.repo.GetByID(ctx, id); err != nil {
		if errors.Is(err, repository.ErrNotFound) {
			return ErrNotFound
		}
		return err
	}
	return s.photos.SavePhoto(ctx, id, data)
}

// GetIssuePhoto 读取照片字节；无照片 → ErrNotFound（handler 映射 404）。
func (s *IssueService) GetIssuePhoto(ctx context.Context, id string) ([]byte, error) {
	if s.photos == nil {
		return nil, ErrNotFound
	}
	data, err := s.photos.GetPhoto(ctx, id)
	if errors.Is(err, repository.ErrNotFound) {
		return nil, ErrNotFound
	}
	return data, err
}

// HasIssuePhoto 供列表/详情标记 photoUrl 是否存在（避免前端盲发图片请求）。
func (s *IssueService) HasIssuePhoto(ctx context.Context, id string) (bool, error) {
	if s.photos == nil {
		return false, nil
	}
	return s.photos.HasPhoto(ctx, id)
}

// ---------- B7 智能优先级建议（纯规则引擎，无外部依赖） ----------

// priorityRule 关键词 → 优先级规则：按 severity 从高到低匹配，命中即返回。
type priorityRule struct {
	severity int // 2=high 1=medium 0=low（取最高命中）
	priority model.Priority
	keywords []string
}

// 规则来源：巡检业务常识（结构安全/人身风险=high，功能缺陷=medium，外观=low）。
// 关键词表集中在一处，业务方增删只改这里。
var priorityRules = []priorityRule{
	{2, model.PriorityHigh, []string{
		"坍塌", "倒塌", "断裂", "裂开", "漏电", "触电", "起火", "冒烟", "燃气", "浓烟",
		"承重", "钢筋外露", "沉降", "倾斜", "危房", "爆炸", "高处坠落", "基坑",
	}},
	{1, model.PriorityMedium, []string{
		"裂缝", "渗水", "漏水", "破损", "松动", "脱落", "锈蚀", "变形", "异响",
		"缺损", "玻璃碎", "护栏", "疏散", "应急灯", "灭火器", "堵塞", "翘起",
	}},
	{0, model.PriorityLow, []string{
		"污渍", "褪色", "掉漆", "划痕", "杂草", "垃圾", "标识不清", "歪斜",
		"积尘", "渗灰", "轻微", "美观",
	}},
}

// SuggestPriorityResult 建议产物：命中关键词一并返回，便于前端解释"为什么建议这个"。
type SuggestPriorityResult struct {
	Priority model.Priority `json:"priority"`
	Reason   string         `json:"reason"`
	Matched  []string       `json:"matched"`
}

// SuggestPriority 规则引擎：对 标题+描述 分词（子串匹配），取命中规则中 severity 最高者。
// 无任何命中 → medium 兜底（宁可提醒多看一眼），reason 说明"未命中关键词"。
func (s *IssueService) SuggestPriority(title, description string) SuggestPriorityResult {
	text := strings.ToLower(title + " " + description)
	best := -1
	var matched []string
	var prio model.Priority

	for _, rule := range priorityRules {
		var hit []string
		for _, kw := range rule.keywords {
			if strings.Contains(text, kw) {
				hit = append(hit, kw)
			}
		}
		if len(hit) > 0 && rule.severity > best {
			best = rule.severity
			prio = rule.priority
			matched = hit
		}
	}

	if best < 0 {
		return SuggestPriorityResult{
			Priority: model.PriorityMedium,
			Reason:   "未命中风险关键词，按中优先级建议，请人工确认",
			Matched:  nil,
		}
	}
	reason := "命中关键词：" + strings.Join(matched, "、")
	switch prio {
	case model.PriorityHigh:
		reason += "（涉及结构/人身安全风险）"
	case model.PriorityMedium:
		reason += "（影响使用功能）"
	default:
		reason += "（外观/轻微问题）"
	}
	return SuggestPriorityResult{Priority: prio, Reason: reason, Matched: matched}
}
