// usePrioritySuggestion：B7 智能优先级建议（后端规则引擎）。
// 输入标题+描述 → 返回建议优先级与解释；供 IssueForm 的"智能建议"按钮驱动。

import { useCallback, useState } from 'react'
import { suggestPriority, type PrioritySuggestion } from '../api/issues'

export function usePrioritySuggestion() {
  const [suggesting, setSuggesting] = useState(false)
  const [suggestion, setSuggestion] = useState<PrioritySuggestion | null>(null)
  const [error, setError] = useState<string | null>(null)

  const fetchSuggestion = useCallback(async (title: string, description: string) => {
    if (!title.trim()) {
      setError('请先填写标题，再使用智能建议')
      return null
    }
    setSuggesting(true)
    setError(null)
    try {
      const res = await suggestPriority(title, description)
      setSuggestion(res)
      return res
    } catch (e) {
      setError(e instanceof Error ? e.message : '建议失败')
      return null
    } finally {
      setSuggesting(false)
    }
  }, [])

  const clear = useCallback(() => {
    setSuggestion(null)
    setError(null)
  }, [])

  return { suggestion, suggesting, error, fetchSuggestion, clear }
}
