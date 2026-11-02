package handler

import (
	"encoding/json"
	"net/http"

	"park-inspect/internal/service"
)

// AuthHandler 登录（A1）：POST /api/v1/auth/login。
type AuthHandler struct {
	svc *service.AuthService
}

func NewAuthHandler(svc *service.AuthService) *AuthHandler {
	return &AuthHandler{svc: svc}
}

type loginRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

// Login 登录成功返回 {token, user}；失败统一 401 "用户名或密码错误"。
func (h *AuthHandler) Login(w http.ResponseWriter, r *http.Request) {
	var req loginRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	result, err := h.svc.Login(r.Context(), req.Username, req.Password)
	if err != nil {
		if err == service.ErrInvalidCredentials {
			writeError(w, http.StatusUnauthorized, "用户名或密码错误")
			return
		}
		writeServiceError(w, err)
		return
	}
	writeOK(w, result)
}
