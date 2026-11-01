package sqlite

import "time"

// 存储统一用 Unix 毫秒（INTEGER），出参再转 RFC3339，避免 TEXT 时间的时区歧义。

func millisToTime(ms int64) time.Time {
	return time.UnixMilli(ms).UTC()
}

func timeToMillis(t time.Time) int64 {
	return t.UTC().UnixMilli()
}

func nullable(s string) any {
	if s == "" {
		return nil
	}
	return s
}

func nullableTime(t *time.Time) any {
	if t == nil {
		return nil
	}
	return timeToMillis(*t)
}

func nullableMillis(ms *int64) any {
	if ms == nil {
		return nil
	}
	return *ms
}
