import unittest
import os
import shutil
import asyncio
from pathlib import Path

class TestVPlanetWorker(unittest.TestCase):
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

if __name__ == "__main__":
    unittest.main()
