// Package sqlite 是 IssueRepository 的默认实现（modernc.org/sqlite，免 CGO）。
package sqlite

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	_ "modernc.org/sqlite"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

type IssueRepo struct {
	db *sql.DB
}

// Open 打开 SQLite 数据库并执行迁移，建表语句单一来源在 migrations 包。
func Open(dbPath string, migrationSQL string) (*IssueRepo, error) {
	if dir := filepath.Dir(dbPath); dir != "." && dir != "" {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return nil, fmt.Errorf("create db dir: %w", err)
		}
	}
	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		return nil, fmt.Errorf("open sqlite: %w", err)
	}
	// WAL 提升并发读写；单文件场景限制连接数避免锁竞争。
	if _, err := db.Exec(`PRAGMA journal_mode=WAL`); err != nil {
		db.Close()
		return nil, fmt.Errorf("enable wal: %w", err)
	}
	db.SetMaxOpenConns(1)
	if _, err := db.Exec(migrationSQL); err != nil {
		db.Close()
		return nil, fmt.Errorf("migrate: %w", err)
	}
	return &IssueRepo{db: db}, nil
}

func (r *IssueRepo) Close() error { return r.db.Close() }

func (r *IssueRepo) Create(ctx context.Context, issue *model.Issue) error {
	anchor, err := json.Marshal(issue.Anchor)
	if err != nil {
		return fmt.Errorf("marshal anchor: %w", err)
	}
	_, err = r.db.ExecContext(ctx, `
		INSERT INTO issues (
			id, title, description, category, priority, status,
			position_x, position_y, position_z, anchor_json,
			latitude, longitude, reporter_name, created_at, updated_at, resolved_at
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		issue.ID, issue.Title, issue.Description, string(issue.Category),
		string(issue.Priority), string(issue.Status),
		issue.Position.X, issue.Position.Y, issue.Position.Z, string(anchor),
		issue.Location.Latitude, issue.Location.Longitude, nullable(issue.ReporterName),
		issue.CreatedAt.UnixMilli(), issue.UpdatedAt.UnixMilli(), nullableTime(issue.ResolvedAt),
	)
	return err
}

func (r *IssueRepo) List(ctx context.Context, f repository.ListFilter) ([]*model.Issue, int, error) {
	where := `WHERE 1=1`
	args := []any{}
	if f.Status != "" {
		where += ` AND status = ?`
		args = append(args, string(f.Status))
	}
	if f.Category != "" {
		where += ` AND category = ?`
		args = append(args, string(f.Category))
	}

	var total int
	if err := r.db.QueryRowContext(ctx, `SELECT COUNT(*) FROM issues `+where, args...).Scan(&total); err != nil {
		return nil, 0, err
	}

	offset := (f.Page - 1) * f.PageSize
	rows, err := r.db.QueryContext(ctx, `
		SELECT id, title, description, category, priority, status,
			position_x, position_y, position_z, anchor_json,
			latitude, longitude, reporter_name, created_at, updated_at, resolved_at
		FROM issues `+where+` ORDER BY created_at DESC LIMIT ? OFFSET ?`,
		append(args, f.PageSize, offset)...)
	if err != nil {
		return nil, 0, err
	}
	defer rows.Close()

	var items []*model.Issue
	for rows.Next() {
		issue, err := scanIssue(rows)
		if err != nil {
			return nil, 0, err
		}
		items = append(items, issue)
	}
	return items, total, rows.Err()
}

func (r *IssueRepo) GetByID(ctx context.Context, id string) (*model.Issue, error) {
	row := r.db.QueryRowContext(ctx, `
		SELECT id, title, description, category, priority, status,
			position_x, position_y, position_z, anchor_json,
			latitude, longitude, reporter_name, created_at, updated_at, resolved_at
		FROM issues WHERE id = ?`, id)
	issue, err := scanIssue(row)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, repository.ErrNotFound
	}
	return issue, err
}

func (r *IssueRepo) UpdateStatusIfCurrent(ctx context.Context, id string, from, to model.Status, updatedAt int64, resolvedAt *int64) (bool, error) {
	res, err := r.db.ExecContext(ctx, `
		UPDATE issues SET status = ?, updated_at = ?, resolved_at = ?
		WHERE id = ? AND status = ?`,
		string(to), updatedAt, nullableMillis(resolvedAt), id, string(from))
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return false, err
	}
	return n > 0, nil
}

func (r *IssueRepo) UpdateIssueFields(ctx context.Context, id, title, description string, category model.Category, priority model.Priority, reporterName string, updatedAt int64) (bool, error) {
	res, err := r.db.ExecContext(ctx, `
		UPDATE issues SET title = ?, description = ?, category = ?, priority = ?, reporter_name = ?, updated_at = ?
		WHERE id = ?`,
		title, description, string(category), string(priority), nullable(reporterName), updatedAt, id)
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return false, err
	}
	return n > 0, nil
}

// Delete 硬删除（A4）。
func (r *IssueRepo) Delete(ctx context.Context, id string) (bool, error) {
	res, err := r.db.ExecContext(ctx, `DELETE FROM issues WHERE id = ?`, id)
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return false, err
	}
	return n > 0, nil
}

type scanner interface {
	Scan(dest ...any) error
}

func scanIssue(s scanner) (*model.Issue, error) {
	var (
		issue      model.Issue
		category   string
		priority   string
		status     string
		anchorJSON string
		latitude   sql.NullFloat64
		longitude  sql.NullFloat64
		reporter   sql.NullString
		createdAt  int64
		updatedAt  int64
		resolvedAt sql.NullInt64
	)
	if err := s.Scan(&issue.ID, &issue.Title, &issue.Description, &category, &priority,
		&status, &issue.Position.X, &issue.Position.Y, &issue.Position.Z, &anchorJSON,
		&latitude, &longitude, &reporter,
		&createdAt, &updatedAt, &resolvedAt); err != nil {
		return nil, err
	}
	if err := json.Unmarshal([]byte(anchorJSON), &issue.Anchor); err != nil {
		return nil, fmt.Errorf("unmarshal anchor: %w", err)
	}
	issue.Category = model.Category(category)
	issue.Priority = model.Priority(priority)
	issue.Status = model.Status(status)
	if latitude.Valid {
		issue.Location.Latitude = &latitude.Float64
	}
	if longitude.Valid {
		issue.Location.Longitude = &longitude.Float64
	}
	issue.ReporterName = reporter.String
	issue.CreatedAt = millisToTime(createdAt)
	issue.UpdatedAt = millisToTime(updatedAt)
	if resolvedAt.Valid {
		t := millisToTime(resolvedAt.Int64)
		issue.ResolvedAt = &t
	}
	return &issue, nil
}
