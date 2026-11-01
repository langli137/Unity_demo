// Package repository 定义持久化接口，只做数据读写，不含业务校验。
package repository

import (
	"context"
	"errors"

	"park-inspect/internal/model"
)

// ErrNotFound 由各实现返回，service 层负责映射为领域错误。
var ErrNotFound = errors.New("issue not found")

type ListFilter struct {
	Status   model.Status
	Category model.Category
	Page     int
	PageSize int
}

type IssueRepository interface {
	Create(ctx context.Context, issue *model.Issue) error
	List(ctx context.Context, f ListFilter) (items []*model.Issue, total int, err error)
	GetByID(ctx context.Context, id string) (*model.Issue, error)
	// UpdateStatusIfCurrent 条件更新：仅当当前状态 == from 时才更新为 to（并发防护）。
	// resolvedAt 为 nil 表示清空（离开 resolved），非 nil 表示写入/保留解决时间。
	// 返回 updated=false 表示记录不存在或状态已被他人修改，由 service 区分处理。
	UpdateStatusIfCurrent(ctx context.Context, id string, from, to model.Status, updatedAt int64, resolvedAt *int64) (bool, error)
	// UpdateIssueFields 字段编辑（标题/描述/分类/优先级/上报人），不触碰 status/position 等
	// AR 与状态机字段。返回 updated=false 表示记录不存在。字段编辑无乐观锁（后写覆盖），
	// 409 语义保持"仅状态并发冲突"不扩大。
	UpdateIssueFields(ctx context.Context, id string, title, description string, category model.Category, priority model.Priority, reporterName string, updatedAt int64) (bool, error)
}
