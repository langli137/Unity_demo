// 装配处：config -> repository -> service -> handler -> router，依赖注入只在这里发生。
package main

import (
	"errors"
	"log"
	"net/http"

	"park-inspect/config"
	"park-inspect/internal/handler"
	"park-inspect/internal/repository/sqlite"
	"park-inspect/internal/service"
	"park-inspect/migrations"
)

func main() {
	cfg := config.Load()

	repo, err := sqlite.Open(cfg.DBPath, mustMigrationSQL())
	if err != nil {
		log.Fatalf("init repository: %v", err)
	}
	defer repo.Close()

	db := repo.ExposeDB()
	userRepo := sqlite.NewUserRepo(db)
	photoRepo := sqlite.NewPhotoRepo(db)

	issueSvc := service.NewIssueService(repo).WithPhotos(photoRepo)
	authSvc := service.NewAuthService(userRepo, cfg.JWTSecret)

	router := handler.NewRouter(
		handler.NewIssueHandler(issueSvc),
		handler.NewAuthHandler(authSvc),
		cfg.JWTSecret,
		cfg.AllowedOrigin,
	)

	log.Printf("server listening on :%s (driver=%s)", cfg.Port, cfg.DBDriver)
	if err := http.ListenAndServe(":"+cfg.Port, router); err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatalf("server: %v", err)
	}
}

func mustMigrationSQL() string {
	// 迁移文件按文件名顺序执行，单一来源在 migrations/ 目录。
	entries, err := migrations.FS.ReadDir(".")
	if err != nil {
		log.Fatalf("read migrations: %v", err)
	}
	var sqlText string
	for _, e := range entries {
		data, err := migrations.FS.ReadFile(e.Name())
		if err != nil {
			log.Fatalf("read migration %s: %v", e.Name(), err)
		}
		sqlText += string(data) + ";\n"
	}
	if sqlText == "" {
		log.Fatal("no migration files found")
	}
	return sqlText
}
