const blockClasses = {
  h1: 'text-base font-bold text-white mt-8 mb-4 border-b border-white/10 pb-2',
  h2: 'text-sm font-semibold text-white mt-6 mb-3 border-b border-white/5 pb-1.5',
  h3: 'text-xs font-semibold text-white mt-4 mb-2',
  p: 'mb-3 text-slate-300 leading-relaxed',
  li: 'list-disc ml-5 mb-1.5 text-slate-300',
  pre: 'bg-black/35 p-4 rounded border border-white/5 overflow-x-auto my-4 font-mono text-xs text-sky-400',
  code: 'bg-black/25 px-1.5 py-0.5 rounded text-sky-400 font-mono text-xs border border-white/5',
  strong: 'font-semibold text-sky-400',
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function renderInline(value: string): string {
  return escapeHtml(value)
    .replace(/\*\*([^*]+)\*\*/g, `<strong class="${blockClasses.strong}">$1</strong>`)
    .replace(/`([^`]+)`/g, `<code class="${blockClasses.code}">$1</code>`)
}

export function renderMarkdown(markdown: string): string {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  const html: string[] = []
  let inCodeBlock = false
  let codeBuffer: string[] = []

  const flushCode = () => {
    if (codeBuffer.length === 0) return
    html.push(`<pre class="${blockClasses.pre}"><code>${escapeHtml(codeBuffer.join('\n'))}</code></pre>`)
    codeBuffer = []
  }

  for (const line of lines) {
    if (line.trim().startsWith('```')) {
      if (inCodeBlock) {
        flushCode()
        inCodeBlock = false
      } else {
        inCodeBlock = true
      }
      continue
    }

    if (inCodeBlock) {
      codeBuffer.push(line)
      continue
    }

    const trimmed = line.trim()
    if (!trimmed) {
      continue
    }

    if (trimmed.startsWith('### ')) {
      html.push(`<h3 class="${blockClasses.h3}">${renderInline(trimmed.slice(4))}</h3>`)
    } else if (trimmed.startsWith('## ')) {
      html.push(`<h2 class="${blockClasses.h2}">${renderInline(trimmed.slice(3))}</h2>`)
    } else if (trimmed.startsWith('# ')) {
      html.push(`<h1 class="${blockClasses.h1}">${renderInline(trimmed.slice(2))}</h1>`)
    } else if (/^[-*]\s+/.test(trimmed)) {
      html.push(`<li class="${blockClasses.li}">${renderInline(trimmed.replace(/^[-*]\s+/, ''))}</li>`)
    } else {
      html.push(`<p class="${blockClasses.p}">${renderInline(line)}</p>`)
    }
  }

  if (inCodeBlock) {
    flushCode()
  }

  return html.join('\n')
}
