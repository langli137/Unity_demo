import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import { ErrorBoundary } from './components/ErrorBoundary'
import './styles.css'

const rootEl = document.getElementById('root')
if (!rootEl) {
  throw new Error('root element #root not found')
}

createRoot(rootEl).render(
  <StrictMode>
    {/* 渲染异常兜底（FR-W-04）：任何异常都不得呈现纯白页面。 */}
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </StrictMode>,
)
