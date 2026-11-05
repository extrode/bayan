const std = @import("std");
const c = std.c;

fn now() u64 {
    var ts: c.timespec = undefined;
    _ = c.clock_gettime(.MONOTONIC, &ts);
    return @as(u64, @intCast(ts.sec)) * 1000000000 + @as(u64, @intCast(ts.nsec));
}

pub fn main() !void {
    const target: usize = 4194304;
    const passes = 8;
    const buf = try std.heap.page_allocator.alloc(u8, target + 64);
    var n: usize = 0;
    var state: u64 = 777;
    while (n < target) {
        for (0..3) |field| {
            state = state *% 6364136223846793005 +% 1442695040888963407;
            var v: u64 = (state >> 33) % 1000000;
            var digits: [8]u8 = undefined;
            var d: usize = 0;
            if (v == 0) {
                digits[d] = 48;
                d += 1;
            }
            while (v > 0) {
                digits[d] = @intCast(48 + v % 10);
                d += 1;
                v /= 10;
            }
            while (d > 0) {
                d -= 1;
                buf[n] = digits[d];
                n += 1;
            }
            buf[n] = if (field == 2) 10 else 44;
            n += 1;
        }
    }
    const data = buf[0..n];
    const start = now();
    var sum: u64 = 0;
    for (0..passes) |_| {
        var value: u64 = 0;
        for (data) |ch| {
            if (ch >= 48 and ch <= 57) {
                value = value *% 10 +% (ch - 48);
            } else {
                sum = sum *% 31 +% value;
                value = 0;
            }
        }
    }
    const elapsed = now() - start;
    var out: [64]u8 = undefined;
    const s = try std.fmt.bufPrint(&out, "{d} {d}\n", .{ elapsed, sum });
    _ = c.write(1, s.ptr, s.len);
}
