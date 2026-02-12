use std::env;
use std::time::Instant;

#[derive(Clone, Copy)]
struct BenchmarkOptions {
    iterations: usize,
    sum_n: u64,
    prime_n: usize,
    matrix_n: usize,
}

fn parse_options() -> BenchmarkOptions {
    let mut options = BenchmarkOptions {
        iterations: 5,
        sum_n: 5_000_000,
        prime_n: 30_000,
        matrix_n: 48,
    };

    let mut args = env::args().skip(1);
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--iterations" => {
                let value = args.next().expect("Missing value for --iterations.");
                options.iterations = value.parse().expect("Invalid value for --iterations.");
            }
            "--sum-n" => {
                let value = args.next().expect("Missing value for --sum-n.");
                options.sum_n = value.parse().expect("Invalid value for --sum-n.");
            }
            "--prime-n" | "--sieve-n" => {
                let value = args.next().expect("Missing value for --prime-n.");
                options.prime_n = value.parse().expect("Invalid value for --prime-n.");
            }
            "--matrix-n" => {
                let value = args.next().expect("Missing value for --matrix-n.");
                options.matrix_n = value.parse().expect("Invalid value for --matrix-n.");
            }
            _ => {
                panic!("Unknown option '{}'.", arg);
            }
        }
    }

    if options.iterations == 0 {
        panic!("--iterations must be greater than zero.");
    }

    options
}

fn run_sum_xor(n: u64) -> u64 {
    let mut acc = 0u64;
    for i in 1..=n {
        acc = acc.wrapping_add((i ^ (i >> 3)).wrapping_add(i % 8));
    }
    acc
}

fn run_prime_trial(n: usize) -> u64 {
    if n < 2 {
        return 0;
    }

    let mut prime_count = 0u64;
    let mut checksum = 0u64;
    for candidate in 2..=n {
        let mut divisor = 2usize;
        let mut is_prime = true;
        while divisor * divisor <= candidate {
            if candidate % divisor == 0 {
                is_prime = false;
                break;
            }
            divisor += 1;
        }

        if !is_prime {
            continue;
        }

        prime_count += 1;
        checksum = checksum.wrapping_add((candidate as u64).wrapping_mul((prime_count % 16) + 1));
    }

    (prime_count << 32) ^ checksum
}

fn run_affine_grid(n: usize) -> u64 {
    if n == 0 {
        return 0;
    }

    let mut checksum = 0u64;
    for row in 0..n {
        for col in 0..n {
            let mut acc = 0u64;
            for k in 0..n {
                let a = (((row * 131) + (k * 17) + 13) % 256) as u64;
                let b = (((k * 19) + (col * 97) + 53) % 256) as u64;
                acc = acc.wrapping_add(a.wrapping_mul(b));
            }

            let index = (row as u64)
                .wrapping_mul(n as u64)
                .wrapping_add(col as u64);
            checksum ^= acc.wrapping_add(index.wrapping_mul(2_654_435_761u64));
        }
    }

    checksum
}

fn run_branch_mix(n: u64) -> u64 {
    let mut acc = 0u64;
    for i in 1..=n {
        if (i % 2) == 0 {
            acc = acc.wrapping_add(i << 1);
        } else {
            acc ^= i.wrapping_mul(3);
        }

        if (i % 7) == 0 {
            acc = acc.wrapping_add(i >> 2);
        } else {
            acc ^= i % 16;
        }

        if (i % 97) == 0 {
            acc = acc.wrapping_add(i.wrapping_mul((i % 13) + 1));
        }
    }

    acc
}

fn run_gcd_fold(n: usize) -> u64 {
    let mut checksum = 0u64;
    for i in 1..=n as u64 {
        let mut a = i.wrapping_mul(37).wrapping_add(17);
        let mut b = i.wrapping_mul(53).wrapping_add(19);
        while b != 0 {
            let t = a % b;
            a = b;
            b = t;
        }

        checksum = checksum.wrapping_add(a.wrapping_mul((i % 16) + 1));
    }

    checksum
}

fn run_lcg_stream(n: u64) -> u64 {
    let mut state = 123_456_789u64;
    let mut checksum = 0u64;
    for _ in 0..n {
        state = (state.wrapping_mul(1_103_515_245).wrapping_add(12_345)) % 2_147_483_647;
        if (state % 2) == 0 {
            checksum = checksum.wrapping_add(state);
        } else {
            checksum ^= state;
        }
    }

    checksum ^ state
}

fn mix_checksum(current: u64, value: u64, iteration: u64) -> u64 {
    let mixed = current
        ^ value
            .wrapping_add(0x9e37_79b9_7f4a_7c15)
            .wrapping_add(iteration << 6)
            .wrapping_add(iteration >> 2);
    mixed.rotate_left(13)
}

fn print_result(algorithm: &str, iterations: usize, total_ms: f64, checksum: u64) {
    let mean_ms = total_ms / iterations as f64;
    println!(
        "rust,{},{},{:.3},{:.6},{}",
        algorithm, iterations, total_ms, mean_ms, checksum
    );
}

fn main() {
    let options = parse_options();
    println!("language,algorithm,iterations,total_ms,mean_ms,checksum");

    let started = Instant::now();
    let mut sum_checksum = 0u64;
    for i in 0..options.iterations {
        sum_checksum = mix_checksum(sum_checksum, run_sum_xor(options.sum_n), i as u64);
    }
    print_result(
        "sum_xor",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        sum_checksum,
    );

    let started = Instant::now();
    let mut prime_checksum = 0u64;
    for i in 0..options.iterations {
        prime_checksum = mix_checksum(prime_checksum, run_prime_trial(options.prime_n), i as u64);
    }
    print_result(
        "prime_trial",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        prime_checksum,
    );

    let started = Instant::now();
    let mut grid_checksum = 0u64;
    for i in 0..options.iterations {
        grid_checksum = mix_checksum(grid_checksum, run_affine_grid(options.matrix_n), i as u64);
    }
    print_result(
        "affine_grid",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        grid_checksum,
    );

    let started = Instant::now();
    let mut branch_checksum = 0u64;
    for i in 0..options.iterations {
        branch_checksum = mix_checksum(branch_checksum, run_branch_mix(options.sum_n), i as u64);
    }
    print_result(
        "branch_mix",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        branch_checksum,
    );

    let started = Instant::now();
    let mut gcd_checksum = 0u64;
    for i in 0..options.iterations {
        gcd_checksum = mix_checksum(gcd_checksum, run_gcd_fold(options.prime_n), i as u64);
    }
    print_result(
        "gcd_fold",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        gcd_checksum,
    );

    let started = Instant::now();
    let mut lcg_checksum = 0u64;
    for i in 0..options.iterations {
        lcg_checksum = mix_checksum(lcg_checksum, run_lcg_stream(options.sum_n), i as u64);
    }
    print_result(
        "lcg_stream",
        options.iterations,
        started.elapsed().as_secs_f64() * 1000.0,
        lcg_checksum,
    );
}
