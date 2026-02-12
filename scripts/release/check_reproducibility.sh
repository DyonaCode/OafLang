#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-}"
RUNTIME_ID="${2:-linux-x64}"
STRICT="${3:-}"

if [[ -z "${VERSION}" ]]; then
  echo "Usage: $0 <version> [runtime-id] [--strict]" >&2
  exit 1
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST_DIR="${ROOT_DIR}/dist"
PACKAGE_NAME="oaf-${VERSION}"

if [[ "${STRICT}" == "--strict" ]]; then
  STRICT="1"
else
  STRICT="${OAF_REPRO_STRICT:-0}"
fi

checksum_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
    return
  fi

  shasum -a 256 "$file" | awk '{print $1}'
}

build_and_capture_manifest() {
  local run_id="$1"
  local run_dir="$2"

  rm -rf "${DIST_DIR}/${PACKAGE_NAME}" "${DIST_DIR}/${PACKAGE_NAME}.tar.gz" "${DIST_DIR}/${PACKAGE_NAME}.zip"
  bash "${ROOT_DIR}/scripts/release/create_package.sh" "${VERSION}" "${RUNTIME_ID}" >&2

  local tar_archive="${DIST_DIR}/${PACKAGE_NAME}.tar.gz"
  local tar_archive_hash
  tar_archive_hash="$(checksum_file "${tar_archive}")"
  local zip_archive="${DIST_DIR}/${PACKAGE_NAME}.zip"
  local zip_archive_hash="n/a"
  if [[ -f "${zip_archive}" ]]; then
    zip_archive_hash="$(checksum_file "${zip_archive}")"
  fi

  mkdir -p "${run_dir}"
  tar -xzf "${tar_archive}" -C "${run_dir}"

  local extracted_root="${run_dir}/${PACKAGE_NAME}"
  local manifest="${run_dir}/manifest_${run_id}.txt"
  (
    cd "${extracted_root}"
    if command -v sha256sum >/dev/null 2>&1; then
      find . -type f -print0 | sort -z | xargs -0 sha256sum
    else
      find . -type f -print0 | sort -z | xargs -0 shasum -a 256
    fi
  ) > "${manifest}"

  echo "${tar_archive_hash}|${zip_archive_hash}"
}

tmp_dir="$(mktemp -d)"
trap 'rm -rf "${tmp_dir}"' EXIT

run1="${tmp_dir}/run1"
run2="${tmp_dir}/run2"

read -r tar_hash_1 zip_hash_1 <<< "$(build_and_capture_manifest 1 "${run1}" | tr '|' ' ')"
read -r tar_hash_2 zip_hash_2 <<< "$(build_and_capture_manifest 2 "${run2}" | tr '|' ' ')"

manifest1="${run1}/manifest_1.txt"
manifest2="${run2}/manifest_2.txt"

manifest_match="yes"
tar_match="yes"
zip_match="n/a"
if ! cmp -s "${manifest1}" "${manifest2}"; then
  manifest_match="no"
fi

if [[ "${tar_hash_1}" != "${tar_hash_2}" ]]; then
  tar_match="no"
fi

if [[ "${zip_hash_1}" != "n/a" && "${zip_hash_2}" != "n/a" ]]; then
  zip_match="yes"
  if [[ "${zip_hash_1}" != "${zip_hash_2}" ]]; then
    zip_match="no"
  fi
fi

echo "Reproducibility check (${RUNTIME_ID}):"
echo "  tar.gz hash run 1: ${tar_hash_1}"
echo "  tar.gz hash run 2: ${tar_hash_2}"
echo "  tar.gz hash match: ${tar_match}"
if [[ "${zip_hash_1}" != "n/a" || "${zip_hash_2}" != "n/a" ]]; then
  echo "  zip hash run 1: ${zip_hash_1}"
  echo "  zip hash run 2: ${zip_hash_2}"
  echo "  zip hash match: ${zip_match}"
fi
echo "  extracted file manifest match: ${manifest_match}"

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "## Release Reproducibility (${RUNTIME_ID})"
    echo
    echo "| Check | Result |"
    echo "|---|---|"
    echo "| tar.gz hash match | ${tar_match} |"
    if [[ "${zip_match}" != "n/a" ]]; then
      echo "| zip hash match | ${zip_match} |"
    fi
    echo "| Extracted file manifest match | ${manifest_match} |"
    echo
  } >> "${GITHUB_STEP_SUMMARY}"
fi

if [[ "${manifest_match}" == "no" ]]; then
  echo "Reproducibility mismatch detected (content differs between builds)." >&2
  if [[ "${STRICT}" == "1" ]]; then
    diff -u "${manifest1}" "${manifest2}" | sed -n '1,200p' >&2 || true
    exit 1
  fi
elif [[ "${tar_match}" == "no" || "${zip_match}" == "no" ]]; then
  echo "Archive hash mismatch detected, but extracted file contents match (likely archive metadata drift)." >&2
  if [[ "${STRICT}" == "1" ]]; then
    exit 1
  fi
fi
