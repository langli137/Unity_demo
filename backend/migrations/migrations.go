// Package migrations 以 embed 携带 SQL 迁移文件，保证建表语句单一来源。
package migrations

import "embed"

//go:embed *.sql
var FS embed.FS
