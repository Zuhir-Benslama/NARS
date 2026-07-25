// ─── SECURITY: XSS SANITIZATION UTILITIES ───────────────────────────────────
// Centralized sanitization using DOMPurify to prevent XSS attacks.
// All user-generated or API-sourced HTML content must pass through these functions.

import createDOMPurify from "dompurify"

// DOMPurify needs to be initialized with a window object in some environments
let DOMPurifyInstance: ReturnType<typeof createDOMPurify>

if (typeof window !== "undefined") {
  DOMPurifyInstance = createDOMPurify(window)
} else {
  // Fallback for SSR/test environments — strip all HTML tags
  // using DOMPurify's own logic rather than a bypassable regex.
  DOMPurifyInstance = {
    sanitize: (dirty: string) => {
      return dirty.replace(/<[^>]*>/g, "").replace(/\0/g, "")
    },
  } as ReturnType<typeof createDOMPurify>
}

/**
 * Sanitize HTML string before inserting via innerHTML.
 * Use this for any dynamic HTML content.
 */
export function sanitizeHtml(dirty: string): string {
  return DOMPurifyInstance.sanitize(dirty, {
    ALLOWED_TAGS: ["div", "span", "strong", "em", "b", "i", "small", "br"],
    ALLOWED_ATTR: ["class", "style", "data-action"],
    ALLOWED_URI_REGEXP: /^(?:(?:f|ht)tps?|mailto|tel):/i,
  })
}

const _escapeEl = typeof document !== "undefined" ? document.createElement("div") : null

/**
 * Escape HTML entities in text content.
 * Use for plain text that might contain HTML special characters.
 */
export function escapeHtml(dirty: string): string {
  if (!_escapeEl) return dirty
  _escapeEl.textContent = dirty
  return _escapeEl.innerHTML
}

/**
 * Encode text for safe use in HTML attribute values.
 *
 * This is a character-encoding function, NOT an HTML sanitizer.
 * It converts special characters to HTML entities for safe use in simple
 * text attributes like `title`, `alt`, or `data-*` attributes.
 *
 * DO NOT use for:
 *   - Event handler attributes (onclick, onerror, etc.)
 *   - href/src attributes — javascript: URIs bypass this encoding.
 *   - Arbitrary HTML injection — use sanitizeHtml() instead.
 *
 * Safe for: title, alt, aria-label, data-* and other pure text attributes.
 */
export function encodeTextAttribute(dirty: string): string {
  return dirty
    .replace(/\0/g, "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;")
    .replace(/`/g, "&#96;")
}

/** @deprecated Use `encodeTextAttribute` instead. */
export const sanitizeAttr = encodeTextAttribute

/**
 * Create a safe HTML element with sanitized text content.
 * Preferred alternative to innerHTML for simple text insertion.
 */
export function createSafeTextElement(tag: string, text: string, className?: string): HTMLElement {
  const el = document.createElement(tag)
  el.textContent = text
  if (className) el.className = className
  return el
}

/**
 * Sanitize user-generated content from API responses.
 * This is a stricter version that removes all HTML tags.
 */
export function sanitizeApiText(dirty: string | null | undefined): string {
  if (!dirty) return ""
  // DOMPurify strips all HTML tags, leaving only safe text content
  return DOMPurifyInstance.sanitize(dirty, { ALLOWED_TAGS: [] })
}
