// useIssues：问题列表的筛选/分页/加载态/错误态 + 10 秒轮询刷新。

import { useCallback, useEffect, useRef, useState } from 'react'
import { listIssues } from '../api/issues'
import type { Category, Issue, Status } from '../types/issue'

const POLL_INTERVAL_MS = 10_000

export interface IssueFilters {
  status: Status | ''
  category: Category | ''
  page: number
  pageSize: number
}

const DEFAULT_FILTERS: IssueFilters = {
  status: '',
  category: '',
  page: 1,
  pageSize: 20,
}

export function useIssues() {
  const [filters, setFilters] = useState<IssueFilters>(DEFAULT_FILTERS)
  const [items, setItems] = useState<Issue[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // 用 ref 持有最新 filters，轮询定时器只注册一次。
  const filtersRef = useRef(filters)
  filtersRef.current = filters

  const load = useCallback(async () => {
    try {
      const data = await listIssues(filtersRef.current)
      setItems(data.items ?? [])
      setTotal(data.total)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [])

  // 筛选/分页变化时立即加载（并回到第一页）。
  const updateFilters = useCallback(
    (patch: Partial<Pick<IssueFilters, 'status' | 'category'>>) => {
      setFilters((f) => ({ ...f, ...patch, page: 1 }))
    },
    [],
  )
  const setPage = useCallback((page: number) => {
    setFilters((f) => ({ ...f, page }))
  }, [])

  useEffect(() => {
    void load()
  }, [load, filters])

  // 10 秒轮询（不做 WebSocket）。
  useEffect(() => {
    const timer = window.setInterval(() => void load(), POLL_INTERVAL_MS)
    return () => window.clearInterval(timer)
  }, [load])

  return { items, total, loading, error, filters, updateFilters, setPage, refresh: load }
}
