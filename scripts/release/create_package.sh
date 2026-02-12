#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
RUNTIME_ID="${2:-}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DIST_DIR="${ROOT_DIR}/dist"
PACKAGE_NAME="oaf-${VERSION}"
STAGING_DIR="${DIST_DIR}/${PACKAGE_NAME}"

echo "Creating release package '${PACKAGE_NAME}'..."
rm -rf "${STAGING_DIR}"
mkdir -p "${STAGING_DIR}/bin"

if [[ -z "${RUNTIME_ID}" ]]; then
  RUNTIME_ID="$(dotnet --info 2>/dev/null | awk -F': *' '/RID:/{print $2; exit}')"
fi

if [[ -z "${RUNTIME_ID}" ]]; then
  echo "Unable to determine Runtime Identifier (RID)." >&2
  echo "Pass one explicitly, e.g. osx-arm64, osx-x64, linux-x64, win-x64." >&2
  exit 1
fi

PUBLISH_ARGS=(
  "${ROOT_DIR}/Oaf.csproj"
  "--configuration" "Release"
  "--output" "${STAGING_DIR}/bin"
  "--runtime" "${RUNTIME_ID}"
  "--self-contained" "true"
  "-p:PublishSingleFile=true"
  "-p:DebugSymbols=false"
  "-p:DebugType=None"
)

dotnet publish "${PUBLISH_ARGS[@]}"

for path in docs examples SpecOverview.md SpecSyntax.md SpecRuntime.md SpecFileStructure.md SpecRoadmap.md; do
  if [[ -e "${ROOT_DIR}/${path}" ]]; then
    cp -R "${ROOT_DIR}/${path}" "${STAGING_DIR}/"
  fi
done

if [[ -f "${ROOT_DIR}/scripts/release/install.sh" ]]; then
  cp "${ROOT_DIR}/scripts/release/install.sh" "${STAGING_DIR}/install.sh"
  chmod +x "${STAGING_DIR}/install.sh"
fi

if [[ -f "${ROOT_DIR}/scripts/release/install.ps1" ]]; then
  cp "${ROOT_DIR}/scripts/release/install.ps1" "${STAGING_DIR}/install.ps1"
fi

cat > "${STAGING_DIR}/README.txt" <<EOF
Oaf Release Package ${VERSION}
Target Runtime: ${RUNTIME_ID}

Contents:
- bin/: Published CLI and runtime assets
- docs/: Guides and references
- examples/: Sample programs and tutorials
- Spec*.md: Language specification documents

Quick start:
1. Execute './bin/oaf --self-test' to validate installation.
2. Run example tests: './bin/oaf test ./examples'
3. Run a file: './bin/oaf run ./examples/basics/01_hello_and_return.oaf'
4. Build bytecode artifact: './bin/oaf build ./examples/basics/01_hello_and_return.oaf'
5. Publish executable: './bin/oaf publish ./examples/applications/01_sum_accumulator.oaf'

Install globally:
- macOS/Linux: './install.sh'
- Windows (PowerShell): '.\install.ps1'

Version management:
- Active version: './bin/oaf --version' (or 'oaf --version' after install)
- List installed versions: 'oaf version'
- Switch version: 'oaf version <num>'
EOF

mkdir -p "${DIST_DIR}"
TAR_PATH="${DIST_DIR}/${PACKAGE_NAME}.tar.gz"
ZIP_PATH="${DIST_DIR}/${PACKAGE_NAME}.zip"
ARCHIVE_TOUCH_TIME="${OAF_ARCHIVE_TOUCH_TIME:-200001010000.00}"
ENTRY_LIST_FILE="$(mktemp)"
trap 'rm -f "${ENTRY_LIST_FILE}"' EXIT

rm -f "${TAR_PATH}" "${ZIP_PATH}"

# Normalize timestamps so archive metadata is reproducible across runs.
find "${STAGING_DIR}" -exec touch -t "${ARCHIVE_TOUCH_TIME}" {} +

(
  cd "${DIST_DIR}"
  LC_ALL=C find "${PACKAGE_NAME}" -print | sort > "${ENTRY_LIST_FILE}"
  tar -cf - -T "${ENTRY_LIST_FILE}" | gzip -n > "${TAR_PATH}"
)

if command -v zip >/dev/null 2>&1; then
  (
    cd "${DIST_DIR}"
    zip -X -q "${ZIP_PATH}" -@ < "${ENTRY_LIST_FILE}"
  )
fi

echo "Package created:"
echo "  ${TAR_PATH}"
if [[ -f "${ZIP_PATH}" ]]; then
  echo "  ${ZIP_PATH}"
fi
