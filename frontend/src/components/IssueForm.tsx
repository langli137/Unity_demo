// 共用问题表单（v1.2）：详情页"编辑"与列表页"新建"复用同一套两列布局。
// 受控组件：值与变更全部由父级持有；本组件不发请求、不写业务判断。

import type { ChangeEvent, FormEvent } from 'react'
import type { Category, Priority } from '../types/issue'
import { CATEGORY_LABELS, PRIORITY_LABELS } from '../types/issue'

export interface IssueFormValue {
  title: string
  description: string
  category: Category
  priority: Priority
  reporterName: string
}

interface Props {
  value: IssueFormValue
  onChange: (next: IssueFormValue) => void
  onSubmit: (e: FormEvent) => void
  onCancel: () => void
  pending: boolean
  error?: string | null
  submitLabel?: string
}

const CATEGORY_OPTIONS = Object.keys(CATEGORY_LABELS) as Category[]
const PRIORITY_OPTIONS = Object.keys(PRIORITY_LABELS) as Priority[]

export function IssueForm({
  value,
  onChange,
  onSubmit,
  onCancel,
  pending,
  error,
  submitLabel = '保存',
}: Props) {
  const set = <K extends keyof IssueFormValue>(key: K, v: IssueFormValue[K]) =>
    onChange({ ...value, [key]: v })

  return (
    <form className="edit-form" onSubmit={onSubmit}>
      <label className="form-field form-field-wide">
        <span>标题 *</span>
        <input
          value={value.title}
          onChange={(e: ChangeEvent<HTMLInputElement>) => set('title', e.target.value)}
          placeholder="问题标题（必填）"
          required
        />
      </label>
      <label className="form-field form-field-wide">
        <span>描述</span>
        <textarea
          rows={3}
          value={value.description}
          onChange={(e: ChangeEvent<HTMLTextAreaElement>) => set('description', e.target.value)}
          placeholder="问题描述"
        />
      </label>
      <label className="form-field">
        <span>分类</span>
        <select
          value={value.category}
          onChange={(e: ChangeEvent<HTMLSelectElement>) => set('category', e.target.value as Category)}
        >
          {CATEGORY_OPTIONS.map((c) => (
            <option key={c} value={c}>{CATEGORY_LABELS[c]}</option>
          ))}
        </select>
      </label>
      <label className="form-field">
        <span>优先级</span>
        <select
          value={value.priority}
          onChange={(e: ChangeEvent<HTMLSelectElement>) => set('priority', e.target.value as Priority)}
        >
          {PRIORITY_OPTIONS.map((p) => (
            <option key={p} value={p}>{PRIORITY_LABELS[p]}</option>
          ))}
        </select>
      </label>
      <label className="form-field">
        <span>上报人</span>
        <input
          value={value.reporterName}
          onChange={(e: ChangeEvent<HTMLInputElement>) => set('reporterName', e.target.value)}
          placeholder="上报人姓名"
        />
      </label>
      <div className="form-actions form-field-wide">
        <button type="submit" className="btn-primary" disabled={pending || value.title.trim() === ''}>
          {pending ? '提交中…' : submitLabel}
        </button>
        <button type="button" onClick={onCancel} disabled={pending}>取消</button>
      </div>
      {error && <p className="error form-field-wide">{error}</p>}
    </form>
  )
}
