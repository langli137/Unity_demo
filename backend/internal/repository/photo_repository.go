package repository

import "context"

// PhotoRepository 问题照片留档（B6）：一问题一张照片（后拍覆盖前拍）。
type PhotoRepository interface {
	SavePhoto(ctx context.Context, issueID string, data []byte) error
	GetPhoto(ctx context.Context, issueID string) ([]byte, error)
	HasPhoto(ctx context.Context, issueID string) (bool, error)
}
