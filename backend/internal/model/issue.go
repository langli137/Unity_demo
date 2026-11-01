package model

import "time"

type Category string

const (
	CategoryWallDamage     Category = "wall_damage"
	CategoryEquipmentFault Category = "equipment_fault"
	CategorySafetyHazard   Category = "safety_hazard"
	CategoryOther          Category = "other"
)

func ValidCategory(c Category) bool {
	switch c {
	case CategoryWallDamage, CategoryEquipmentFault, CategorySafetyHazard, CategoryOther:
		return true
	}
	return false
}

type Priority string

const (
	PriorityLow    Priority = "low"
	PriorityMedium Priority = "medium"
	PriorityHigh   Priority = "high"
)

func ValidPriority(p Priority) bool {
	switch p {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return true
	}
	return false
}

// Status 三态（需求文档 v1.1 3.2，Q1 已决策）。
type Status string

const (
	StatusOpen       Status = "open"
	StatusInProgress Status = "in_progress"
	StatusResolved   Status = "resolved"
)

func ValidStatus(s Status) bool {
	switch s {
	case StatusOpen, StatusInProgress, StatusResolved:
		return true
	}
	return false
}

// 流转规则（v1.1 Q1 联网调研拍板）：三态任意互转（含回退、跨态）合法；
// 自转为幂等 no-op，同样返回成功；服务端只校验枚举合法性与并发冲突，
// 不再持有"非法流转"概念，409 收窄为仅并发冲突。

type Vector3 struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Anchor AR 锚点扩展信息；位置已提升为 Issue 顶层 position 字段。
type Anchor struct {
	Rotation Vector3 `json:"rotation"`
	PlaneID  string  `json:"planeId"`
}

type Location struct {
	Latitude  *float64 `json:"latitude,omitempty"`
	Longitude *float64 `json:"longitude,omitempty"`
}

// Issue 三端共用领域模型，json tag 即全局契约，禁止各端另造命名。
type Issue struct {
	ID           string     `json:"id"`
	Title        string     `json:"title"`
	Description  string     `json:"description"`
	Category     Category   `json:"category"`
	Priority     Priority   `json:"priority"`
	Status       Status     `json:"status"`
	Position     Vector3    `json:"position"`
	Anchor       Anchor     `json:"anchor"`
	Location     Location   `json:"location"`
	ReporterName string     `json:"reporterName,omitempty"`
	CreatedAt    time.Time  `json:"createdAt"`
	UpdatedAt    time.Time  `json:"updatedAt"`
	ResolvedAt   *time.Time `json:"resolvedAt,omitempty"`
}
