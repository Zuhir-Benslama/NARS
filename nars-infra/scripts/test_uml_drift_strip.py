"""Tests for the UML drift guards' comment/string stripping.

Both check_uml_*.py guards now strip comments and quoted literals before
searching for type/member identifiers, so a deleted member whose name survives
only in prose (a // TODO, an XML doc, a log message) is flagged instead of
false-passing. These tests pin that behavior for the backend (C#) and frontend
(TS/TSX/Vue) dialects without needing the repo source trees.
"""

from pathlib import Path

import check_uml_class_diagram as backend
import check_uml_vite_component_diagram as frontend


def test_csharp_strips_line_comment() -> None:
    stripped = backend.strip_non_code("int x; // detectBar() was here\nreturn x;")
    assert "detectBar" not in stripped
    assert "return" in stripped


def test_csharp_strips_block_comment() -> None:
    stripped = backend.strip_non_code("/* detectBar() removed in refactor */ return 1;")
    assert "detectBar" not in stripped
    assert "return" in stripped


def test_csharp_strips_string_and_char_literals() -> None:
    assert "detectBar" not in backend.strip_non_code('var msg = "detectBar()";')
    assert "detectBar" not in backend.strip_non_code(
        'var msg = "a \\" detectBar() \\" b";'
    )
    assert "detectBar" not in backend.strip_non_code("char c = 'd';")


def test_csharp_keeps_real_identifier() -> None:
    assert "detectBar" in backend.strip_non_code("void detectBar() { }")


def test_csharp_verbatim_string_double_quote_escape() -> None:
    text = 'var sql = @"SELECT "" detectBar() "" FROM users";'
    assert "detectBar" not in backend.strip_non_code(text)


def test_ts_strips_line_block_and_html_comments() -> None:
    assert "detectBar" not in frontend.strip_non_code("// detectBar()\nconst x = 1;")
    assert "detectBar" not in frontend.strip_non_code("/* detectBar() */ const x = 1;")
    assert "detectBar" not in frontend.strip_non_code("<!-- detectBar() -->")


def test_ts_strips_quote_and_template_literals() -> None:
    assert "detectBar" not in frontend.strip_non_code("const s = 'detectBar()';")
    assert "detectBar" not in frontend.strip_non_code('const s = "detectBar()";')
    assert "detectBar" not in frontend.strip_non_code("const s = `detectBar()`;")


def test_ts_keeps_interpolation_expression() -> None:
    assert "detectBar" in frontend.strip_non_code("const s = `count: ${detectBar()}`;")


def test_ts_keeps_real_identifier() -> None:
    assert "detectBar" in frontend.strip_non_code("export function detectBar() {}")


def test_backend_member_deleted_but_comment_only_is_flagged(tmp_path: Path) -> None:
    src = tmp_path / "Widget.cs"
    src.write_text(
        "public class Widget {\n"
        "    // DeleteFeature was removed in refactor 4\n"
        "    public int Kept() => 1;\n"
        "}\n",
        encoding="utf-8",
    )
    target = tmp_path / "base.py"
    target.write_text("", encoding="utf-8")
    assert not backend.member_visible([src], "DeleteFeature")
    assert backend.member_visible([src], "Kept")


def test_frontend_member_deleted_but_comment_only_is_flagged(tmp_path: Path) -> None:
    src = tmp_path / "widget.ts"
    src.write_text(
        "// deleteItem was removed in refactor 4\nexport function keepItem() {}\n",
        encoding="utf-8",
    )
    assert not frontend.member_visible([src], "deleteItem")
    assert frontend.member_visible([src], "keepItem")
