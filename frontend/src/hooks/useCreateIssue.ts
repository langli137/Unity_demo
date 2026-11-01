// useCreateIssue：管理端补录问题用例（v1.2）。成功返回新 issue（含服务端生成 id）。

import { useCallback, useState } from 'react'
import { createIssue, type CreateIssueInput } from '../api/issues'
import type { Issue } from '../types/issue'

export function useCreateIssue() {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const create = useCallback(async (input: CreateIssueInput): Promise<Issue | null> => {
    setPending(true)
    setError(null)
    try {
      return await createIssue(input)
    } catch (e) {
      setError(e instanceof Error ? e.message : '创建失败')
      return null
    } finally {
      setPending(false)
    }
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { create, pending, error, clearError }
}
