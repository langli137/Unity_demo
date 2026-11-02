// Package service 承载业务规则与状态机，不接触 HTTP，不拼 SQL。
package service

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"
	"strings"
	"time"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

// 领域错误，由 handler 统一映射 HTTP 状态码。
// v1.1（Q1）：不再有"非法流转"错误——三态任意互转与自转均合法（200），
// 409 仅表示并发冲突；枚举非法属参数错误（400）。
var (
	ErrValidation = errors.New("validation failed")
	ErrNotFound   = errors.New("issue not found")
	ErrConflict   = errors.New("status changed by others")
)

type IssueService struct {
	repo   repository.IssueRepository
	photos repository.PhotoRepository // B6 照片留档；nil 时照片操作返回验证错误（单测兼容）
	now    func() time.Time
}

func NewIssueService(repo repository.IssueRepository) *IssueService {
	return &IssueService{repo: repo, now: time.Now}
}

// WithPhotos 注入照片仓储（装配处调用；返回自身便于链式）。
func (s *IssueService) WithPhotos(p repository.PhotoRepository) *IssueService {
	s.photos = p
	return s
}

type CreateIssueInput struct {
	Title        string
	Description  string
	Category     model.Category
	Priority     model.Priority
	Position     model.Vector3
	Anchor       model.Anchor
	Location     model.Location
	ReporterName string
}

func (s *IssueService) CreateIssue(ctx context.Context, in CreateIssueInput) (*model.Issue, error) {
	in.Title = strings.TrimSpace(in.Title)
	if in.Title == "" {
		return nil, ErrValidation
	}
	// D3（Q2 已决策）：category 选填，缺省 other；提供非法值仍 400。
	if in.Category == "" {
		in.Category = model.CategoryOther
	}
	if !model.ValidCategory(in.Category) {
		return nil, ErrValidation
	}
	if !model.ValidPriority(in.Priority) {
		return nil, ErrValidation
	}
	// D3（Q3 已决策）：anchor.planeId 选填，不再作为提交门槛。

	now := s.now().UTC()
	issue := &model.Issue{
		ID:           newID(),
		Title:        in.Title,
		Description:  in.Description,
		Category:     in.Category,
		Priority:     in.Priority,
		Status:       model.StatusOpen,
		Position:     in.Position,
		Anchor:       in.Anchor,
		Location:     in.Location,
		ReporterName: in.ReporterName,
		CreatedAt:    now,
		UpdatedAt:    now,
	}
	if err := s.repo.Create(ctx, issue); err != nil {
		return nil, err
	}
	return issue, nil
}

func (s *IssueService) ListIssues(ctx context.Context, f repository.ListFilter) ([]*model.Issue, int, error) {
	if f.Status != "" && !model.ValidStatus(f.Status) {
		return nil, 0, ErrValidation
	}
	if f.Category != "" && !model.ValidCategory(f.Category) {
		return nil, 0, ErrValidation
	}
	if f.Priority != "" && !model.ValidPriority(f.Priority) {
		return nil, 0, ErrValidation
	}
	if f.Since < 0 || f.Until < 0 || (f.Since > 0 && f.Until > 0 && f.Since > f.Until) {
		return nil, 0, ErrValidation
	}
	if f.Page < 1 {
		f.Page = 1
	}
	if f.PageSize < 1 || f.PageSize > 100 {
		f.PageSize = 20
	}
	return s.repo.List(ctx, f)
}

// ListAllForExport B8 报表导出：绕过分页钳制（pageSize 上限 100 不够导出全量），
// 但仍走 ListFilter 校验与筛选；单次导出硬上限 10000 条防 OOM。
func (s *IssueService) ListAllForExport(ctx context.Context, f repository.ListFilter) ([]*model.Issue, error) {
	if f.Status != "" && !model.ValidStatus(f.Status) {
		return nil, ErrValidation
	}
	if f.Category != "" && !model.ValidCategory(f.Category) {
		return nil, ErrValidation
	}
	if f.Priority != "" && !model.ValidPriority(f.Priority) {
		return nil, ErrValidation
	}
	if f.Since < 0 || f.Until < 0 || (f.Since > 0 && f.Until > 0 && f.Since > f.Until) {
		return nil, ErrValidation
	}
	f.Page = 1
	f.PageSize = 10000
	items, _, err := s.repo.List(ctx, f)
	return items, err
}

func (s *IssueService) GetIssue(ctx context.Context, id string) (*model.Issue, error) {
	issue, err := s.repo.GetByID(ctx, id)
	if errors.Is(err, repository.ErrNotFound) {
		return nil, ErrNotFound
	}
	return issue, err
}

// UpdateStatus 状态流转唯一入口。v1.1（Q1 已决策）：
// 三态任意互转（含回退、跨态）与自转（幂等 no-op）一律放行；
// 409 仅表示并发冲突——条件更新失败后区分"记录不存在"（404）与"状态已被他人修改"（409）。
// 进入 resolved 写 resolvedAt，离开 resolved 清空。
func (s *IssueService) UpdateStatus(ctx context.Context, id string, to model.Status) (*model.Issue, error) {
	if !model.ValidStatus(to) {
		return nil, ErrValidation
	}
	cur, err := s.repo.GetByID(ctx, id)
	if errors.Is(err, repository.ErrNotFound) {
		return nil, ErrNotFound
	}
	if err != nil {
		return nil, err
	}

	now := s.now().UTC()
	var resolvedAt *int64
	if to == model.StatusResolved && cur.Status != model.StatusResolved {
		// 进入 resolved（含自转 resolved→resolved 不重置时间：仅状态变化时写入）。
		ms := now.UnixMilli()
		resolvedAt = &ms
	} else if to != model.StatusResolved && cur.Status == model.StatusResolved {
		// 离开 resolved：清空（传 nil）。
		resolvedAt = nil
	} else {
		// 其余情形保持原值：resolved→resolved 保留首次解决时间；open/in_progress 间互转保持空。
		if cur.ResolvedAt != nil {
			ms := cur.ResolvedAt.UnixMilli()
			resolvedAt = &ms
		}
	}
	updated, err := s.repo.UpdateStatusIfCurrent(ctx, id, cur.Status, to, now.UnixMilli(), resolvedAt)
	if err != nil {
		return nil, err
	}
	if !updated {
		// 读到校验之间状态被他人改掉：再查一次区分 404 / 409。
		latest, err := s.repo.GetByID(ctx, id)
		if errors.Is(err, repository.ErrNotFound) {
			return nil, ErrNotFound
		}
		if err != nil {
			return nil, err
		}
		return latest, ErrConflict
	}
	return s.repo.GetByID(ctx, id)
}

// UpdateIssueInput 字段编辑入参：仅覆盖可编辑字段（标题/描述/分类/优先级/上报人），
// 状态与 AR 定位字段（position/anchor/location）不在此接口范围内——状态走 UpdateStatus，
// 定位由 AR 上报时一次性写入。
type UpdateIssueInput struct {
	Title        string
	Description  string
	Category     model.Category
	Priority     model.Priority
	ReporterName string
}

// UpdateIssue 字段编辑唯一入口。校验规则与创建保持一致（title 必填、
// category 缺省 other、枚举非法 400）；记录不存在 404。
// 并发语义：字段编辑后写覆盖（last-write-wins），409 仍仅表示状态并发冲突。
func (s *IssueService) UpdateIssue(ctx context.Context, id string, in UpdateIssueInput) (*model.Issue, error) {
	in.Title = strings.TrimSpace(in.Title)
	if in.Title == "" {
		return nil, ErrValidation
	}
	if in.Category == "" {
		in.Category = model.CategoryOther
	}
	if !model.ValidCategory(in.Category) {
		return nil, ErrValidation
	}
	if !model.ValidPriority(in.Priority) {
		return nil, ErrValidation
	}

	updated, err := s.repo.UpdateIssueFields(ctx, id, in.Title, in.Description,
		in.Category, in.Priority, strings.TrimSpace(in.ReporterName), s.now().UTC().UnixMilli())
	if err != nil {
		return nil, err
	}
	if !updated {
		return nil, ErrNotFound
	}
	return s.repo.GetByID(ctx, id)
}

// newID 生成 UUID v4 字符串，ID 一律服务端生成。
func newID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	dst := make([]byte, 32)
	hex.Encode(dst, b[:])
	return string(dst[0:8]) + "-" + string(dst[8:12]) + "-" + string(dst[12:16]) + "-" + string(dst[16:20]) + "-" + string(dst[20:32])
}
