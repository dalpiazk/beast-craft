"""Unit tests for meshy.py's pure helpers (no network, no API key needed). Standard library unittest only.

  python -m unittest Tooling/ArtLab/scripts/test_meshy.py -v    (from the repo root)
  python test_meshy.py -v                                       (from Tooling/ArtLab/scripts)

Not run by CI (ArtLab is local-only tooling; see README.md), but safe to run anywhere -- makes no network
calls and touches no files outside a temp dir.
"""
import base64
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import meshy  # noqa: E402


class RedactTests(unittest.TestCase):
    def test_strips_key_from_text(self):
        self.assertEqual(meshy.redact("Authorization: Bearer msy_abc123", "msy_abc123"),
                          f"Authorization: Bearer {meshy.REDACTED}")

    def test_no_key_is_a_noop(self):
        self.assertEqual(meshy.redact("hello world", None), "hello world")

    def test_repeated_occurrences_all_stripped(self):
        text = "key=msy_x key again=msy_x"
        self.assertNotIn("msy_x", meshy.redact(text, "msy_x"))

    def test_non_string_input_is_stringified(self):
        self.assertEqual(meshy.redact(404, "msy_x"), "404")


class DataUriTests(unittest.TestCase):
    def test_png_round_trips_and_has_correct_mime(self):
        payload = b"\x89PNG\r\n\x1a\nfake-but-fine-for-this-test"
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d) / "in.png"
            p.write_bytes(payload)
            uri, count = meshy.image_to_data_uri(p)
            self.assertTrue(uri.startswith("data:image/png;base64,"))
            self.assertEqual(count, len(payload))
            b64 = uri.split(",", 1)[1]
            self.assertEqual(base64.b64decode(b64), payload)

    def test_jpeg_mime(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d) / "in.jpg"
            p.write_bytes(b"\xff\xd8\xff\xe0fake")
            uri, _ = meshy.image_to_data_uri(p)
            self.assertTrue(uri.startswith("data:image/jpeg;base64,"))

    def test_unsupported_extension_raises(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d) / "in.gif"
            p.write_bytes(b"GIF89a")
            with self.assertRaises(ValueError):
                meshy.image_to_data_uri(p)


class CostLookupTests(unittest.TestCase):
    def test_meshy6_lite_mesh_only(self):
        credits, note = meshy.cost_lookup("meshy-6-lite", False, "2k", False)
        self.assertEqual(credits, 5)
        self.assertIn("mesh only", note)

    def test_meshy6_lite_textured_2k(self):
        credits, _ = meshy.cost_lookup("meshy-6-lite", True, "2k", False)
        self.assertEqual(credits, 15)

    def test_meshy6_lite_8k_not_offered(self):
        credits, note = meshy.cost_lookup("meshy-6-lite", True, "8k", False)
        self.assertIsNone(credits)
        self.assertIn("no documented", note)

    def test_meshy71_textured_2k(self):
        credits, _ = meshy.cost_lookup("meshy-7.1", True, "2k", False)
        self.assertEqual(credits, 30)

    def test_meshy71_8k(self):
        credits, _ = meshy.cost_lookup("meshy-7.1", True, "8k", False)
        self.assertEqual(credits, 35)

    def test_latest_assumed_as_meshy71(self):
        credits, note = meshy.cost_lookup("latest", True, "2k", False)
        self.assertEqual(credits, 30)
        self.assertIn("ASSUMED", note)

    def test_pbr_noted_but_not_priced(self):
        credits, note = meshy.cost_lookup("meshy-6", True, "2k", True)
        self.assertEqual(credits, 30)
        self.assertIn("enable_pbr", note)


class BuildBodyTests(unittest.TestCase):
    def test_minimal_textured_body(self):
        body = meshy.build_image_to_3d_body("data:image/png;base64,AAA", "latest", None, None, True, "2k", False)
        self.assertEqual(body["image_url"], "data:image/png;base64,AAA")
        self.assertEqual(body["ai_model"], "latest")
        self.assertTrue(body["should_texture"])
        self.assertEqual(body["texture_resolution"], "2k")
        self.assertNotIn("target_polycount", body)
        self.assertNotIn("should_remesh", body)
        self.assertNotIn("symmetry_mode", body)

    def test_no_texture_omits_texture_resolution(self):
        body = meshy.build_image_to_3d_body("uri", "meshy-6", None, None, False, "2k", False)
        self.assertFalse(body["should_texture"])
        self.assertNotIn("texture_resolution", body)

    def test_polycount_implies_remesh(self):
        body = meshy.build_image_to_3d_body("uri", "meshy-6", 20000, None, True, "2k", False)
        self.assertEqual(body["target_polycount"], 20000)
        self.assertIs(body["should_remesh"], True)

    def test_symmetry_passed_through(self):
        body = meshy.build_image_to_3d_body("uri", "meshy-6", None, "on", True, "2k", False)
        self.assertEqual(body["symmetry_mode"], "on")


class ApiKeyTests(unittest.TestCase):
    def test_process_env_wins_over_registry(self, monkeypatch=None):
        import os
        old = os.environ.get("MESHY_API_KEY")
        os.environ["MESHY_API_KEY"] = "msy_from_process_env"
        try:
            key, source = meshy.get_api_key()
            self.assertEqual(key, "msy_from_process_env")
            self.assertEqual(source, "process environment")
        finally:
            if old is None:
                del os.environ["MESHY_API_KEY"]
            else:
                os.environ["MESHY_API_KEY"] = old


if __name__ == "__main__":
    unittest.main()
