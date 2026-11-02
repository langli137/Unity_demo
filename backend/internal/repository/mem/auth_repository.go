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

// PhotoRepo 照片内存实现（B6 单测）。
type PhotoRepo struct {
	mu     sync.Mutex
	photos map[string][]byte
}

func NewPhotoRepo() *PhotoRepo { return &PhotoRepo{photos: map[string][]byte{}} }

func (r *PhotoRepo) SavePhoto(_ context.Context, issueID string, data []byte) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	cp := make([]byte, len(data))
	copy(cp, data)
	r.photos[issueID] = cp
	return nil
}

func (r *PhotoRepo) GetPhoto(_ context.Context, issueID string) ([]byte, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	data, ok := r.photos[issueID]
	if !ok {
		return nil, repository.ErrNotFound
	}
	cp := make([]byte, len(data))
	copy(cp, data)
	return cp, nil
}

func (r *PhotoRepo) HasPhoto(_ context.Context, issueID string) (bool, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	_, ok := r.photos[issueID]
	return ok, nil
}
