// useUpdateStatus：状态修改用例。以后端校验为准，成功/失败都由页面决定刷新策略。
// 409（含非法流转与并发冲突）不静默吞掉：错误信息透出，由页面提示并刷新。

import { useCallback, useState } from 'react'
import { updateStatus } from '../api/issues'
import { ApiError } from '../api/client'
import type { Issue, Status } from '../types/issue'

export function useUpdateStatus() {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // 返回 updated issue 或 null（失败时错误已写入 error）。
  const change = useCallback(
    async (id: string, status: Status): Promise<{ issue: Issue | null; conflict: boolean }> => {
      setPending(true)
      setError(null)
      try {
        const issue = await updateStatus(id, status)
        return { issue, conflict: false }
      } catch (e) {
        if (e instanceof ApiError && e.code === 409) {
          setError('状态已被他人更新')
          return { issue: null, conflict: true }
        }
        setError(e instanceof Error ? e.message : '操作失败')
        return { issue: null, conflict: false }
      } finally {
        setPending(false)
      }
    },
    [],
  )

  return { change, pending, error }
}
