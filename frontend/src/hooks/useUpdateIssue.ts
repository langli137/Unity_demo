// useUpdateIssue：字段编辑用例（v1.2）。以后端校验为准，成功返回最新 issue 供页面就地更新。
// 404 透出"问题不存在"；400 透出后端校验信息（如空标题/非法枚举）。

import { useCallback, useState } from 'react'
import { updateIssue, type UpdateIssueInput } from '../api/issues'
import type { Issue } from '../types/issue'

export function useUpdateIssue() {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = useCallback(
    async (id: string, input: UpdateIssueInput): Promise<Issue | null> => {
      setPending(true)
      setError(null)
      try {
        return await updateIssue(id, input)
      } catch (e) {
        setError(e instanceof Error ? e.message : '保存失败')
        return null
      } finally {
        setPending(false)
      }
    },
    [],
  )

  const clearError = useCallback(() => setError(null), [])

  return { save, pending, error, clearError }
}
