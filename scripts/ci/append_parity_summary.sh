#!/usr/bin/env bash
set -euo pipefail

SELF_TEST_OUTPUT="${1:-}"
SUMMARY_CONTEXT="${2:-}"

if [[ -z "${SELF_TEST_OUTPUT}" ]]; then
    echo "Usage: $0 <self-test-output-path> [context]" >&2
    exit 1
fi

if [[ ! -f "${SELF_TEST_OUTPUT}" ]]; then
    echo "Self-test output file not found: ${SELF_TEST_OUTPUT}" >&2
    exit 1
fi

SUMMARY_FILE="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

PARITY_CHECKS=$(
    cat <<'EOF'
integration::bytecode_and_mlir_targets_produce_identical_vm_results|Bytecode vs MLIR (VM)
integration::vm_and_native_runtimes_produce_identical_results|VM vs native (bytecode target)
integration::mlir_target_vm_and_native_runtimes_produce_identical_results|VM vs native (MLIR target)
EOF
)

if [[ -n "${SUMMARY_CONTEXT}" ]]; then
    heading="## Parity checks (${SUMMARY_CONTEXT})"
else
    heading="## Parity checks"
fi

{
    echo "${heading}"
    echo
    echo "| Check | Status |"
    echo "|---|---|"

    while IFS='|' read -r test_name label; do
        if [[ -z "${test_name}" ]]; then
            continue
        fi

        status="NOT RUN"
        if grep -Fq "FAIL ${test_name}" "${SELF_TEST_OUTPUT}"; then
            status="FAIL"
        elif grep -Fq "PASS ${test_name}" "${SELF_TEST_OUTPUT}"; then
            status="PASS"
        fi

        echo "| ${label} | ${status} |"
    done <<< "${PARITY_CHECKS}"

    echo
} >> "${SUMMARY_FILE}"
