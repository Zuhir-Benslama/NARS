// ─── TOAST NOTIFICATIONS ──────────────────────────────────────────────────────
// Lightweight non-blocking replacement for alert().

import { UI_CONFIG } from './config'

type ToastType = 'success' | 'error' | 'info'

const DURATION = UI_CONFIG.toastDuration

let container: HTMLElement | null = null

function getContainer(): HTMLElement {
    if (!container) {
        container = document.createElement('div')
        container.id = 'nars-toast-container'
        container.style.cssText = [
            'position:fixed',
            'bottom:24px',
            'right:24px',
            'z-index:9999',
            'display:flex',
            'flex-direction:column',
            'gap:8px',
            'pointer-events:none',
        ].join(';')
        document.body.appendChild(container)
    }
    return container
}

export function showToast(message: string, type: ToastType = 'info'): void {
    // Guard: defer if DOM is not yet ready — use once listener to prevent leak
    if (!document.body) {
        document.addEventListener('DOMContentLoaded', () => showToast(message, type), { once: true })
        return
    }

    const bg: Record<ToastType, string> = {
        success: '#22c55e',
        error: '#ef4444',
        info: '#3b82f6',
    }

    const el = document.createElement('div')
    el.style.cssText = [
        `background:${bg[type]}`,
        'color:#fff',
        'padding:10px 18px',
        'border-radius:8px',
        'font-size:14px',
        'line-height:1.4',
        'max-width:340px',
        'box-shadow:0 4px 12px rgba(0,0,0,0.25)',
        'opacity:0',
        'transform:translateY(8px)',
        'transition:opacity 0.2s,transform 0.2s',
        'pointer-events:auto',
        'cursor:default',
    ].join(';')
    el.textContent = message

    getContainer().appendChild(el)

    // Trigger enter animation
    requestAnimationFrame(() => {
        el.style.opacity = '1'
        el.style.transform = 'translateY(0)'
    })

    // Auto-dismiss
    const dismiss = () => {
        el.style.opacity = '0'
        el.style.transform = 'translateY(8px)'
        el.addEventListener('transitionend', () => el.remove(), { once: true })
    }

    const timer = setTimeout(dismiss, DURATION)
    el.addEventListener('click', () => {
        clearTimeout(timer)
        dismiss()
    })
}

// ─── CONFIRM DIALOG ──────────────────────────────────────────────────────────
// Promise-based replacement for blocking window.confirm().
// Matches the app's existing glassmorphism/overlay style.

export function showConfirm(message: string): Promise<boolean> {
    return new Promise((resolve) => {
        if (!document.body) {
            document.addEventListener(
                'DOMContentLoaded',
                () => {
                    showConfirm(message).then(resolve)
                },
                { once: true },
            )
            return
        }

        // Backdrop
        const backdrop = document.createElement('div')
        backdrop.className = 'nars-confirm-backdrop'
        backdrop.style.cssText = [
            'position:fixed',
            'inset:0',
            'background:rgba(0,0,0,0.4)',
            'z-index:10000',
            'display:flex',
            'align-items:center',
            'justify-content:center',
            'opacity:0',
            'transition:opacity 0.15s',
        ].join(';')

        // Dialog
        const dialog = document.createElement('div')
        dialog.className = 'nars-confirm-dialog'
        dialog.style.cssText = [
            'background:#fff',
            'color:#1e293b',
            'padding:24px',
            'border-radius:12px',
            'max-width:380px',
            'width:90%',
            'box-shadow:0 8px 32px rgba(0,0,0,0.3)',
            'font-size:15px',
            'line-height:1.5',
        ].join(';')

        const msgEl = document.createElement('p')
        msgEl.style.marginBottom = '20px'
        msgEl.textContent = message
        dialog.appendChild(msgEl)

        const btnRow = document.createElement('div')
        btnRow.style.cssText = 'display:flex;gap:12px;justify-content:flex-end'

        const cancelBtn = document.createElement('button')
        cancelBtn.textContent = 'Cancel'
        cancelBtn.style.cssText = [
            'padding:8px 20px',
            'border:1px solid #cbd5e1',
            'border-radius:8px',
            'background:#f8fafc',
            'color:#475569',
            'font-size:14px',
            'cursor:pointer',
        ].join(';')
        cancelBtn.addEventListener('mouseenter', () => {
            cancelBtn.style.background = '#e2e8f0'
        })
        cancelBtn.addEventListener('mouseleave', () => {
            cancelBtn.style.background = '#f8fafc'
        })

        const okBtn = document.createElement('button')
        okBtn.textContent = 'Delete'
        okBtn.style.cssText = [
            'padding:8px 20px',
            'border:none',
            'border-radius:8px',
            'background:#ef4444',
            'color:#fff',
            'font-size:14px',
            'font-weight:600',
            'cursor:pointer',
        ].join(';')
        okBtn.addEventListener('mouseenter', () => {
            okBtn.style.background = '#dc2626'
        })
        okBtn.addEventListener('mouseleave', () => {
            okBtn.style.background = '#ef4444'
        })

        btnRow.appendChild(cancelBtn)
        btnRow.appendChild(okBtn)
        dialog.appendChild(btnRow)
        backdrop.appendChild(dialog)
        document.body.appendChild(backdrop)

        // Trigger animation
        requestAnimationFrame(() => {
            backdrop.style.opacity = '1'
        })

        const cleanup = (result: boolean) => {
            backdrop.style.opacity = '0'
            backdrop.addEventListener('transitionend', () => backdrop.remove(), { once: true })
            resolve(result)
        }

        cancelBtn.addEventListener('click', () => cleanup(false))
        okBtn.addEventListener('click', () => cleanup(true))

        // Close on backdrop click
        backdrop.addEventListener('click', (e) => {
            if (e.target === backdrop) cleanup(false)
        })

        // Close on Escape key
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                document.removeEventListener('keydown', onKey)
                cleanup(false)
            }
        }
        document.addEventListener('keydown', onKey)

        // Focus the cancel button for keyboard accessibility
        cancelBtn.focus()
    })
}
