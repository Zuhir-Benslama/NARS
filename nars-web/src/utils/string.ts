export function slugify(name: string): string {
  return (
    name
      .toLowerCase()
      // NFD decomposes both Latin accents (é → e + U+0301) and Arabic hamza
      // forms (ئ → ي + U+0654). Stripping the Latin combining marks removes the
      // accents; the following NFC recomposes the still-marked Arabic pairs back
      // to their original letters. Without it, "الجزائر" slurred into a
      // visually broken "الجزائر" (stray standalone combining hamza).
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .normalize("NFC")
      .replace(/[^a-z0-9\u0600-\u06FF\u4E00-\u9FFF\s-]/gu, "")
      .replace(/\s+/g, "-")
      .replace(/-+/g, "-")
      .replace(/^-|-$/g, "")
  )
}
