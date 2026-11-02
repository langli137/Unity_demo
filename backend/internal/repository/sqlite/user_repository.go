package sqlite

import (
	"context"
	"database/sql"
	"errors"

	"park-inspect/internal/repository"
)

// UserRepo 用户查询（A1）：与 IssueRepo 共用同一个 *sql.DB。
type UserRepo struct {
	db *sql.DB
}

func NewUserRepo(db *sql.DB) *UserRepo { return &UserRepo{db: db} }

// ExposeDB 供装配处把 IssueRepo 的 db 交出来（避免 Open 拆成两个连接）。
func (r *IssueRepo) ExposeDB() *sql.DB { return r.db }

func (r *UserRepo) GetByUsername(ctx context.Context, username string) (*repository.UserRecord, error) {
	row := r.db.QueryRowContext(ctx, `
		SELECT username, display_name, role, password_hash, salt
		FROM users WHERE username = ?`, username)
	var rec repository.UserRecord
	if err := row.Scan(&rec.Username, &rec.DisplayName, &rec.Role, &rec.PasswordHash, &rec.Salt); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return nil, repository.ErrUserNotFound
		}
		return nil, err
	}
	return &rec, nil
}
