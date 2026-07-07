#!/usr/bin/env bash
# fetch-cao2024.sh — download the Cao et al. 2024 supplementary dataset (Zenodo 13340841)
# and extract the PLATES4 .rot rotation files needed for plate-rate calibration.
#
# Dataset:
#   Cao, X., Flament, P., Zahirovic, S., et al. (2024).
#   "Earth's tectonic and plate boundary evolution over 1.8 billion years."
#   Geoscience Frontiers 15(6) 101922.  https://doi.org/10.1016/j.gsf.2024.101922
#   Supplementary data: Zenodo record 13340841 (https://zenodo.org/records/13340841)
#   License: CC-BY 4.0 (attribution required — see report for the citation).
#
# The record ships a single archive 1.8Ga_model_GSF.zip (~20 MB) containing the full
# GPlates project (polygons, coastlines, boundaries, two .rot files). Only the .rot
# files are extracted here; everything else stays in the archive.
#
# Output: data/1.8Ga_model_GSF/{1000_0_rotfile.rot, 1800_1000_rotfile.rot, README.txt}
# Idempotent: re-running skips the download if the zip is already present and valid.
#
# Usage:  ./fetch-cao2024.sh          (run from tools/rates/, or set TOOLS_RATES_DIR)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="${SCRIPT_DIR}/data"
RECORD_ID="13340841"
ZIP_NAME="1.8Ga_model_GSF.zip"
ZIP_URL="https://zenodo.org/api/records/${RECORD_ID}/files/${ZIP_NAME}/content"
ZIP_PATH="${DATA_DIR}/${ZIP_NAME}"
EXTRACT_DIR="${DATA_DIR}/1.8Ga_model_GSF"

# Rotation files of interest (the model splits the 1.8 Ga history into two files).
ROT_FILES=("1000_0_rotfile.rot" "1800_1000_rotfile.rot")

mkdir -p "${DATA_DIR}"

need_download=1
if [[ -f "${ZIP_PATH}" ]]; then
    # Validate the existing zip; re-download if it is truncated/corrupt.
    if python3 -c "import sys, zipfile; zipfile.ZipFile(sys.argv[1]).testzip()" "${ZIP_PATH}" >/dev/null 2>&1; then
        need_download=0
        echo "[fetch] Existing ${ZIP_NAME} is a valid zip; skipping download."
    else
        echo "[fetch] Existing ${ZIP_NAME} is corrupt/truncated; re-downloading."
        rm -f "${ZIP_PATH}"
    fi
fi

if [[ "${need_download}" -eq 1 ]]; then
    echo "[fetch] Downloading ${ZIP_NAME} from Zenodo ${RECORD_ID} (~20 MB)..."
    curl -fL --max-time 300 -o "${ZIP_PATH}" "${ZIP_URL}"
    echo "[fetch] Downloaded $(stat -f %z "${ZIP_PATH}" 2>/dev/null || stat -c %s "${ZIP_PATH}") bytes."
fi

echo "[fetch] Extracting .rot files from archive..."
# Render the bash array as a Python list literal so the heredoc stays valid Python.
ROT_FILES_PY=$(printf '[%s]' "$(printf "'%s'," "${ROT_FILES[@]}" | sed 's/,$//')")
python3 - <<PY
import os, sys, zipfile
zip_path = r"${ZIP_PATH}"
extract_dir = r"${EXTRACT_DIR}"
rot_files = ${ROT_FILES_PY}
os.makedirs(extract_dir, exist_ok=True)
with zipfile.ZipFile(zip_path) as z:
    names = {n: z.getinfo(n) for n in z.namelist()}
    for rf in rot_files:
        member = f"1.8Ga_model_GSF/{rf}"
        if member not in names:
            print(f"[fetch]   WARNING: {member} not found in archive", file=sys.stderr)
            continue
        z.extract(member, path=r"${DATA_DIR}")
        size = names[member].file_size
        print(f"[fetch]   extracted {member}  ({size} bytes)")
    # README for provenance.
    readme = "1.8Ga_model_GSF/README.txt"
    if readme in names:
        z.extract(readme, path=r"${DATA_DIR}")
PY

echo
echo "[fetch] Provenance:"
echo "  Record : https://zenodo.org/records/${RECORD_ID}"
echo "  DOI    : 10.5281/zenodo.${RECORD_ID}"
echo "  License: CC-BY 4.0 (Creative Commons Attribution 4.0 International)"
echo "  Files  :"
for rf in "${ROT_FILES[@]}"; do
    p="${EXTRACT_DIR}/${rf}"
    if [[ -f "${p}" ]]; then
        sz=$(stat -f %z "${p}" 2>/dev/null || stat -c %s "${p}")
        printf "    %-28s %10s bytes\n" "${rf}" "${sz}"
    fi
done
echo "[fetch] Done.  Rotation files ready under ${EXTRACT_DIR}"
