package config

import "os"

type Config struct {
	Port          string
	DBDriver      string
	DBPath        string
	AllowedOrigin string
}

// Load 只从环境变量读取配置，默认值面向本地开发。
func Load() Config {
	return Config{
		Port:          getEnv("PORT", "8090"),
		DBDriver:      getEnv("DB_DRIVER", "sqlite"),
		DBPath:        getEnv("DB_PATH", "data/issues.db"),
		AllowedOrigin: getEnv("ALLOWED_ORIGIN", "http://localhost:5173"),
	}
}

func getEnv(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
