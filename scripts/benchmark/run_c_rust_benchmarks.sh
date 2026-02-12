#!/usr/bin/env bash
set -euo pipefail

ITERATIONS=5
SUM_N=5000000
PRIME_N=30000
MATRIX_N=48
OAF_MODE="all"
OAF_CLI=""
OUT_FILE=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --iterations)
            ITERATIONS="$2"
            shift 2
            ;;
        --sum-n)
            SUM_N="$2"
            shift 2
            ;;
        --prime-n|--sieve-n)
            PRIME_N="$2"
            shift 2
            ;;
        --matrix-n)
            MATRIX_N="$2"
            shift 2
            ;;
        --oaf-mode)
            OAF_MODE="$2"
            shift 2
            ;;
        --oaf-cli)
            OAF_CLI="$2"
            shift 2
            ;;
        --out)
            OUT_FILE="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
C_SOURCE="${ROOT_DIR}/benchmarks/native/bench_c.c"
RUST_SOURCE="${ROOT_DIR}/benchmarks/native/bench_rust.rs"

CC_BIN="${CC:-cc}"
if ! command -v "${CC_BIN}" >/dev/null 2>&1; then
    echo "C compiler not found: ${CC_BIN}" >&2
    exit 1
fi

if ! command -v rustc >/dev/null 2>&1; then
    echo "rustc not found on PATH." >&2
    exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

C_BIN="${TMP_DIR}/bench_c"
RUST_BIN="${TMP_DIR}/bench_rust"

"${CC_BIN}" -O3 -std=c11 -Wall -Wextra -pedantic "${C_SOURCE}" -o "${C_BIN}"
rustc -O -C target-cpu=native "${RUST_SOURCE}" -o "${RUST_BIN}"

COMMON_ARGS=(
    --iterations "${ITERATIONS}"
    --sum-n "${SUM_N}"
    --prime-n "${PRIME_N}"
    --matrix-n "${MATRIX_N}"
)

C_OUTPUT="$("${C_BIN}" "${COMMON_ARGS[@]}")"
RUST_OUTPUT="$("${RUST_BIN}" "${COMMON_ARGS[@]}")"

if [[ -n "${OAF_CLI}" ]]; then
    OAF_CMD=("${OAF_CLI}")
else
    OAF_CMD=(dotnet run --configuration Release --)
fi

OAF_ROWS_FILE="${TMP_DIR}/oaf_rows.csv"
: > "${OAF_ROWS_FILE}"

append_oaf_rows() {
    local label="$1"
    shift
    local output
    output="$("${OAF_CMD[@]}" --benchmark-kernels "$@" "${COMMON_ARGS[@]}")"
    printf '%s\n' "${output}" | sed "1d;s/^oaf,/${label},/" >> "${OAF_ROWS_FILE}"
}

case "${OAF_MODE}" in
    native)
        append_oaf_rows "oaf_exe" --native
        ;;
    vm)
        append_oaf_rows "oaf_vm"
        ;;
    tiered)
        append_oaf_rows "oaf_tiered" --tiered
        ;;
    both)
        append_oaf_rows "oaf_vm"
        append_oaf_rows "oaf_exe" --native
        ;;
    mlir)
        append_oaf_rows "oaf_mlir_vm" --compilation-target mlir
        append_oaf_rows "oaf_mlir_exe" --compilation-target mlir --native
        ;;
    all)
        append_oaf_rows "oaf_vm"
        append_oaf_rows "oaf_exe" --native
        append_oaf_rows "oaf_mlir_vm" --compilation-target mlir
        append_oaf_rows "oaf_mlir_exe" --compilation-target mlir --native
        ;;
    *)
        echo "Unknown --oaf-mode value '${OAF_MODE}'. Use 'native', 'tiered', 'vm', 'both', 'mlir', or 'all'." >&2
        exit 1
        ;;
esac

if [[ -z "${OUT_FILE}" ]]; then
    TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
    OUT_FILE="${ROOT_DIR}/benchmarks/results/native_compare_${TIMESTAMP}.csv"
fi

mkdir -p "$(dirname "${OUT_FILE}")"
{
    echo "language,algorithm,iterations,total_ms,mean_ms,checksum"
    printf '%s\n' "${C_OUTPUT}" | sed '1d'
    printf '%s\n' "${RUST_OUTPUT}" | sed '1d'
    cat "${OAF_ROWS_FILE}"
} > "${OUT_FILE}"

echo "Native benchmark results written to ${OUT_FILE}"
if command -v column >/dev/null 2>&1; then
    column -t -s, "${OUT_FILE}"
else
    cat "${OUT_FILE}"
fi

echo
echo "Relative performance (lower mean_ms is faster):"
awk -F, '
NR == 1 { next }
$1 == "c" { c[$2] = $5 + 0.0 }
$1 == "rust" { r[$2] = $5 + 0.0 }
$1 ~ /^oaf_/ { o[$1 SUBSEP $2] = $5 + 0.0 }
function ratio(numerator, denominator,   safe_denominator) {
    safe_denominator = denominator
    if (safe_denominator < 0.000001) {
        safe_denominator = 0.000001
    }
    return sprintf("%.3fx", numerator / safe_denominator)
}
END {
    printf "%-18s %-16s %-16s %-16s %-16s %-16s\n", "algorithm", "rust_vs_c_ratio", "oaf_vm_vs_c", "oaf_exe_vs_c", "oaf_mlir_vm_vs_c", "oaf_mlir_exe_vs_c"
    for (algo in c) {
        rust_ratio = (algo in r) ? ratio(r[algo], c[algo]) : "n/a"
        vm_key = "oaf_vm" SUBSEP algo
        exe_key = "oaf_exe" SUBSEP algo
        mlir_vm_key = "oaf_mlir_vm" SUBSEP algo
        mlir_exe_key = "oaf_mlir_exe" SUBSEP algo
        vm_ratio = (vm_key in o) ? ratio(o[vm_key], c[algo]) : "n/a"
        exe_ratio = (exe_key in o) ? ratio(o[exe_key], c[algo]) : "n/a"
        mlir_vm_ratio = (mlir_vm_key in o) ? ratio(o[mlir_vm_key], c[algo]) : "n/a"
        mlir_exe_ratio = (mlir_exe_key in o) ? ratio(o[mlir_exe_key], c[algo]) : "n/a"
        printf "%-18s %-16s %-16s %-16s %-16s %-16s\n", algo, rust_ratio, vm_ratio, exe_ratio, mlir_vm_ratio, mlir_exe_ratio
    }
}
' "${OUT_FILE}"

echo
echo "Oaf mode comparison (lower mean_ms is faster):"
awk -F, '
NR == 1 { next }
$1 ~ /^oaf_/ { o[$1, $2] = $5 + 0.0; alg[$2] = 1 }
function ratio(numerator, denominator,   safe_denominator) {
    safe_denominator = denominator
    if (safe_denominator < 0.000001) {
        safe_denominator = 0.000001
    }
    return sprintf("%.3fx", numerator / safe_denominator)
}
END {
    printf "%-18s %-16s %-16s %-16s %-16s\n", "algorithm", "vm_vs_native", "mlir_vm_vs_vm", "mlir_native_vs_native", "mlir_vm_vs_mlir_native"
    for (algo in alg) {
        vm = o["oaf_vm", algo]
        exe = o["oaf_exe", algo]
        mlir_vm = o["oaf_mlir_vm", algo]
        mlir_exe = o["oaf_mlir_exe", algo]

        vm_vs_exe = (("oaf_vm", algo) in o && ("oaf_exe", algo) in o) ? ratio(vm, exe) : "n/a"
        mlir_vm_vs_vm = (("oaf_mlir_vm", algo) in o && ("oaf_vm", algo) in o) ? ratio(mlir_vm, vm) : "n/a"
        mlir_exe_vs_exe = (("oaf_mlir_exe", algo) in o && ("oaf_exe", algo) in o) ? ratio(mlir_exe, exe) : "n/a"
        mlir_vm_vs_mlir_exe = (("oaf_mlir_vm", algo) in o && ("oaf_mlir_exe", algo) in o) ? ratio(mlir_vm, mlir_exe) : "n/a"

        printf "%-18s %-16s %-16s %-16s %-16s\n", algo, vm_vs_exe, mlir_vm_vs_vm, mlir_exe_vs_exe, mlir_vm_vs_mlir_exe
    }
}
' "${OUT_FILE}"
