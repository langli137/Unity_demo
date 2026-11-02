package mem

import (
	"context"
	"sync"

	"park-inspect/internal/repository"
)

// UserRepo 用户查询内存实现（A1 单测）。
type UserRepo struct {
	mu    sync.Mutex
	users map[string]*repository.UserRecord
}

func NewUserRepo() *UserRepo { return &UserRepo{users: map[string]*repository.UserRecord{}} }

func (r *UserRepo) Add(rec *repository.UserRecord) {
	r.mu.Lock()
	defer r.mu.Unlock()
	cp := *rec
	r.users[rec.Username] = &cp
}

func (r *UserRepo) GetByUsername(_ context.Context, username string) (*repository.UserRecord, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	rec, ok := r.users[username]
	if !ok {
		return nil, repository.ErrUserNotFound
	}
	cp := *rec
	return &cp, nil
}
