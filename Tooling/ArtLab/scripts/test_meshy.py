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


class RemeshBodyTests(unittest.TestCase):
    def test_minimal_body_only_input_task_id(self):
        body = meshy.build_remesh_body("0193bfc5-ee4f-73f8-8525-44b398884ce9", None, None, [])
        self.assertEqual(body, {"input_task_id": "0193bfc5-ee4f-73f8-8525-44b398884ce9"})

    def test_target_polycount_included_when_set(self):
        body = meshy.build_remesh_body("task-1", 8000, None, [])
        self.assertEqual(body["target_polycount"], 8000)

    def test_topology_included_when_set(self):
        body = meshy.build_remesh_body("task-1", None, "triangle", [])
        self.assertEqual(body["topology"], "triangle")

    def test_formats_included_when_set(self):
        body = meshy.build_remesh_body("task-1", None, None, ["glb", "fbx"])
        self.assertEqual(body["target_formats"], ["glb", "fbx"])

    def test_formats_omitted_when_empty(self):
        body = meshy.build_remesh_body("task-1", None, None, [])
        self.assertNotIn("target_formats", body)

    def test_full_body(self):
        body = meshy.build_remesh_body("task-1", 8000, "triangle", ["glb"])
        self.assertEqual(body, {
            "input_task_id": "task-1",
            "target_polycount": 8000,
            "topology": "triangle",
            "target_formats": ["glb"],
        })


class RemeshCostTests(unittest.TestCase):
    def test_flat_five_credits(self):
        self.assertEqual(meshy.REMESH_COST, 5)


class TextTo3DPreviewCostTests(unittest.TestCase):
    def test_meshy71_preview(self):
        credits, note = meshy.text_to_3d_preview_cost_lookup("meshy-7.1")
        self.assertEqual(credits, 20)
        self.assertIn("mesh only", note)

    def test_meshy6_lite_preview(self):
        credits, _ = meshy.text_to_3d_preview_cost_lookup("meshy-6-lite")
        self.assertEqual(credits, 5)

    def test_meshy_t2_preview(self):
        credits, _ = meshy.text_to_3d_preview_cost_lookup("meshy-t2")
        self.assertEqual(credits, 5)

    def test_latest_assumed_as_meshy71(self):
        credits, note = meshy.text_to_3d_preview_cost_lookup("latest")
        self.assertEqual(credits, 20)
        self.assertIn("ASSUMED", note)

    def test_meshy_t2_also_valid_for_image_to_3d(self):
        # meshy-t2 appears in both the Image to 3D and Text to 3D (Preview) pricing tables per the docs
        # (docs.meshy.ai/en/api/pricing, read 2026-10-04) -- not text-to-3d-only despite the "t2" name.
        self.assertIn("meshy-t2", meshy.IMAGE_TO_3D_COSTS)
        self.assertEqual(meshy.IMAGE_TO_3D_COSTS["meshy-t2"]["mesh"], meshy.TEXT_TO_3D_PREVIEW_COSTS["meshy-t2"])


class TextTo3DRefineCostTests(unittest.TestCase):
    def test_2k(self):
        credits, note = meshy.text_to_3d_refine_cost_lookup("2k")
        self.assertEqual(credits, 10)
        self.assertIn("flat rate", note)

    def test_4k_same_as_2k(self):
        credits, _ = meshy.text_to_3d_refine_cost_lookup("4k")
        self.assertEqual(credits, 10)

    def test_8k(self):
        credits, _ = meshy.text_to_3d_refine_cost_lookup("8k")
        self.assertEqual(credits, 15)

    def test_case_insensitive(self):
        credits, _ = meshy.text_to_3d_refine_cost_lookup("2K")
        self.assertEqual(credits, 10)

    def test_unknown_resolution_returns_none(self):
        credits, note = meshy.text_to_3d_refine_cost_lookup("16k")
        self.assertIsNone(credits)
        self.assertIn("no documented", note)


class BuildTextTo3DPreviewBodyTests(unittest.TestCase):
    def test_minimal_body(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, None, None, None, [])
        self.assertEqual(body, {"mode": "preview", "prompt": "a griffin", "ai_model": "latest"})

    def test_art_style_included_when_set(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", "realistic", None, None, None, None, None, [])
        self.assertEqual(body["art_style"], "realistic")

    def test_negative_prompt_included_when_set(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, "no rider", None, None, None, None, [])
        self.assertEqual(body["negative_prompt"], "no rider")

    def test_empty_negative_prompt_omitted(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, "", None, None, None, None, [])
        self.assertNotIn("negative_prompt", body)

    def test_polycount_implies_remesh(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, 8000, None, None, [])
        self.assertEqual(body["target_polycount"], 8000)
        self.assertIs(body["should_remesh"], True)

    def test_topology_included_when_set(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, "quad", None, None, None, [])
        self.assertEqual(body["topology"], "quad")

    def test_symmetry_passed_through(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, None, "on", None, [])
        self.assertEqual(body["symmetry_mode"], "on")

    def test_pose_mode_included_when_set(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, None, None, "a-pose", [])
        self.assertEqual(body["pose_mode"], "a-pose")

    def test_pose_mode_omitted_when_none(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, None, None, None, [])
        self.assertNotIn("pose_mode", body)

    def test_formats_included_when_set(self):
        body = meshy.build_text_to_3d_preview_body("a griffin", "latest", None, None, None, None, None, None, ["glb", "fbx"])
        self.assertEqual(body["target_formats"], ["glb", "fbx"])


class BuildTextTo3DRefineBodyTests(unittest.TestCase):
    def test_minimal_body(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, None, False, "2k", None, [])
        self.assertEqual(body, {"mode": "refine", "preview_task_id": "task-1",
                                 "enable_pbr": False, "texture_resolution": "2k"})

    def test_texture_prompt_included_when_set(self):
        body = meshy.build_text_to_3d_refine_body("task-1", "golden feathers", None, False, "2k", None, [])
        self.assertEqual(body["texture_prompt"], "golden feathers")

    def test_texture_image_url_included_when_set(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, "data:image/png;base64,AAA", False, "2k", None, [])
        self.assertEqual(body["texture_image_url"], "data:image/png;base64,AAA")

    def test_model_override_included_when_set(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, None, False, "2k", "meshy-6", [])
        self.assertEqual(body["ai_model"], "meshy-6")

    def test_model_omitted_when_none(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, None, False, "2k", None, [])
        self.assertNotIn("ai_model", body)

    def test_pbr_passed_through(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, None, True, "4k", None, [])
        self.assertIs(body["enable_pbr"], True)
        self.assertEqual(body["texture_resolution"], "4k")

    def test_formats_included_when_set(self):
        body = meshy.build_text_to_3d_refine_body("task-1", None, None, False, "2k", None, ["glb", "usdz"])
        self.assertEqual(body["target_formats"], ["glb", "usdz"])


class TextTo3DPromptLengthTests(unittest.TestCase):
    def test_max_chars_constant_matches_docs(self):
        self.assertEqual(meshy.TEXT_TO_3D_PROMPT_MAX_CHARS, 800)


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
