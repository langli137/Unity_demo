// ErrorBoundary：渲染异常兜底（FR-W-04）——任一异常不得呈现纯白页面。
// 仅捕获渲染期异常；请求错误由页面/错误态文案处理，二者共同覆盖"错误展示矩阵"。

import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  error: Error | null
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // 保留控制台记录便于定位，不影响用户可见的兜底渲染。
    console.error('渲染异常:', error, info.componentStack)
  }

  render() {
    const { error } = this.state
    if (error) {
      return (
        <main className="container">
          <section>
            <header className="page-header">
              <h1>页面出错了</h1>
            </header>
            <p className="error">渲染异常：{error.message || '未知错误'}</p>
            <div className="status-actions">
              <button onClick={() => this.setState({ error: null })}>重试</button>
              <button onClick={() => window.location.reload()}>重新加载页面</button>
            </div>
          </section>
        </main>
      )
    }
    return this.props.children
  }
}
