#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_BIN_DIR="${SCRIPT_DIR}/bin"
SOURCE_BIN="${SCRIPT_DIR}/bin/oaf"
INSTALL_DIR="${OAF_INSTALL_DIR:-$HOME/.local/bin}"
OAF_HOME="${OAF_HOME:-$HOME/.oaf}"
VERSIONS_DIR="${OAF_HOME}/versions"
CURRENT_FILE="${OAF_HOME}/current.txt"
SHIM_BIN="${INSTALL_DIR}/oaf"
REQUESTED_VERSION="${1:-}"

if [[ ! -f "${SOURCE_BIN}" ]]; then
  echo "Could not find '${SOURCE_BIN}'." >&2
  echo "Run this script from the extracted Oaf package root." >&2
  exit 1
fi

if [[ -z "${REQUESTED_VERSION}" ]]; then
  candidate_version="$("${SOURCE_BIN}" --version 2>/dev/null | head -n 1 | tr -d '\r' || true)"
  if [[ "${candidate_version}" =~ ^[vV]?[0-9]+([.][0-9]+){0,3}([\-+][0-9A-Za-z.]+)?$ ]]; then
    REQUESTED_VERSION="${candidate_version}"
  fi
fi

if [[ -z "${REQUESTED_VERSION}" ]]; then
  package_name="$(basename "${SCRIPT_DIR}")"
  if [[ "${package_name}" == oaf-* ]]; then
    REQUESTED_VERSION="${package_name#oaf-}"
  fi
fi

if [[ -z "${REQUESTED_VERSION}" ]]; then
  echo "Unable to determine Oaf version automatically." >&2
  echo "Run install with an explicit version, e.g. './install.sh 0.1.0'." >&2
  exit 1
fi

VERSION="${REQUESTED_VERSION#v}"
VERSION="${VERSION#V}"
if [[ ! "${VERSION}" =~ ^[0-9]+([.][0-9]+){0,3}([\-+][0-9A-Za-z.]+)?$ ]]; then
  echo "Resolved version '${VERSION}' is not valid." >&2
  echo "Run install with an explicit version, e.g. './install.sh 0.1.0'." >&2
  exit 1
fi
VERSION_BIN_DIR="${VERSIONS_DIR}/${VERSION}/bin"
TARGET_BIN="${VERSION_BIN_DIR}/oaf"

copy_bin_tree() {
  local source_dir="$1"
  local destination_dir="$2"

  copy_bin_tree_fallback() {
    local fallback_source="$1"
    local fallback_destination="$2"

    # Stream copy avoids macOS xattr re-application issues and does not rely on find.
    (cd "${fallback_source}" && tar -cf - . 2>/dev/null) | (cd "${fallback_destination}" && tar -xf - 2>/dev/null)
  }

  # On macOS, downloaded binaries may carry xattrs that cannot be re-applied.
  # Prefer copying without xattrs to avoid "Operation not permitted" failures.
  if [[ "$(uname -s)" == "Darwin" ]]; then
    if command -v xattr >/dev/null 2>&1; then
      xattr -cr "${source_dir}" 2>/dev/null || true
    fi

    if command -v rsync >/dev/null 2>&1; then
      if rsync -a --no-xattrs "${source_dir}/" "${destination_dir}/" 2>/dev/null; then
        return 0
      fi
    fi

    if command -v ditto >/dev/null 2>&1; then
      if ditto --noextattr --noqtn "${source_dir}" "${destination_dir}" 2>/dev/null; then
        return 0
      fi
    fi

    if cp -R -X "${source_dir}/." "${destination_dir}/" 2>/dev/null; then
      return 0
    fi

    if COPYFILE_DISABLE=1 cp -R "${source_dir}/." "${destination_dir}/" 2>/dev/null; then
      return 0
    fi

    copy_bin_tree_fallback "${source_dir}" "${destination_dir}"
    return 0
  fi

  if cp -R "${source_dir}/." "${destination_dir}/" 2>/dev/null; then
    return 0
  fi

  copy_bin_tree_fallback "${source_dir}" "${destination_dir}"
}

mkdir -p "${VERSION_BIN_DIR}"
copy_bin_tree "${SOURCE_BIN_DIR}" "${VERSION_BIN_DIR}"
chmod +x "${TARGET_BIN}"

mkdir -p "${OAF_HOME}"
printf '%s\n' "${VERSION}" > "${CURRENT_FILE}"

mkdir -p "${INSTALL_DIR}"
cat > "${SHIM_BIN}" <<EOF
#!/usr/bin/env bash
set -euo pipefail

OAF_HOME_DEFAULT="${OAF_HOME}"
OAF_HOME="\${OAF_HOME:-\${OAF_HOME_DEFAULT}}"
if [[ -z "\${OAF_HOME}" ]]; then
  OAF_HOME="\$HOME/.oaf"
fi
CURRENT_FILE="\${OAF_HOME}/current.txt"
if [[ ! -f "\${CURRENT_FILE}" ]]; then
  echo "No active Oaf version configured. Re-run install.sh." >&2
  exit 1
fi

VERSION="\$(tr -d '\r\n[:space:]' < "\${CURRENT_FILE}")"
TARGET="\${OAF_HOME}/versions/\${VERSION}/bin/oaf"
if [[ ! -x "\${TARGET}" ]]; then
  echo "Configured Oaf version '\${VERSION}' is missing: \${TARGET}" >&2
  exit 1
fi

exec "\${TARGET}" "\$@"
EOF
chmod +x "${SHIM_BIN}"

if ! "${TARGET_BIN}" --version >/dev/null 2>&1; then
  echo "Installed binary failed '--version': ${TARGET_BIN}" >&2
  echo "The package may contain an outdated CLI entrypoint." >&2
  exit 1
fi

if ! OAF_HOME="${OAF_HOME}" "${SHIM_BIN}" --version >/dev/null 2>&1; then
  echo "Installed shim failed '--version': ${SHIM_BIN}" >&2
  echo "Check write permissions for '${OAF_HOME}' and '${INSTALL_DIR}'." >&2
  exit 1
fi

path_line="export PATH=\"${INSTALL_DIR}:\$PATH\""

ensure_path_in_rc() {
  local rc_file="$1"
  if [[ -f "${rc_file}" ]]; then
    if ! grep -Fqs "${INSTALL_DIR}" "${rc_file}"; then
      printf '\n%s\n' "${path_line}" >> "${rc_file}"
    fi
  else
    printf '%s\n' "${path_line}" > "${rc_file}"
  fi
}

ensure_path_in_rc "${HOME}/.zshrc"
ensure_path_in_rc "${HOME}/.bashrc"

echo "Installed version ${VERSION} at: ${TARGET_BIN}"
echo "Active version set to: ${VERSION}"
echo "Shim installed at: ${SHIM_BIN}"
if [[ ":$PATH:" != *":${INSTALL_DIR}:"* ]]; then
  echo "Added PATH update to ~/.zshrc and ~/.bashrc."
  echo "Open a new shell or run: source ~/.zshrc"
fi

echo "Try:"
echo "  oaf --version"
echo "  oaf version"
