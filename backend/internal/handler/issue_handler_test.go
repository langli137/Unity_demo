package handler

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"park-inspect/internal/auth"
	"park-inspect/internal/repository/mem"
	"park-inspect/internal/service"
)

const testJWTSecret = "test-secret"

func newTestRouter() http.Handler {
	svc := service.NewIssueService(mem.New()).WithPhotos(mem.NewPhotoRepo())
	authSvc := service.NewAuthService(mem.NewUserRepo(), testJWTSecret)
	return NewRouter(NewIssueHandler(svc), NewAuthHandler(authSvc), testJWTSecret, "http://localhost:5173")
}

// testToken 签发测试用 admin 令牌（A1 后所有业务端点都要求登录）。
func testToken(t *testing.T) string {
	t.Helper()
	return auth.Sign(testJWTSecret, auth.Claims{
		Sub: "admin", Role: "admin", Exp: time.Now().Add(time.Hour).Unix(),
	})
}

func doJSON(t *testing.T, router http.Handler, method, path, body string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(method, path, bytes.NewBufferString(body))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+testToken(t))
	rec := httptest.NewRecorder()
	router.ServeHTTP(rec, req)
	return rec
}

func TestIssueAPILifecycle(t *testing.T) {
	router := newTestRouter()

	// 200：创建合法 issue。
	rec := doJSON(t, router, http.MethodPost, "/api/v1/issues", `{
		"title":"入口墙面破损","description":"左侧墙体存在裂缝",
		"category":"wall_damage","priority":"high",
		"position":{"x":0.42,"y":0.03,"z":1.26},
		"anchor":{"rotation":{"x":0,"y":0,"z":0},"planeId":"plane-1"},
		"reporterName":"张三"
	}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("create: status = %d, body = %s", rec.Code, rec.Body)
	}
	var created struct {
		Code int `json:"code"`
		Data struct {
			ID       string `json:"id"`
			Status   string `json:"status"`
			Priority string `json:"priority"`
			Position struct {
				X float64 `json:"x"`
			} `json:"position"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("decode create response: %v", err)
	}
	if created.Code != 0 || created.Data.ID == "" || created.Data.Status != "open" ||
		created.Data.Priority != "high" || created.Data.Position.X != 0.42 {
		t.Fatalf("unexpected create response: %s", rec.Body)
	}
	id := created.Data.ID

	// 400：非法 body。
	rec = doJSON(t, router, http.MethodPost, "/api/v1/issues", `{"title":""}`)
	if rec.Code != http.StatusBadRequest {
		t.Errorf("invalid create: status = %d, want 400", rec.Code)
	}

	// 200：列表（status=open 筛选）。
	rec = doJSON(t, router, http.MethodGet, "/api/v1/issues?status=open&page=1&pageSize=10", "")
	if rec.Code != http.StatusOK {
		t.Errorf("list: status = %d, want 200", rec.Code)
	}

	// 200：三态任意互转 open -> in_progress -> resolved -> open（含回退）。
	for _, to := range []string{"in_progress", "resolved", "open"} {
		rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id+"/status", `{"status":"`+to+`"}`)
		if rec.Code != http.StatusOK {
			t.Fatalf("transition to %s: status = %d, body = %s", to, rec.Code, rec.Body)
		}
	}

	// 200：自转是幂等 no-op（v1.1 Q1）。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id+"/status", `{"status":"open"}`)
	if rec.Code != http.StatusOK {
		t.Errorf("self transition: status = %d, want 200", rec.Code)
	}

	// 400：非法状态枚举（参数错误，非流转冲突）。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id+"/status", `{"status":"pending"}`)
	if rec.Code != http.StatusBadRequest {
		t.Errorf("unknown status: status = %d, want 400", rec.Code)
	}

	// 404：不存在的 id。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/no-such-id/status", `{"status":"resolved"}`)
	if rec.Code != http.StatusNotFound {
		t.Errorf("missing issue: status = %d, want 404", rec.Code)
	}
}

// TestUpdateIssueFieldsAPI 字段编辑接口（v1.2）：200 编辑成功 / 400 校验失败 / 404 不存在。
func TestUpdateIssueFieldsAPI(t *testing.T) {
	router := newTestRouter()

	// 先创建一条。
	rec := doJSON(t, router, http.MethodPost, "/api/v1/issues", `{
		"title":"设备间异响","category":"equipment_fault","priority":"medium",
		"position":{"x":0.1,"y":0,"z":1.5},"anchor":{"planeId":"plane-2"},
		"reporterName":"王五"
	}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("create: status = %d, body = %s", rec.Code, rec.Body)
	}
	var created struct {
		Data struct {
			ID     string `json:"id"`
			Status string `json:"status"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("decode: %v", err)
	}
	id := created.Data.ID

	// 200：编辑可编辑字段；响应返回最新值，状态不变。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id, `{
		"title":"设备间异响（已复核）","description":"风机轴承异响",
		"category":"safety_hazard","priority":"high","reporterName":"赵六"
	}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("edit: status = %d, body = %s", rec.Code, rec.Body)
	}
	var edited struct {
		Data struct {
			Title        string `json:"title"`
			Category     string `json:"category"`
			Priority     string `json:"priority"`
			ReporterName string `json:"reporterName"`
			Status       string `json:"status"`
		} `json:"data"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &edited); err != nil {
		t.Fatalf("decode edit: %v", err)
	}
	if edited.Data.Title != "设备间异响（已复核）" || edited.Data.Category != "safety_hazard" ||
		edited.Data.Priority != "high" || edited.Data.ReporterName != "赵六" {
		t.Errorf("edited fields mismatch: %s", rec.Body)
	}
	if edited.Data.Status != created.Data.Status {
		t.Errorf("field edit must not touch status: %s -> %s", created.Data.Status, edited.Data.Status)
	}

	// 400：空标题。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id, `{"title":"  "}`)
	if rec.Code != http.StatusBadRequest {
		t.Errorf("empty title: status = %d, want 400", rec.Code)
	}

	// 400：非法枚举。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/"+id, `{"title":"x","priority":"urgent"}`)
	if rec.Code != http.StatusBadRequest {
		t.Errorf("invalid priority: status = %d, want 400", rec.Code)
	}

	// 404：不存在的 id。
	rec = doJSON(t, router, http.MethodPatch, "/api/v1/issues/no-such-id", `{"title":"x","priority":"low"}`)
	if rec.Code != http.StatusNotFound {
		t.Errorf("missing issue: status = %d, want 404", rec.Code)
	}
}
