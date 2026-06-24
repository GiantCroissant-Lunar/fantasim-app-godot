import asyncio
import os
import shutil
import subprocess
import json
import hashlib
from pathlib import Path

# Determine project root relative to this file
HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
ARTIFACTS = ROOT / "build" / "_artifacts" / "generated"

def _resolve_vplanet_bin():
    vplanet_bin = os.environ.get("VPLANET_BIN")
    if not vplanet_bin:
        return None, False
    resolved = shutil.which(vplanet_bin)
    if not resolved:
        return vplanet_bin, False
    resolved_path = Path(resolved)
    try:
        if resolved_path.exists() and os.access(resolved_path, os.X_OK):
            return str(resolved_path), True
    except Exception:
        pass
    return resolved, False

async def status(payload=None):
    bin_path, available = _resolve_vplanet_bin()
    version = None
    if available:
        try:
            proc = await asyncio.to_thread(
                subprocess.run,
                [bin_path, "-V"],
                capture_output=True,
                text=True,
                timeout=5
            )
            out = proc.stdout or proc.stderr
            if out:
                version = out.splitlines()[0].strip()
        except Exception:
            pass
            
    return {
        "status": {
            "available": available,
            "binPath": bin_path,
            "version": version,
            "message": "VPLanet is available" if available else "VPLanet binary not found or not executable"
        },
        "ok": available
    }

async def input_build(payload=None):
    payload = payload or {}
    system_name = payload.get("systemName", "solarsystem")
    star_body_name = payload.get("starBodyName", "sun")
    planet_body_name = payload.get("planetBodyName", "earth")
    
    stop_time_years = payload.get("stopTimeYears")
    if stop_time_years is None:
        stop_time_years = 4.6e9
    else:
        try:
            stop_time_years = float(stop_time_years)
        except (TypeError, ValueError):
            stop_time_years = 4.6e9
            
    output_time_years = payload.get("outputTimeYears")
    if output_time_years is None:
        output_time_years = 1.0e6
    else:
        try:
            output_time_years = float(output_time_years)
        except (TypeError, ValueError):
            output_time_years = 1.0e6
            
    job_id = payload.get("job_id")
    if not job_id:
        payload_str = json.dumps({
            "systemName": system_name,
            "starBodyName": star_body_name,
            "planetBodyName": planet_body_name,
            "stopTimeYears": stop_time_years,
            "outputTimeYears": output_time_years
        }, sort_keys=True)
        job_id = "vplanet_" + hashlib.sha256(payload_str.encode("utf-8")).hexdigest()[:8]
        
    job_vplanet_dir = ARTIFACTS / job_id / "vplanet"
    job_vplanet_dir.mkdir(parents=True, exist_ok=True)
    
    primary_path = job_vplanet_dir / "vpl.in"
    star_path = job_vplanet_dir / f"{star_body_name}.in"
    planet_path = job_vplanet_dir / f"{planet_body_name}.in"
    
    primary_content = f"""# Primary Input File
sSystemName     {system_name}
iVerbose        5
bOverwrite      1
saBodyFiles     {star_body_name}.in {planet_body_name}.in
sUnitMass       solar
sUnitLength     AU
sUnitTime       YEARS
sUnitAngle      d
bDoForward      1
dStopTime       {stop_time_years}
dOutputTime     {output_time_years}
"""
    await asyncio.to_thread(primary_path.write_text, primary_content, encoding="ascii")
    
    star_content = f"""# Star Body File
sName           {star_body_name}
saModules       stellar
dMass           1.0
dRadius         -1.0
dAge            5.0e7
"""
    await asyncio.to_thread(star_path.write_text, star_content, encoding="ascii")
    
    planet_content = f"""# Planet Body File
sName           {planet_body_name}
dMass           -3.003e-6
dSemi           1.0
dEcc            0.0167
"""
    await asyncio.to_thread(planet_path.write_text, planet_content, encoding="ascii")
    
    manifest_path = job_vplanet_dir / "manifest.json"
    manifest_data = {
        "systemName": system_name,
        "starBodyName": star_body_name,
        "planetBodyName": planet_body_name,
        "primaryPath": str(primary_path),
        "bodyPaths": {
            star_body_name: str(star_path),
            planet_body_name: str(planet_path)
        },
        "files": ["vpl.in", f"{star_body_name}.in", f"{planet_body_name}.in"]
    }
    
    def write_manifest():
        with open(manifest_path, "w", encoding="ascii") as f:
            json.dump(manifest_data, f, indent=2)
            
    await asyncio.to_thread(write_manifest)
    
    input_bundle = {
        "job_id": job_id,
        "rootPath": str(job_vplanet_dir),
        "manifestPath": str(manifest_path),
        "primaryPath": str(primary_path),
        "bodyPaths": {
            star_body_name: str(star_path),
            planet_body_name: str(planet_path)
        },
        "systemName": system_name,
        "starBodyName": star_body_name,
        "planetBodyName": planet_body_name
    }
    
    return {
        "inputBundle": input_bundle,
        "job_id": job_id
    }

async def run(payload=None):
    payload = payload or {}
    input_bundle = payload.get("inputBundle", {})
    timeout_seconds = payload.get("timeoutSeconds", 300)
    
    job_id = payload.get("job_id") or input_bundle.get("job_id")
    root_path_str = input_bundle.get("rootPath")
    if not job_id:
        if root_path_str:
            job_id = Path(root_path_str).parent.name
        else:
            job_id = "vplanet_run_default"
            
    if root_path_str:
        job_vplanet_dir = Path(root_path_str)
    else:
        job_vplanet_dir = ARTIFACTS / job_id / "vplanet"
        
    job_vplanet_dir.mkdir(parents=True, exist_ok=True)
    
    stdout_path = job_vplanet_dir / "stdout.log"
    stderr_path = job_vplanet_dir / "stderr.log"
    
    bin_path, available = _resolve_vplanet_bin()
    
    star_body_name = input_bundle.get("starBodyName", "sun")
    planet_body_name = input_bundle.get("planetBodyName", "earth")
    
    if available:
        primary_path = input_bundle.get("primaryPath")
        if not primary_path:
            primary_path = str(job_vplanet_dir / "vpl.in")
            
        cmd = [bin_path, Path(primary_path).name]
        
        try:
            proc = await asyncio.to_thread(
                subprocess.run,
                cmd,
                capture_output=True,
                text=True,
                timeout=timeout_seconds,
                cwd=str(job_vplanet_dir)
            )
            
            await asyncio.to_thread(stdout_path.write_text, proc.stdout, encoding="ascii")
            await asyncio.to_thread(stderr_path.write_text, proc.stderr, encoding="ascii")
            
            return_code = proc.returncode
            fallback = False
        except Exception as e:
            await asyncio.to_thread(stdout_path.write_text, "", encoding="ascii")
            await asyncio.to_thread(stderr_path.write_text, f"Execution failed: {str(e)}", encoding="ascii")
            return_code = -1
            fallback = True
            available = False
    else:
        return_code = 0
        fallback = True
        
    if fallback:
        fallback_stdout = "[fallback: VPLanet simulation run stub]\nSimulation complete.\n"
        await asyncio.to_thread(stdout_path.write_text, fallback_stdout, encoding="ascii")
        await asyncio.to_thread(stderr_path.write_text, "", encoding="ascii")
        
        star_forward_path = job_vplanet_dir / f"{star_body_name}.forward"
        star_forward_content = (
            "# Time Luminosity Radius Temperature\n"
            "0.0 1.0 1.0 5778.0\n"
            "1.0e6 0.99 0.99 5770.0\n"
        )
        await asyncio.to_thread(star_forward_path.write_text, star_forward_content, encoding="ascii")
        
        planet_forward_path = job_vplanet_dir / f"{planet_body_name}.forward"
        planet_forward_content = (
            "# Time SemiMajorAxis Eccentricity Obliquity\n"
            "0.0 1.0 0.0167 23.5\n"
            "1.0e6 1.0 0.0167 23.5\n"
        )
        await asyncio.to_thread(planet_forward_path.write_text, planet_forward_content, encoding="ascii")

    run_result = {
        "job_id": job_id,
        "rootPath": str(job_vplanet_dir),
        "stdoutPath": str(stdout_path),
        "stderrPath": str(stderr_path),
        "outputPath": str(job_vplanet_dir),
        "returnCode": return_code,
        "fallback": fallback,
        "available": available
    }
    
    return {
        "runResult": run_result,
        "job_id": job_id
    }

async def output_parse(payload=None):
    payload = payload or {}
    run_result = payload.get("runResult", {})
    body_name = payload.get("bodyName", "sun")
    
    job_id = payload.get("job_id") or run_result.get("job_id")
    if not job_id:
        root_path = run_result.get("rootPath")
        if root_path:
            job_id = Path(root_path).parent.name
        else:
            job_id = "vplanet_parse_default"
            
    output_path_str = run_result.get("outputPath") or run_result.get("rootPath")
    if not output_path_str:
        raise ValueError("runResult.outputPath or runResult.rootPath is missing")
        
    output_path = Path(output_path_str)
    
    file_to_parse = None
    if output_path.is_dir():
        f_path = output_path / f"{body_name}.forward"
        b_path = output_path / f"{body_name}.backward"
        if f_path.exists():
            file_to_parse = f_path
        elif b_path.exists():
            file_to_parse = b_path
        else:
            for f in output_path.glob(f"*{body_name}*"):
                if f.is_file() and (f.suffix == ".forward" or f.suffix == ".backward"):
                    file_to_parse = f
                    break
            if not file_to_parse:
                file_to_parse = f_path
    else:
        file_to_parse = output_path
        
    columns = []
    rows = []
    
    def read_and_parse():
        if not file_to_parse.exists():
            raise FileNotFoundError(f"VPLanet output file not found: {file_to_parse}")
            
        cols = []
        rws = []
        with open(file_to_parse, "r", encoding="ascii") as f:
            lines = [line.strip() for line in f if line.strip()]
            
        if not lines:
            return cols, rws
            
        first_line = lines[0]
        has_header = False
        if first_line.startswith("#"):
            has_header = True
            cols = first_line.lstrip("#").strip().split()
        else:
            tokens = first_line.split()
            is_numeric = True
            for t in tokens:
                try:
                    float(t)
                except ValueError:
                    is_numeric = False
                    break
            if not is_numeric:
                has_header = True
                cols = tokens
                
        start_idx = 1 if has_header else 0
        for line in lines[start_idx:]:
            if line.startswith("#"):
                continue
            tokens = line.split()
            if not tokens:
                continue
            row = []
            for t in tokens:
                try:
                    row.append(float(t))
                except ValueError:
                    row.append(t)
            rws.append(row)
            
        if not cols and rws:
            cols = [f"col_{i}" for i in range(len(rws[0]))]
            
        return cols, rws

    try:
        columns, rows = await asyncio.to_thread(read_and_parse)
    except Exception as e:
        if run_result.get("fallback"):
            columns = ["Time", "Value"]
            rows = [[0.0, 0.0]]
        else:
            raise e
            
    output_table = {
        "bodyName": body_name,
        "columns": columns,
        "rows": rows,
        "sourcePath": str(file_to_parse),
        "fallback": run_result.get("fallback", False)
    }
    
    return {
        "outputTable": output_table,
        "job_id": job_id
    }
