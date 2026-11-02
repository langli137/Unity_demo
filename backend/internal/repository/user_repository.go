// Package repository 定义持久化接口：service 只依赖接口，sqlite/mem 双实现。
package repository

import (
	"context"
	"errors"
)

var ErrUserNotFound = errors.New("user not found")

// UserRecord 用户持久化记录（含散列，仅 repository→service 单向传递，不进 JSON）。
type UserRecord struct {
	Username     string
	DisplayName  string
	Role         string
	PasswordHash string
	Salt         string
}

// UserRepository 用户查询（A1）：登录只需按用户名取一条，不做用户管理 CRUD。
type UserRepository interface {
	GetByUsername(ctx context.Context, username string) (*UserRecord, error)
}
