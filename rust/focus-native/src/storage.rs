use std::{
    fs::{self, OpenOptions},
    io::{self, Write},
    path::Path,
    sync::atomic::{AtomicU64, Ordering},
    time::Duration,
};
static SEQUENCE: AtomicU64 = AtomicU64::new(0);

#[cfg(windows)]
fn replace(from: &Path, to: &Path) -> io::Result<()> {
    use std::os::windows::ffi::OsStrExt;
    #[link(name = "kernel32")]
    extern "system" {
        fn MoveFileExW(from: *const u16, to: *const u16, flags: u32) -> i32;
    }
    let a: Vec<_> = from.as_os_str().encode_wide().chain(Some(0)).collect();
    let b: Vec<_> = to.as_os_str().encode_wide().chain(Some(0)).collect();
    if unsafe { MoveFileExW(a.as_ptr(), b.as_ptr(), 9) } == 0 {
        Err(io::Error::last_os_error())
    } else {
        Ok(())
    }
}
#[cfg(not(windows))]
fn replace(from: &Path, to: &Path) -> io::Result<()> {
    fs::rename(from, to)
}

pub fn atomic_write(path: &Path, data: &[u8]) -> io::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let mut name = path.as_os_str().to_os_string();
    name.push(format!(
        ".{}.{}.tmp",
        std::process::id(),
        SEQUENCE.fetch_add(1, Ordering::Relaxed)
    ));
    let temp = std::path::PathBuf::from(name);
    let result = (|| {
        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temp)?;
        file.write_all(data)?;
        file.sync_all()?;
        drop(file);
        for attempt in 0..4 {
            match replace(&temp, path) {
                Ok(()) => return Ok(()),
                Err(e) if attempt == 3 => return Err(e),
                Err(_) => std::thread::sleep(Duration::from_millis(40)),
            }
        }
        unreachable!()
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result
}

/// UTF-8 path and bytes are borrowed for this call; returns an OS error or negative ABI error.
#[no_mangle]
pub unsafe extern "C" fn focus_atomic_write(
    path: *const u8,
    path_len: u32,
    data: *const u8,
    len: u32,
) -> i32 {
    super::guarded(|| {
        if path.is_null() || path_len == 0 || (data.is_null() && len != 0) {
            return -2;
        }
        let Ok(path) = std::str::from_utf8(std::slice::from_raw_parts(path, path_len as usize))
        else {
            return -2;
        };
        if path.contains('\0') {
            return -2;
        }
        let bytes = if len == 0 {
            &[]
        } else {
            std::slice::from_raw_parts(data, len as usize)
        };
        match atomic_write(Path::new(path), bytes) {
            Ok(()) => 0,
            Err(e) => e.raw_os_error().unwrap_or(-4),
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn replaces_and_preserves_on_failure() {
        let root = std::env::temp_dir().join(format!("focus-rust-test-{}", std::process::id()));
        fs::create_dir_all(&root).unwrap();
        let p = root.join("设置.json");
        atomic_write(&p, b"old").unwrap();
        atomic_write(&p, b"new").unwrap();
        assert_eq!(fs::read(&p).unwrap(), b"new");
        let directory = root.join("directory");
        fs::create_dir_all(&directory).unwrap();
        assert!(atomic_write(&directory, b"bad").is_err());
        assert!(directory.is_dir());
        assert_eq!(fs::read_dir(&root).unwrap().count(), 2);
        fs::remove_dir_all(root).unwrap();
    }
}
