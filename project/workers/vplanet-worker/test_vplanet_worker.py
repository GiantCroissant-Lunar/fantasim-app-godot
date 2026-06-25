import unittest
import os
import shutil
import asyncio
import json
from pathlib import Path

import vplanet_worker


class TestVPlanetWorker(unittest.TestCase):
    SCHEMA_DIR = (
        Path(vplanet_worker.__file__).resolve().parent.parent.parent
        / "contracts"
        / "App.NodeGraph"
        / "ExternalTools"
        / "Schemas"
        / "vplanet"
    )

    def _load_schema(self, name):
        path = self.SCHEMA_DIR / name
        with open(path, "r", encoding="ascii") as f:
            return json.load(f)

    def _schema_required(self, name):
        return self._load_schema(name)["required"]

    def setUp(self):
        self.original_env = dict(os.environ)
        
    def tearDown(self):
        os.environ.clear()
        os.environ.update(self.original_env)
        
    def test_import_safety(self):
        try:
            import vplanet_worker
            self.assertTrue(hasattr(vplanet_worker, "status"))
            self.assertTrue(hasattr(vplanet_worker, "input_build"))
            self.assertTrue(hasattr(vplanet_worker, "run"))
            self.assertTrue(hasattr(vplanet_worker, "output_parse"))
        except Exception as e:
            self.fail(f"Importing vplanet_worker failed: {e}")
            
    def test_status_unavailable_when_unset(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]
            
        import vplanet_worker
        res = asyncio.run(vplanet_worker.status())
        self.assertIn("status", res)
        self.assertIn("ok", res)
        self.assertFalse(res["status"]["available"])
        self.assertFalse(res["ok"])
        
    def test_input_build_writes_manifest_and_files(self):
        import vplanet_worker
        
        payload = {
            "systemName": "test_sys",
            "starBodyName": "test_star",
            "planetBodyName": "test_planet",
            "stopTimeYears": 1e9,
            "outputTimeYears": 1e5
        }
        res = asyncio.run(vplanet_worker.input_build(payload))
        
        self.assertIn("inputBundle", res)
        self.assertIn("job_id", res)
        
        bundle = res["inputBundle"]
        self.assertEqual(bundle["systemName"], "test_sys")
        self.assertEqual(bundle["starBodyName"], "test_star")
        self.assertEqual(bundle["planetBodyName"], "test_planet")
        
        root_path = Path(bundle["rootPath"])
        manifest_path = Path(bundle["manifestPath"])
        primary_path = Path(bundle["primaryPath"])
        
        self.assertTrue(root_path.exists())
        self.assertTrue(manifest_path.exists())
        self.assertTrue(primary_path.exists())
        
        # Clean up
        shutil.rmtree(root_path.parent)
        
    def test_run_fallback_writes_artifacts(self):
        os.environ["VPLANET_BIN"] = "/nonexistent/vplanet_binary_path"
        
        import vplanet_worker
        
        payload = {
            "systemName": "fallback_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth"
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]
        
        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id
        }))
        
        self.assertIn("runResult", run_res)
        self.assertEqual(run_res["job_id"], job_id)
        
        run_result = run_res["runResult"]
        self.assertTrue(run_result["fallback"])
        self.assertFalse(run_result["available"])
        self.assertEqual(run_result["returnCode"], 0)
        
        root_path = Path(run_result["rootPath"])
        self.assertTrue((root_path / "stdout.log").exists())
        self.assertTrue((root_path / "stderr.log").exists())
        self.assertTrue((root_path / "sun.forward").exists())
        self.assertTrue((root_path / "earth.forward").exists())
        
        # Clean up
        shutil.rmtree(root_path.parent)
        
    def test_output_parse_returns_table_from_fallback(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]
            
        import vplanet_worker
        
        payload = {
            "systemName": "parse_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth"
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]
        
        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id
        }))
        run_result = run_res["runResult"]
        
        parse_res = asyncio.run(vplanet_worker.output_parse({
            "runResult": run_result,
            "bodyName": "earth",
            "job_id": job_id
        }))
        
        self.assertIn("outputTable", parse_res)
        self.assertEqual(parse_res["job_id"], job_id)
        
        table = parse_res["outputTable"]
        self.assertEqual(table["bodyName"], "earth")
        self.assertTrue(table["fallback"])
        self.assertIn("Time", table["columns"])
        self.assertTrue(len(table["rows"]) >= 1)
        
        # Clean up
        shutil.rmtree(Path(run_result["rootPath"]).parent)

    def test_input_build_rejects_malicious_names(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        import vplanet_worker

        base_payload = {
            "systemName": "solarsystem",
            "starBodyName": "sun",
            "planetBodyName": "earth",
        }

        malicious_cases = [
            ("systemName", "../evil"),
            ("systemName", "earth/../../x"),
            ("systemName", ""),
            ("systemName", "."),
            ("systemName", "a/b"),
            ("systemName", 123),
            ("starBodyName", "../evil"),
            ("starBodyName", ""),
            ("starBodyName", "."),
            ("planetBodyName", "earth/../../x"),
            ("planetBodyName", ""),
            ("planetBodyName", "."),
            ("job_id", "../evil"),
            ("job_id", ""),
            ("job_id", "."),
        ]

        for field, value in malicious_cases:
            with self.subTest(field=field, value=value):
                payload = dict(base_payload)
                payload[field] = value
                with self.assertRaises(ValueError):
                    asyncio.run(vplanet_worker.input_build(payload))

    def test_run_rejects_malicious_bundle_job_id(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        import vplanet_worker

        with self.assertRaises(ValueError):
            asyncio.run(vplanet_worker.run({
                "inputBundle": {
                    "job_id": "../evil",
                    "rootPath": "/tmp/vplanet/evil/vplanet",
                    "starBodyName": "sun",
                    "planetBodyName": "earth",
                }
            }))

    def test_input_build_accepts_normal_names(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        import vplanet_worker

        normal_cases = [
            {
                "systemName": "sun",
                "starBodyName": "sun",
                "planetBodyName": "earth",
            },
            {
                "systemName": "test_star",
                "starBodyName": "test_star",
                "planetBodyName": "test-planet",
            },
        ]

        for payload in normal_cases:
            with self.subTest(payload=payload):
                res = asyncio.run(vplanet_worker.input_build(payload))
                self.assertIn("inputBundle", res)
                self.assertIn("job_id", res)
                root_path = Path(res["inputBundle"]["rootPath"])
                self.assertTrue(root_path.exists())
                shutil.rmtree(root_path.parent)

    def test_run_fallback_writes_deterministic_forward_files(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        import vplanet_worker

        payload = {
            "systemName": "fallback_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth"
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]

        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id
        }))

        run_result = run_res["runResult"]
        root_path = Path(run_result["rootPath"])

        expected_sun = (
            "# Time Luminosity Radius Temperature\n"
            "0.0 1.0 1.0 5778.0\n"
            "1.0e6 0.99 0.99 5770.0\n"
        )
        expected_earth = (
            "# Time SemiMajorAxis Eccentricity Obliquity\n"
            "0.0 1.0 0.0167 23.5\n"
            "1.0e6 1.0 0.0167 23.5\n"
        )

        self.assertEqual((root_path / "sun.forward").read_text(encoding="ascii"), expected_sun)
        self.assertEqual((root_path / "earth.forward").read_text(encoding="ascii"), expected_earth)

        shutil.rmtree(root_path.parent)

    def test_output_parse_returns_expected_shape_for_fallback_earth(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        import vplanet_worker

        payload = {
            "systemName": "shape_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth"
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]

        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id
        }))
        run_result = run_res["runResult"]

        parse_res = asyncio.run(vplanet_worker.output_parse({
            "runResult": run_result,
            "bodyName": "earth",
            "job_id": job_id
        }))

        self.assertIn("job_id", parse_res)
        self.assertEqual(parse_res["job_id"], job_id)
        self.assertIn("outputTable", parse_res)

        table = parse_res["outputTable"]
        self.assertEqual(set(table.keys()), {"bodyName", "columns", "rows", "sourcePath", "fallback"})
        self.assertEqual(table["bodyName"], "earth")
        self.assertTrue(table["fallback"])
        self.assertIn("Time", table["columns"])
        self.assertTrue(len(table["rows"]) >= 1)
        self.assertTrue(Path(table["sourcePath"]).exists())

        shutil.rmtree(Path(run_result["rootPath"]).parent)


    def test_status_response_matches_schema(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        res = asyncio.run(vplanet_worker.status())
        for key in self._schema_required("vplanet.status.response.schema.json"):
            self.assertIn(key, res)

    def test_input_build_response_matches_schema(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        payload = {
            "systemName": "schema_sys",
            "starBodyName": "schema_star",
            "planetBodyName": "schema_planet",
            "stopTimeYears": 1e8,
            "outputTimeYears": 1e4,
        }
        res = asyncio.run(vplanet_worker.input_build(payload))
        for key in self._schema_required("vplanet.input-build.response.schema.json"):
            self.assertIn(key, res)

        shutil.rmtree(Path(res["inputBundle"]["rootPath"]).parent)

    def test_run_response_matches_schema(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        payload = {
            "systemName": "run_schema_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth",
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]

        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id,
        }))
        for key in self._schema_required("vplanet.run.response.schema.json"):
            self.assertIn(key, run_res)

        shutil.rmtree(Path(run_res["runResult"]["rootPath"]).parent)

    def test_output_parse_response_matches_schema(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        payload = {
            "systemName": "parse_schema_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth",
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]

        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id,
        }))
        run_result = run_res["runResult"]

        parse_res = asyncio.run(vplanet_worker.output_parse({
            "runResult": run_result,
            "bodyName": "earth",
            "job_id": job_id,
        }))
        for key in self._schema_required("vplanet.output-parse.response.schema.json"):
            self.assertIn(key, parse_res)

        shutil.rmtree(Path(run_result["rootPath"]).parent)

    def test_output_table_matches_schema(self):
        if "VPLANET_BIN" in os.environ:
            del os.environ["VPLANET_BIN"]

        payload = {
            "systemName": "table_schema_sys",
            "starBodyName": "sun",
            "planetBodyName": "earth",
        }
        build_res = asyncio.run(vplanet_worker.input_build(payload))
        input_bundle = build_res["inputBundle"]
        job_id = build_res["job_id"]

        run_res = asyncio.run(vplanet_worker.run({
            "inputBundle": input_bundle,
            "job_id": job_id,
        }))
        run_result = run_res["runResult"]

        parse_res = asyncio.run(vplanet_worker.output_parse({
            "runResult": run_result,
            "bodyName": "earth",
            "job_id": job_id,
        }))
        table = parse_res["outputTable"]
        for key in self._schema_required("vplanet.output-table.schema.json"):
            self.assertIn(key, table)

        shutil.rmtree(Path(run_result["rootPath"]).parent)


if __name__ == "__main__":
    unittest.main()
