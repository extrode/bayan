use std::time::Instant;

fn main() {
    let size: usize = 8388608;
    let passes = 16;
    let mut buf = Vec::with_capacity(size);
    let mut state: u64 = 12345;
    for _ in 0..size {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
        buf.push((state >> 56) as u8);
    }
    let start = Instant::now();
    let mut h: u64 = 14695981039346656037;
    for _ in 0..passes {
        for i in 0..size {
            h = (h ^ buf[i] as u64).wrapping_mul(1099511628211);
        }
    }
    println!("{} {}", start.elapsed().as_nanos(), h);
}
