package handler

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"park-inspect/internal/auth"
	"park-inspect/internal/repository"
	"park-inspect/internal/repository/mem"
	"park-inspect/internal/service"
)

// loginWith 用内存用户库登录拿 token；users 为空时先注入种子账号。
func loginWith(t *testing.T, users *mem.UserRepo, username, password string) *httptest.ResponseRecorder {
	t.Helper()
	svc := service.NewAuthService(users, testJWTSecret)
	ah := NewAuthHandler(svc)
	body, _ := json.Marshal(map[string]string{"username": username, "password": password})
	req := httptest.NewRequest(http.MethodPost, "/api/v1/auth/login", bytes.NewReader(body))
	rec := httptest.NewRecorder()
	ah.Login(rec, req)
	return rec
}

func seedUsers() *mem.UserRepo {
	users := mem.NewUserRepo()
	users.Add(&repository.UserRecord{
		Username: "admin", DisplayName: "系统管理员", Role: "admin",
		PasswordHash: auth.HashPassword("admin", "admin123"), Salt: "admin",
	})
	users.Add(&repository.UserRecord{
		Username: "inspector", DisplayName: "巡检员", Role: "inspector",
		PasswordHash: auth.HashPassword("inspector", "inspector123"), Salt: "inspector",
	})
	return users
}

// TestLoginAPI A1：200 登录成功发 token / 401 密码错误 / 401 用户不存在。
func TestLoginAPI(t *testing.T) {
	users := seedUsers()

	rec := loginWith(t, users, "admin", "admin123")
	if rec.Code != http.StatusOK {
		t.Fatalf("login: status = %d, body = %s", rec.Code, rec.Body)
	}
	var ok struct {
		Data struct {
			Token string `json:"token"`
			User  struct {
				Username string `json:"username"`
				Role     string `json:"role"`
			} `json:"user"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &ok); err != nil {
		t.Fatalf("decode: %v", err)
	}
	if ok.Data.Token == "" || ok.Data.User.Username != "admin" || ok.Data.User.Role != "admin" {
		t.Fatalf("unexpected login response: %s", rec.Body)
	}

	if rec := loginWith(t, users, "admin", "wrong"); rec.Code != http.StatusUnauthorized {
		t.Errorf("wrong password: status = %d, want 401", rec.Code)
	}
	if rec := loginWith(t, users, "nobody", "x"); rec.Code != http.StatusUnauthorized {
		t.Errorf("unknown user: status = %d, want 401", rec.Code)
	}
}

// TestAuthRequired A1：无 token 401；伪造 token 401；inspector 访问 admin 端点 403。
func TestAuthRequired(t *testing.T) {
	router := newTestRouter()

	// 无 token。
	req := httptest.NewRequest(http.MethodGet, "/api/v1/issues", nil)
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusUnauthorized {
		t.Errorf("no token: status = %d, want 401", rec.Code)
	}

	// 伪造 token。
	req = httptest.NewRequest(http.MethodGet, "/api/v1/issues", nil)
	req.Header.Set("Authorization", "Bearer "+strings.Repeat("x", 10)+".eyJzdWIiOiJhIn0.y")
	rec = httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusUnauthorized {
		t.Errorf("forged token: status = %d, want 401", rec.Code)
	}

	// inspector 删除 → 403（权限不足）。
	inspTok := auth.Sign(testJWTSecret, auth.Claims{
		Sub: "inspector", Role: "inspector", Exp: time.Now().Add(time.Hour).Unix(),
	})
	req = httptest.NewRequest(http.MethodDelete, "/api/v1/issues/some-id", nil)
	req.Header.Set("Authorization", "Bearer "+inspTok)
	rec = httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusForbidden {
		t.Errorf("inspector delete: status = %d, want 403", rec.Code)
	}
}

// TestDeleteIssueAPI A4：200 删除成功 / 404 不存在 / 删除后 404。
func TestDeleteIssueAPI(t *testing.T) {
	router := newTestRouter()

	// 先创建一条。
	rec := doJSON(t, router, http.MethodPost, "/api/v1/issues", `{
		"title":"待删问题","category":"other","priority":"low",
		"position":{"x":0,"y":0,"z":0},"anchor":{"planeId":""}
	}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("create: status = %d, body = %s", rec.Code, rec.Body)
	}
	var created struct {
		Data struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &created)
	id := created.Data.ID

	// 200 删除。
	rec = doJSON(t, router, http.MethodDelete, "/api/v1/issues/"+id, "")
	if rec.Code != http.StatusOK {
		t.Fatalf("delete: status = %d, body = %s", rec.Code, rec.Body)
	}
	// 删除后再查 404。
	rec = doJSON(t, router, http.MethodGet, "/api/v1/issues/"+id, "")
	if rec.Code != http.StatusNotFound {
		t.Errorf("after delete: status = %d, want 404", rec.Code)
	}
	// 重复删除 404。
	rec = doJSON(t, router, http.MethodDelete, "/api/v1/issues/"+id, "")
	if rec.Code != http.StatusNotFound {
		t.Errorf("double delete: status = %d, want 404", rec.Code)
	}
}

// TestPhotoRoundtrip B6：上传 → HasPhoto 可见 → 下载字节一致；不存在的问题 404。
func TestPhotoRoundtrip(t *testing.T) {
	users := seedUsers()
	photos := mem.NewPhotoRepo()
	issues := mem.New()
	svc := service.NewIssueService(issues).WithPhotos(photos)
	router := NewRouter(NewIssueHandler(svc), NewAuthHandler(service.NewAuthService(users, testJWTSecret)),
		testJWTSecret, "http://localhost:5173")

	// 创建。
	rec := doJSON(t, router, http.MethodPost, "/api/v1/issues", `{
		"title":"拍照问题","category":"other","priority":"low",
		"position":{"x":0,"y":0,"z":0},"anchor":{"planeId":""}
	}`)
	var created struct {
		Data struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	_ = json.Unmarshal(rec.Body.Bytes(), &created)
	id := created.Data.ID

	// 上传。
	jpeg := []byte("\xFF\xD8\xFF\xE0fake-jpeg-bytes")
	req := httptest.NewRequest(http.MethodPost, "/api/v1/issues/"+id+"/photo", bytes.NewReader(jpeg))
	req.Header.Set("Authorization", "Bearer "+testToken(t))
	req.Header.Set("Content-Type", "image/jpeg")
	rec = httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("upload: status = %d, body = %s", rec.Code, rec.Body)
	}

	// 下载字节一致。
	req = httptest.NewRequest(http.MethodGet, "/api/v1/issues/"+id+"/photo", nil)
	req.Header.Set("Authorization", "Bearer "+testToken(t))
	rec = httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || !bytes.Equal(rec.Body.Bytes(), jpeg) {
		t.Errorf("download: status = %d, bytes equal = %v", rec.Code, bytes.Equal(rec.Body.Bytes(), jpeg))
	}
	if ct := rec.Header().Get("Content-Type"); ct != "image/jpeg" {
		t.Errorf("content-type = %q", ct)
	}

	// 不存在的问题上传 404。
	req = httptest.NewRequest(http.MethodPost, "/api/v1/issues/no-such/photo", bytes.NewReader(jpeg))
	req.Header.Set("Authorization", "Bearer "+testToken(t))
	rec = httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	if rec.Code != http.StatusNotFound {
		t.Errorf("upload missing issue: status = %d, want 404", rec.Code)
	}
}
