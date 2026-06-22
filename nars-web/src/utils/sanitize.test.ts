import { describe, it, expect } from "vitest"
import {
  sanitizeHtml,
  escapeHtml,
  sanitizeAttr,
  sanitizeApiText,
  createSafeTextElement,
} from "./sanitize"

describe("sanitize", () => {
  describe("escapeHtml", () => {
    it("escapes < and >", () => {
      expect(escapeHtml("<script>alert('xss')</script>")).toBe(
        "&lt;script&gt;alert('xss')&lt;/script&gt;",
      )
    })

    it("escapes &", () => {
      expect(escapeHtml("a & b")).toBe("a &amp; b")
    })

    it("passes double quotes through (not special in text)", () => {
      expect(escapeHtml('say "hello"')).toBe('say "hello"')
    })

    it("passes through plain text unchanged", () => {
      expect(escapeHtml("hello world")).toBe("hello world")
    })

    it("handles empty string", () => {
      expect(escapeHtml("")).toBe("")
    })
  })

  describe("sanitizeHtml", () => {
    it("strips script tags", () => {
      const result = sanitizeHtml("<script>alert('xss')</script>")
      expect(result).not.toContain("<script>")
    })

    it("allows safe HTML tags", () => {
      const result = sanitizeHtml("<strong>bold</strong> <br> <em>italic</em>")
      expect(result).toContain("<strong>bold</strong>")
      expect(result).toContain("<em>italic</em>")
    })

    it("strips disallowed tags", () => {
      const result = sanitizeHtml("<div><script>evil</script><span>safe</span></div>")
      expect(result).toContain("safe")
      expect(result).not.toContain("<script>")
    })

    it("allows style and class attributes", () => {
      const result = sanitizeHtml('<span class="foo" style="color:red">text</span>')
      expect(result).toContain("text")
    })

    it("removes event handler attributes", () => {
      const result = sanitizeHtml('<div onclick="evil()">click</div>')
      expect(result).not.toContain("onclick")
    })

    it("handles empty string", () => {
      expect(sanitizeHtml("")).toBe("")
    })
  })

  describe("sanitizeAttr", () => {
    it("escapes HTML special characters", () => {
      expect(sanitizeAttr("<script>\"&'")).toContain("&lt;")
      expect(sanitizeAttr("<script>\"&'")).toContain("&gt;")
      expect(sanitizeAttr("<script>\"&'")).toContain("&quot;")
      expect(sanitizeAttr("<script>\"&'")).toContain("&amp;")
    })

    it("strips null bytes", () => {
      expect(sanitizeAttr("bad\0value")).not.toContain("\0")
    })

    it("escapes backticks", () => {
      expect(sanitizeAttr("`backtick`")).toContain("&#96;")
    })

    it("passes through safe values", () => {
      expect(sanitizeAttr("hello-world_123")).toBe("hello-world_123")
    })

    it("handles empty string", () => {
      expect(sanitizeAttr("")).toBe("")
    })
  })

  describe("sanitizeApiText", () => {
    it("returns empty string for null", () => {
      expect(sanitizeApiText(null)).toBe("")
    })

    it("returns empty string for undefined", () => {
      expect(sanitizeApiText(undefined)).toBe("")
    })

    it("escapes HTML in API text", () => {
      const result = sanitizeApiText("<b>bold</b>")
      expect(result).not.toContain("<b>")
      expect(result).toContain("&lt;")
    })

    it("passes through safe text", () => {
      expect(sanitizeApiText("hello world")).toBe("hello world")
    })
  })

  describe("createSafeTextElement", () => {
    it("creates an element with sanitized text", () => {
      const el = createSafeTextElement("div", "hello")
      expect(el.tagName).toBe("DIV")
      expect(el.textContent).toBe("hello")
    })

    it("sets className when provided", () => {
      const el = createSafeTextElement("span", "text", "my-class")
      expect(el.className).toBe("my-class")
    })

    it("creates element without className", () => {
      const el = createSafeTextElement("p", "paragraph")
      expect(el.className).toBe("")
    })
  })
})
