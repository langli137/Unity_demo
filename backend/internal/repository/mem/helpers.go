package mem

import "time"

func millisToTime(ms int64) time.Time {
	return time.UnixMilli(ms).UTC()
}
