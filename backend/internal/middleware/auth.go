package middleware

import (
	"context"
	"encoding/json"
	"net/http"
	"strings"

	"park-inspect/internal/auth"
	"park-inspect/internal/model"
)

type ctxKey int

const userClaimsKey ctxKey = iota

// RequireAuth 校验 Authorization: Bearer <jwt>，通过后把 Claims 放进 context。
// 失败统一 401 响应壳（与 handler 的 {code,message} 契约一致）。
func RequireAuth(secret string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		header := r.Header.Get("Authorization")
		if !strings.HasPrefix(header, "Bearer ") {
			writeAuthError(w, http.StatusUnauthorized, "未登录或令牌缺失")
			return
		}
		claims, err := auth.Parse(secret, strings.TrimPrefix(header, "Bearer "))
		if err != nil {
			writeAuthError(w, http.StatusUnauthorized, "登录已过期，请重新登录")
			return
		}
		ctx := context.WithValue(r.Context(), userClaimsKey, claims)
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}

// RequireRole 要求至少具备指定角色（admin > inspector）。
// admin 拥有 inspector 的全部权限；调用方须保证外层已包 RequireAuth。
func RequireRole(min model.Role, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		claims, ok := ClaimsFrom(r.Context())
		if !ok {
			writeAuthError(w, http.StatusUnauthorized, "未登录或令牌缺失")
			return
		}
		if !hasRole(model.Role(claims.Role), min) {
			writeAuthError(w, http.StatusForbidden, "权限不足：该操作需要管理员权限")
			return
		}
		next.ServeHTTP(w, r)
	})
}

// hasRole：admin 覆盖 inspector；同级放行。
func hasRole(actual, min model.Role) bool {
	if min == model.RoleInspector {
		return actual == model.RoleAdmin || actual == model.RoleInspector
	}
	return actual == min
}

// ClaimsFrom 取当前请求的登录态。
func ClaimsFrom(ctx context.Context) (*auth.Claims, bool) {
	claims, ok := ctx.Value(userClaimsKey).(*auth.Claims)
	return claims, ok
}

func writeAuthError(w http.ResponseWriter, status int, msg string) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{"code": status, "message": msg})
}
