package service

import (
	"context"
)

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
