#define _POSIX_C_SOURCE 200809L

#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

typedef struct benchmark_options {
    int iterations;
    uint64_t sum_n;
    uint32_t prime_n;
    uint32_t matrix_n;
} benchmark_options_t;

static double now_ms(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec * 1000.0 + (double)ts.tv_nsec / 1000000.0;
}

static uint64_t parse_u64(const char* value, const char* option_name) {
    char* end = NULL;
    const uint64_t parsed = strtoull(value, &end, 10);
    if (end == value || *end != '\0') {
        fprintf(stderr, "Invalid value '%s' for %s.\n", value, option_name);
        exit(1);
    }
    return parsed;
}

static uint64_t run_sum_xor(uint64_t n) {
    uint64_t acc = 0;
    for (uint64_t i = 1; i <= n; ++i) {
        acc += (i ^ (i >> 3)) + (i % 8u);
    }
    return acc;
}

static uint64_t run_prime_trial(uint32_t n) {
    if (n < 2u) {
        return 0;
    }

    uint64_t prime_count = 0;
    uint64_t checksum = 0;
    for (uint32_t candidate = 2; candidate <= n; ++candidate) {
        uint32_t divisor = 2;
        uint32_t is_prime = 1;
        while ((uint64_t)divisor * (uint64_t)divisor <= (uint64_t)candidate) {
            if (candidate % divisor == 0u) {
                is_prime = 0;
                break;
            }

            divisor++;
        }

        if (is_prime == 0u) {
            continue;
        }

        prime_count++;
        checksum += ((uint64_t)candidate * ((prime_count % 16u) + 1u));
    }

    return (prime_count << 32) ^ checksum;
}

static uint64_t run_affine_grid(uint32_t n) {
    if (n == 0) {
        return 0;
    }

    uint64_t checksum = 0;
    for (uint32_t row = 0; row < n; ++row) {
        for (uint32_t col = 0; col < n; ++col) {
            uint64_t acc = 0;
            for (uint32_t k = 0; k < n; ++k) {
                const uint64_t a = (uint64_t)(((row * 131u) + (k * 17u) + 13u) % 256u);
                const uint64_t b = (uint64_t)(((k * 19u) + (col * 97u) + 53u) % 256u);
                acc += a * b;
            }

            const uint64_t index = (uint64_t)row * (uint64_t)n + (uint64_t)col;
            checksum ^= acc + index * 2654435761ull;
        }
    }

    return checksum;
}

static uint64_t run_branch_mix(uint64_t n) {
    uint64_t acc = 0;
    for (uint64_t i = 1; i <= n; ++i) {
        if ((i % 2ull) == 0ull) {
            acc += i << 1;
        } else {
            acc ^= i * 3ull;
        }

        if ((i % 7ull) == 0ull) {
            acc += i >> 2;
        } else {
            acc ^= i % 16ull;
        }

        if ((i % 97ull) == 0ull) {
            acc += i * ((i % 13ull) + 1ull);
        }
    }

    return acc;
}

static uint64_t run_gcd_fold(uint32_t n) {
    uint64_t checksum = 0;
    for (uint32_t i = 1; i <= n; ++i) {
        uint64_t a = ((uint64_t)i * 37ull) + 17ull;
        uint64_t b = ((uint64_t)i * 53ull) + 19ull;
        while (b != 0ull) {
            const uint64_t t = a % b;
            a = b;
            b = t;
        }

        checksum += a * ((uint64_t)(i % 16u) + 1ull);
    }

    return checksum;
}

static uint64_t run_lcg_stream(uint64_t n) {
    uint64_t state = 123456789ull;
    uint64_t checksum = 0;

    for (uint64_t i = 0; i < n; ++i) {
        state = ((state * 1103515245ull) + 12345ull) % 2147483647ull;
        if ((state % 2ull) == 0ull) {
            checksum += state;
        } else {
            checksum ^= state;
        }
    }

    return checksum ^ state;
}

static uint64_t mix_checksum(uint64_t current, uint64_t value, uint64_t iteration) {
    current ^= value + 0x9e3779b97f4a7c15ull + (iteration << 6) + (iteration >> 2);
    return (current << 13) | (current >> (64 - 13));
}

static void parse_options(int argc, char** argv, benchmark_options_t* options) {
    for (int i = 1; i < argc; ++i) {
        if (strcmp(argv[i], "--iterations") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr, "Missing value for --iterations.\n");
                exit(1);
            }
            options->iterations = (int)parse_u64(argv[++i], "--iterations");
            continue;
        }

        if (strcmp(argv[i], "--sum-n") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr, "Missing value for --sum-n.\n");
                exit(1);
            }
            options->sum_n = parse_u64(argv[++i], "--sum-n");
            continue;
        }

        if (strcmp(argv[i], "--prime-n") == 0 || strcmp(argv[i], "--sieve-n") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr, "Missing value for --prime-n.\n");
                exit(1);
            }
            options->prime_n = (uint32_t)parse_u64(argv[++i], "--prime-n");
            continue;
        }

        if (strcmp(argv[i], "--matrix-n") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr, "Missing value for --matrix-n.\n");
                exit(1);
            }
            options->matrix_n = (uint32_t)parse_u64(argv[++i], "--matrix-n");
            continue;
        }

        fprintf(stderr, "Unknown option '%s'.\n", argv[i]);
        exit(1);
    }

    if (options->iterations <= 0) {
        fprintf(stderr, "--iterations must be greater than zero.\n");
        exit(1);
    }
}

static void print_result(const char* algorithm, int iterations, double total_ms, uint64_t checksum) {
    const double mean_ms = total_ms / (double)iterations;
    printf("c,%s,%d,%.3f,%.6f,%" PRIu64 "\n", algorithm, iterations, total_ms, mean_ms, checksum);
}

int main(int argc, char** argv) {
    benchmark_options_t options;
    options.iterations = 5;
    options.sum_n = 5000000ull;
    options.prime_n = 30000u;
    options.matrix_n = 48u;

    parse_options(argc, argv, &options);

    printf("language,algorithm,iterations,total_ms,mean_ms,checksum\n");

    double started = now_ms();
    uint64_t sum_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        sum_checksum = mix_checksum(sum_checksum, run_sum_xor(options.sum_n), (uint64_t)i);
    }
    print_result("sum_xor", options.iterations, now_ms() - started, sum_checksum);

    started = now_ms();
    uint64_t prime_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        prime_checksum = mix_checksum(prime_checksum, run_prime_trial(options.prime_n), (uint64_t)i);
    }
    print_result("prime_trial", options.iterations, now_ms() - started, prime_checksum);

    started = now_ms();
    uint64_t grid_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        grid_checksum = mix_checksum(grid_checksum, run_affine_grid(options.matrix_n), (uint64_t)i);
    }
    print_result("affine_grid", options.iterations, now_ms() - started, grid_checksum);

    started = now_ms();
    uint64_t branch_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        branch_checksum = mix_checksum(branch_checksum, run_branch_mix(options.sum_n), (uint64_t)i);
    }
    print_result("branch_mix", options.iterations, now_ms() - started, branch_checksum);

    started = now_ms();
    uint64_t gcd_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        gcd_checksum = mix_checksum(gcd_checksum, run_gcd_fold(options.prime_n), (uint64_t)i);
    }
    print_result("gcd_fold", options.iterations, now_ms() - started, gcd_checksum);

    started = now_ms();
    uint64_t lcg_checksum = 0;
    for (int i = 0; i < options.iterations; ++i) {
        lcg_checksum = mix_checksum(lcg_checksum, run_lcg_stream(options.sum_n), (uint64_t)i);
    }
    print_result("lcg_stream", options.iterations, now_ms() - started, lcg_checksum);

    return 0;
}
