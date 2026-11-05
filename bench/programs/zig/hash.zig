const std = @import("std");
const c = std.c;

fn now() u64 {
    var ts: c.timespec = undefined;
    _ = c.clock_gettime(.MONOTONIC, &ts);
    return @as(u64, @intCast(ts.sec)) * 1000000000 + @as(u64, @intCast(ts.nsec));
}

pub fn main() !void {
    const size: usize = 8388608;
    const passes = 16;
    const buf = try std.heap.page_allocator.alloc(u8, size);
    var state: u64 = 12345;
    for (buf) |*b| {
        state = state *% 6364136223846793005 +% 1442695040888963407;
        b.* = @intCast(state >> 56);
    }
    const start = now();
    var h: u64 = 14695981039346656037;
    for (0..passes) |_| {
        for (buf) |b| h = (h ^ b) *% 1099511628211;
    }
    const elapsed = now() - start;
    var out: [64]u8 = undefined;
    const s = try std.fmt.bufPrint(&out, "{d} {d}\n", .{ elapsed, h });
    _ = c.write(1, s.ptr, s.len);
}
