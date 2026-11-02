// Package service 承载业务规则。AuthService：登录校验与令牌签发（A1）。
package service

import (
	"context"
	"errors"
	"strings"
	"time"

	"park-inspect/internal/auth"
	"park-inspect/internal/model"
	"park-inspect/internal/repository"
)

// ErrInvalidCredentials 统一提示：不区分"用户不存在"与"密码错误"（防用户名枚举）。
var ErrInvalidCredentials = errors.New("invalid username or password")

type AuthService struct {
	users  repository.UserRepository
	secret string
	now    func() time.Time
}

func NewAuthService(users repository.UserRepository, jwtSecret string) *AuthService {
	return &AuthService{users: users, secret: jwtSecret, now: time.Now}
}

// LoginResult 登录成功产物：token 供后续请求携带，user 供前端展示角色。
type LoginResult struct {
	Token string     `json:"token"`
	User  model.User `json:"user"`
}

// Login 校验口令并签发 JWT（7 天有效）。
func (s *AuthService) Login(ctx context.Context, username, password string) (*LoginResult, error) {
	username = strings.TrimSpace(username)
	if username == "" || password == "" {
		return nil, ErrInvalidCredentials
	}
	rec, err := s.users.GetByUsername(ctx, username)
	if err != nil {
		if errors.Is(err, repository.ErrUserNotFound) {
			return nil, ErrInvalidCredentials
		}
		return nil, err
	}
	if auth.HashPassword(rec.Salt, password) != rec.PasswordHash {
		return nil, ErrInvalidCredentials
	}
	claims := auth.Claims{
		Sub:  rec.Username,
		Role: rec.Role,
		Exp:  s.now().Add(auth.TokenTTL).Unix(),
	}
	return &LoginResult{
		Token: auth.Sign(s.secret, claims),
		User: model.User{
			Username:    rec.Username,
			DisplayName: rec.DisplayName,
			Role:        model.Role(rec.Role),
		},
	}, nil
}
