//! Focus's native core. C callers own input/output buffers; timeline handles must remain
//! alive for each call. No borrowed memory escapes a call. All exported operations catch panics.
#![allow(clippy::missing_safety_doc)]
use std::{
    collections::VecDeque,
    panic::{catch_unwind, AssertUnwindSafe},
    sync::Mutex,
};
mod storage;

#[repr(C)]
#[derive(Default, Debug, Clone, Copy)]
pub struct Stats {
    pub count: u64,
    pub seconds: f64,
    pub current_fps: f64,
    pub average_fps: f64,
    pub low_one: f64,
    pub low_point_one: f64,
    pub median: f64,
    pub p95: f64,
    pub p99: f64,
    pub maximum: f64,
    pub stutters: u64,
    pub stutter_percent: f64,
}

fn percentile(sorted: &[f64], p: f64) -> f64 {
    if sorted.is_empty() {
        return 0.0;
    }
    let pos = p.clamp(0.0, 1.0) * (sorted.len() - 1) as f64;
    let a = sorted[pos.floor() as usize];
    a + (sorted[pos.ceil() as usize] - a) * pos.fract()
}
fn low(sorted: &[f64], fraction: f64) -> f64 {
    if sorted.is_empty() {
        return 0.0;
    }
    let take = ((sorted.len() as f64 * fraction.clamp(0.0, 1.0)).ceil() as usize).max(1);
    1000.0 * take as f64 / sorted[sorted.len() - take..].iter().sum::<f64>()
}
pub fn compute(frames: &[f64]) -> Stats {
    let mut sorted: Vec<_> = frames
        .iter()
        .copied()
        .filter(|t| t.is_finite() && *t > 0.0)
        .collect();
    sorted.sort_by(f64::total_cmp);
    if sorted.is_empty() {
        return Stats::default();
    }
    let seconds = sorted.iter().sum::<f64>() / 1000.0;
    let median = percentile(&sorted, 0.5);
    let stutters = sorted.iter().filter(|t| **t > median * 2.0).count() as u64;
    Stats {
        count: sorted.len() as u64,
        seconds,
        average_fps: sorted.len() as f64 / seconds,
        low_one: low(&sorted, 0.01),
        low_point_one: low(&sorted, 0.001),
        median,
        p95: percentile(&sorted, 0.95),
        p99: percentile(&sorted, 0.99),
        maximum: *sorted.last().unwrap(),
        stutters,
        stutter_percent: stutters as f64 * 100.0 / sorted.len() as f64,
        ..Stats::default()
    }
}

pub struct Timeline {
    times: VecDeque<f64>,
    capacity: usize,
    total: u64,
}
impl Timeline {
    fn new(capacity: usize) -> Self {
        Self {
            times: VecDeque::new(),
            capacity: capacity.clamp(16, 2_000_000),
            total: 0,
        }
    }
    fn add(&mut self, time: f64) {
        if !time.is_finite() || self.times.back().is_some_and(|last| time <= *last) {
            return;
        }
        if self.times.len() == self.capacity {
            self.times.pop_front();
        }
        self.times.push_back(time);
        self.total += 1;
    }
    fn deltas(&self) -> Vec<f64> {
        self.times
            .iter()
            .zip(self.times.iter().skip(1))
            .map(|(a, b)| b - a)
            .collect()
    }
    fn fps(&self, now: f64, window: f64) -> f64 {
        let times: Vec<_> = self
            .times
            .iter()
            .filter(|t| **t >= now - window && **t <= now)
            .collect();
        if times.len() < 2 {
            return 0.0;
        }
        let last = **times.last().unwrap();
        let active_span = last - *times[0];
        let expected_interval = active_span / (times.len() - 1) as f64;
        let span = active_span + (now - last - expected_interval).max(0.0);
        if span > 0.0 {
            (times.len() - 1) as f64 * 1000.0 / span
        } else {
            0.0
        }
    }
}
type Handle = Mutex<Timeline>;
fn guarded(f: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(f)).unwrap_or(-1)
}

#[no_mangle]
pub extern "C" fn focus_abi_version() -> u32 {
    1
}
#[no_mangle]
pub extern "C" fn focus_timeline_new(capacity: u32) -> *mut Handle {
    catch_unwind(|| Box::into_raw(Box::new(Mutex::new(Timeline::new(capacity as usize)))))
        .unwrap_or(std::ptr::null_mut())
}
#[no_mangle]
pub unsafe extern "C" fn focus_timeline_free(handle: *mut Handle) {
    if !handle.is_null() {
        let _ = catch_unwind(|| drop(Box::from_raw(handle)));
    }
}
// operation: 0 add, 1 clear, 2 summary, 3 copy most recent deltas, 4 count window.
// Output summary: count, total, first, last, capacity; all buffers supplied by caller.
#[no_mangle]
pub unsafe extern "C" fn focus_timeline(
    handle: *mut Handle,
    operation: u32,
    a: f64,
    b: f64,
    output: *mut f64,
    length: u32,
) -> i32 {
    guarded(|| {
        if handle.is_null() {
            return -2;
        }
        let Ok(mut t) = (*handle).lock() else {
            return -3;
        };
        match operation {
            0 => t.add(a),
            1 => {
                t.times.clear();
                t.total = 0;
            }
            2 => {
                if output.is_null() || length < 5 {
                    return -2;
                }
                std::slice::from_raw_parts_mut(output, 5).copy_from_slice(&[
                    t.times.len() as f64,
                    t.total as f64,
                    *t.times.front().unwrap_or(&0.0),
                    *t.times.back().unwrap_or(&0.0),
                    t.capacity as f64,
                ]);
            }
            3 => {
                let deltas = t.deltas();
                let take = deltas.len().min(length as usize);
                if take > 0 {
                    if output.is_null() {
                        return -2;
                    }
                    std::ptr::copy_nonoverlapping(
                        deltas[deltas.len() - take..].as_ptr(),
                        output,
                        take,
                    );
                }
                return take as i32;
            }
            4 => return t.times.iter().filter(|x| **x >= a - b && **x <= a).count() as i32,
            _ => return -2,
        };
        0
    })
}
#[no_mangle]
pub unsafe extern "C" fn focus_timeline_stats(
    handle: *mut Handle,
    now: f64,
    window: f64,
    output: *mut Stats,
) -> i32 {
    guarded(|| {
        if handle.is_null() || output.is_null() {
            return -2;
        }
        let Ok(t) = (*handle).lock() else {
            return -3;
        };
        let mut stats = compute(&t.deltas());
        stats.count = t.total;
        stats.current_fps = t.fps(now, window);
        *output = stats;
        0
    })
}
#[no_mangle]
pub unsafe extern "C" fn focus_compute(input: *const f64, length: u32, output: *mut Stats) -> i32 {
    guarded(|| {
        if output.is_null() || (input.is_null() && length != 0) {
            return -2;
        }
        let frames = if length == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(input, length as usize)
        };
        *output = compute(frames);
        0
    })
}
#[no_mangle]
pub unsafe extern "C" fn focus_quantile(
    input: *const f64,
    length: u32,
    fraction: f64,
    mode: u32,
    output: *mut f64,
) -> i32 {
    guarded(|| {
        if output.is_null() || (input.is_null() && length != 0) || !fraction.is_finite() {
            return -2;
        }
        let mut values = if length == 0 {
            vec![]
        } else {
            std::slice::from_raw_parts(input, length as usize).to_vec()
        };
        values.retain(|x| x.is_finite() && (mode == 0 || *x > 0.0));
        values.sort_by(f64::total_cmp);
        *output = if mode == 0 {
            percentile(&values, fraction)
        } else {
            low(&values, fraction)
        };
        0
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preserves_first_spike() {
        let mut t = Timeline::new(16);
        for x in [100.0, 200.0, 210.0, 220.0] {
            t.add(x);
        }
        assert_eq!(t.deltas(), [100.0, 10.0, 10.0]);
        assert_eq!(compute(&t.deltas()).low_one, 10.0);
    }
    #[test]
    fn wrap_and_invalid_timestamps() {
        let mut t = Timeline::new(16);
        for x in 0..100 {
            t.add(x as f64);
        }
        t.add(f64::NAN);
        t.add(98.0);
        assert_eq!(t.deltas(), vec![1.0; 15]);
        assert_eq!(t.total, 100);
    }
    #[test]
    fn invalid_samples_and_stutters() {
        let s = compute(&[10.0, 10.0, 100.0, f64::NAN, f64::INFINITY, -1.0, 0.0]);
        assert_eq!(s.count, 3);
        assert_eq!(s.stutters, 1);
        assert_eq!(s.maximum, 100.0);
    }
    #[test]
    fn fps_decays_during_idle() {
        let mut t = Timeline::new(200);
        for x in 0..100 {
            t.add(x as f64 * 10.0);
        }
        assert!(t.fps(1400.0, 1000.0) < 100.0);
        assert_eq!(t.fps(2000.0, 1000.0), 0.0);
    }
    #[test]
    fn concurrent_timeline_access() {
        let t = std::sync::Arc::new(Mutex::new(Timeline::new(16)));
        std::thread::scope(|s| {
            let writer = t.clone();
            s.spawn(move || {
                for x in 0..10000 {
                    writer.lock().unwrap().add(x as f64);
                }
            });
            for _ in 0..1000 {
                assert!(t.lock().unwrap().deltas().iter().all(|v| *v > 0.0));
            }
        });
    }
}
