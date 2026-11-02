// Package mem 是 repository.IssueRepository 的内存实现，仅用于单测（service 层测试不得依赖真实数据库）。
package mem

import (
	"context"
	"sync"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

type IssueRepo struct {
	mu     sync.Mutex
	issues map[string]*model.Issue
}

func New() *IssueRepo {
	return &IssueRepo{issues: map[string]*model.Issue{}}
}

func (r *IssueRepo) Create(_ context.Context, issue *model.Issue) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	cp := *issue
	r.issues[issue.ID] = &cp
	return nil
}

func (r *IssueRepo) List(_ context.Context, f repository.ListFilter) ([]*model.Issue, int, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	var items []*model.Issue
	for _, issue := range r.issues {
		if f.Status != "" && issue.Status != f.Status {
			continue
		}
		if f.Category != "" && issue.Category != f.Category {
			continue
		}
		cp := *issue
		items = append(items, &cp)
	}
	total := len(items)
	// 与 SQLite 实现保持一致：按 created_at 倒序分页。
	for i := 0; i < len(items); i++ {
		for j := i + 1; j < len(items); j++ {
			if items[j].CreatedAt.After(items[i].CreatedAt) {
				items[i], items[j] = items[j], items[i]
			}
		}
	}
	start := (f.Page - 1) * f.PageSize
	if start > total {
		start = total
	}
	end := start + f.PageSize
	if end > total {
		end = total
	}
	return items[start:end], total, nil
}

func (r *IssueRepo) GetByID(_ context.Context, id string) (*model.Issue, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	issue, ok := r.issues[id]
	if !ok {
		return nil, repository.ErrNotFound
	}
	cp := *issue
	return &cp, nil
}

func (r *IssueRepo) UpdateStatusIfCurrent(_ context.Context, id string, from, to model.Status, updatedAt int64, resolvedAt *int64) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	issue, ok := r.issues[id]
	if !ok {
		return false, nil
	}
	if issue.Status != from {
		return false, nil
	}
	issue.Status = to
	issue.UpdatedAt = millisToTime(updatedAt)
	// 与 SQLite 实现一致：resolvedAt 非 nil 时写入（进入 resolved），nil 时清空（离开 resolved）。
	if resolvedAt != nil {
		t := millisToTime(*resolvedAt)
		issue.ResolvedAt = &t
	} else {
		issue.ResolvedAt = nil
	}
	return true, nil
}

func (r *IssueRepo) UpdateIssueFields(_ context.Context, id, title, description string, category model.Category, priority model.Priority, reporterName string, updatedAt int64) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	issue, ok := r.issues[id]
	if !ok {
		return false, nil
	}
	issue.Title = title
	issue.Description = description
	issue.Category = category
	issue.Priority = priority
	issue.ReporterName = reporterName
	issue.UpdatedAt = millisToTime(updatedAt)
	return true, nil
}

// Delete 硬删除（A4），照片由 mem.PhotoRepo 独立管理（单测各自覆盖）。
func (r *IssueRepo) Delete(_ context.Context, id string) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.issues[id]; !ok {
		return false, nil
	}
	delete(r.issues, id)
	return true, nil
}
