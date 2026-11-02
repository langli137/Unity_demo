package service

import (
	"context"
	"errors"

	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

// ErrPhotoTooLarge 照片上限 8MB（手机截图 JPEG 通常 200KB~2MB）。
var ErrPhotoTooLarge = errors.New("photo too large (max 8MB)")

const maxPhotoBytes = 8 << 20

// DeleteIssue 删除问题（A4，仅 admin——角色校验在 middleware/handler 层）。
// 级联删照片在 sqlite 实现内完成；mem 实现照片独立（单测各自覆盖）。
func (s *IssueService) DeleteIssue(ctx context.Context, id string) error {
	deleted, err := s.repo.Delete(ctx, id)
	if err != nil {
		return err
	}
	if !deleted {
		return ErrNotFound
	}
	return nil
}

// SaveIssuePhoto 上传/覆盖问题照片（B6）。问题必须已存在（404 兜底）。
func (s *IssueService) SaveIssuePhoto(ctx context.Context, id string, data []byte) error {
	if s.photos == nil {
		return ErrValidation
	}
	if len(data) == 0 {
		return ErrValidation
	}
	if len(data) > maxPhotoBytes {
		return ErrPhotoTooLarge
	}
	if _, err := s.repo.GetByID(ctx, id); err != nil {
		if errors.Is(err, repository.ErrNotFound) {
			return ErrNotFound
		}
		return err
	}
	return s.photos.SavePhoto(ctx, id, data)
}

// GetIssuePhoto 读取照片字节；无照片 → ErrNotFound（handler 映射 404）。
func (s *IssueService) GetIssuePhoto(ctx context.Context, id string) ([]byte, error) {
	if s.photos == nil {
		return nil, ErrNotFound
	}
	data, err := s.photos.GetPhoto(ctx, id)
	if errors.Is(err, repository.ErrNotFound) {
		return nil, ErrNotFound
	}
	return data, err
}

// HasIssuePhoto 供列表/详情标记 photoUrl 是否存在（避免前端盲发图片请求）。
func (s *IssueService) HasIssuePhoto(ctx context.Context, id string) (bool, error) {
	if s.photos == nil {
		return false, nil
	}
	return s.photos.HasPhoto(ctx, id)
}
