-- 002: 登录与角色权限（A1）+ 照片留档（B6）。
-- 迁移脚本每次启动都会整体执行，故全部使用幂等语句。

CREATE TABLE IF NOT EXISTS users (
  username TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  role TEXT NOT NULL,            -- admin | inspector
  password_hash TEXT NOT NULL,   -- hex(sha256(salt || ":" || password))
  salt TEXT NOT NULL,
  created_at INTEGER NOT NULL
);

-- 种子账号（INSERT OR IGNORE：已存在则不覆盖，避免启动时重置用户改过的密码）。
-- 密码为演示默认值：admin/admin123、inspector/inspector123（README 注明可改）。
INSERT OR IGNORE INTO users (username, display_name, role, password_hash, salt, created_at) VALUES
  ('admin', '系统管理员', 'admin',
   'bf6b5bdb74c79ece9fc0ad0ac9fb0359f9555d4f35a83b2e6ec69ae99e09603d', 'admin',
   strftime('%s','now') * 1000),
  ('inspector', '巡检员', 'inspector',
   'c734e69f8f3ad7b20bb63a0845025442739009a1676605f23e15aef8557bd330', 'inspector',
   strftime('%s','now') * 1000);

-- 问题照片留档（B6）：存 BLOB 避免文件路径管理；issue_id 主键 = 一问题一照片（后拍覆盖前拍）。
CREATE TABLE IF NOT EXISTS issue_photos (
  issue_id TEXT PRIMARY KEY,
  data BLOB NOT NULL,
  created_at INTEGER NOT NULL
);
