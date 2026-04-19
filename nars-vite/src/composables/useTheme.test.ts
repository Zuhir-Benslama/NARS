// ─── COMPOSABLES TESTS ───────────────────────────────────────────────────────
// Tests for theme composable functionality.

import { describe, it, expect, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { theme, setTheme, initTheme } from './useTheme'

describe('useTheme.ts', () => {
    beforeEach(() => {
        localStorage.clear()
        document.documentElement.removeAttribute('data-theme')
    })

    describe('setTheme', () => {
        beforeEach(() => {
            initTheme() // Set up the watcher
        })

        it('should update theme ref', async () => {
            setTheme('light')
            await nextTick()
            expect(theme.value).toBe('light')
        })

        it('should persist to localStorage', async () => {
            setTheme('dark')
            await nextTick()
            expect(localStorage.getItem('nars_theme')).toBe('dark')
        })

        it('should update DOM data-theme attribute', async () => {
            setTheme('light')
            await nextTick()
            expect(document.documentElement.getAttribute('data-theme')).toBe('light')

            setTheme('dark')
            await nextTick()
            expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
        })

        it('should support auto theme mode', async () => {
            setTheme('auto')
            await nextTick()
            expect(theme.value).toBe('auto')
            expect(document.documentElement.getAttribute('data-theme')).toBeNull()
        })
    })

    describe('initTheme', () => {
        it('should apply current theme ref to DOM', () => {
            // Set up a known theme in the ref
            setTheme('dark')
            initTheme()
            expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
        })

        it('should set up watcher for future changes', async () => {
            initTheme()

            setTheme('light')
            await nextTick()

            expect(document.documentElement.getAttribute('data-theme')).toBe('light')
            expect(localStorage.getItem('nars_theme')).toBe('light')
        })
    })
})
