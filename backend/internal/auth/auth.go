// Package auth 承载口令散列与 JWT 签发/校验（无第三方依赖：HS256 手写实现）。
package auth

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"strings"
	"time"
)

// ---------- 口令散列 ----------

// HashPassword 口令散列：hex(sha256(salt || ":" || password))。
// 演示系统够用；生产应换 bcrypt/argon2（已在 README 已知问题注明）。
func HashPassword(salt, password string) string {
	sum := sha256.Sum256([]byte(salt + ":" + password))
	return hex.EncodeToString(sum[:])
}

// NewSalt 每用户随机盐（hex，16 字节）。
func NewSalt() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	return hex.EncodeToString(b[:])
}

// ---------- JWT（HS256） ----------

var (
	ErrTokenInvalid = errors.New("token invalid")
	ErrTokenExpired = errors.New("token expired")
)

// Claims JWT 载荷：sub=用户名，role=admin|inspector，exp=过期（Unix 秒）。
type Claims struct {
	Sub  string `json:"sub"`
	Role string `json:"role"`
	Exp  int64  `json:"exp"`
}

type header struct {
	Alg string `json:"alg"`
	Typ string `json:"Typ"`
}

var b64 = base64.RawURLEncoding

// Sign 签发 HS256 JWT。
func Sign(secret string, c Claims) string {
	hj, _ := json.Marshal(header{Alg: "HS256", Typ: "JWT"})
	pj, _ := json.Marshal(c)
	unsigned := b64.EncodeToString(hj) + "." + b64.EncodeToString(pj)
	mac := hmac.New(sha256.New, []byte(secret))
	mac.Write([]byte(unsigned))
	return unsigned + "." + b64.EncodeToString(mac.Sum(nil))
}

// Parse 校验并解析 JWT；签名错误/格式非法/已过期均返回错误。
func Parse(secret, token string) (*Claims, error) {
	parts := strings.Split(token, ".")
	if len(parts) != 3 {
		return nil, ErrTokenInvalid
	}
	unsigned := parts[0] + "." + parts[1]
	mac := hmac.New(sha256.New, []byte(secret))
	mac.Write([]byte(unsigned))
	want, err := b64.DecodeString(parts[2])
	if err != nil || !hmac.Equal(want, mac.Sum(nil)) {
		return nil, ErrTokenInvalid
	}
	payload, err := b64.DecodeString(parts[1])
	if err != nil {
		return nil, ErrTokenInvalid
	}
	var c Claims
	if err := json.Unmarshal(payload, &c); err != nil {
		return nil, ErrTokenInvalid
	}
	if c.Sub == "" {
		return nil, ErrTokenInvalid
	}
	if time.Now().Unix() >= c.Exp {
		return nil, ErrTokenExpired
	}
	return &c, nil
}

// TokenTTL 令牌有效期：7 天（演示便利；生产应短一些并配刷新机制）。
const TokenTTL = 7 * 24 * time.Hour
