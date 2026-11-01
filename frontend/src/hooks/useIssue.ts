// useIssue：单条问题详情加载与手动刷新（409 后需要刷新详情）。

import { useCallback, useEffect, useState } from 'react'
import { getIssue } from '../api/issues'
import type { Issue } from '../types/issue'

export function useIssue(id: string) {
  const [issue, setIssue] = useState<Issue | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true) // 手动重试时也给出加载反馈（列表页轮询不在本 hook，无闪烁问题）
    try {
      const data = await getIssue(id)
      setIssue(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return { issue, loading, error, refresh }
}
