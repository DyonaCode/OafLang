#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NUGET_DIR="${ROOT_DIR}/dist/sdk"
TOOL_PATH="${ROOT_DIR}/.oaf/sdk-tools"

mkdir -p "${NUGET_DIR}" "${TOOL_PATH}"

dotnet pack "${ROOT_DIR}/Oaf.csproj" --configuration Release -p:Version="${VERSION}" >/dev/null

PACKAGE_ID="Oaf.Sdk"

if dotnet tool list --tool-path "${TOOL_PATH}" | awk '{print $1}' | grep -Fxq "${PACKAGE_ID}"; then
    dotnet tool update "${PACKAGE_ID}" \
      --tool-path "${TOOL_PATH}" \
      --add-source "${NUGET_DIR}" \
      --version "${VERSION}" \
      --ignore-failed-sources >/dev/null
else
    dotnet tool install "${PACKAGE_ID}" \
      --tool-path "${TOOL_PATH}" \
      --add-source "${NUGET_DIR}" \
      --version "${VERSION}" \
      --ignore-failed-sources >/dev/null
fi

echo "Installed '${PACKAGE_ID}' ${VERSION}."
echo "Tool path: ${TOOL_PATH}"
echo "Run with:"
echo "  ${TOOL_PATH}/oaf --help"
echo "Or add to PATH:"
echo "  export PATH=\"${TOOL_PATH}:\$PATH\""
