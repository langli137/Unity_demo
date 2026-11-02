package sqlite

import (
	"context"
	"database/sql"
	"errors"

	"park-inspect/internal/repository"
)

// PhotoRepo 问题照片留档（B6）：一问题一照片（后拍覆盖前拍，主键 issue_id）。
type PhotoRepo struct {
	db *sql.DB
}

func NewPhotoRepo(db *sql.DB) *PhotoRepo { return &PhotoRepo{db: db} }

func (r *PhotoRepo) SavePhoto(ctx context.Context, issueID string, data []byte) error {
	_, err := r.db.ExecContext(ctx, `
		INSERT INTO issue_photos (issue_id, data, created_at)
		VALUES (?, ?, strftime('%s','now') * 1000)
		ON CONFLICT(issue_id) DO UPDATE SET data = excluded.data, created_at = excluded.created_at`,
		issueID, data)
	return err
}

func (r *PhotoRepo) GetPhoto(ctx context.Context, issueID string) ([]byte, error) {
	row := r.db.QueryRowContext(ctx, `SELECT data FROM issue_photos WHERE issue_id = ?`, issueID)
	var data []byte
	if err := row.Scan(&data); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return nil, repository.ErrNotFound
		}
		return nil, err
	}
	return data, nil
}

func (r *PhotoRepo) HasPhoto(ctx context.Context, issueID string) (bool, error) {
	var n int
	if err := r.db.QueryRowContext(ctx, `SELECT COUNT(*) FROM issue_photos WHERE issue_id = ?`, issueID).Scan(&n); err != nil {
		return false, err
	}
	return n > 0, nil
}
