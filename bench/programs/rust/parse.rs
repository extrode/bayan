use std::time::Instant;

fn main() {
    let target: usize = 4194304;
    let passes = 8;
    let mut buf: Vec<u8> = Vec::with_capacity(target + 64);
    let mut state: u64 = 777;
    while buf.len() < target {
        for field in 0..3 {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
            let mut v = (state >> 33) % 1000000;
            let mut digits: Vec<u8> = Vec::new();
            if v == 0 { digits.push(48); }
            while v > 0 { digits.push(48 + (v % 10) as u8); v /= 10; }
            while let Some(d) = digits.pop() { buf.push(d); }
            buf.push(if field == 2 { 10 } else { 44 });
        }
    }
    let start = Instant::now();
    let mut sum: u64 = 0;
    for _ in 0..passes {
        let mut value: u64 = 0;
        for i in 0..buf.len() {
            let c = buf[i];
            if (48..=57).contains(&c) {
                value = value.wrapping_mul(10).wrapping_add((c - 48) as u64);
            } else {
                sum = sum.wrapping_mul(31).wrapping_add(value);
                value = 0;
            }
        }
    }
    println!("{} {}", start.elapsed().as_nanos(), sum);
}
