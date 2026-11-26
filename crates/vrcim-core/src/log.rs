#[cfg(any(windows, test))]
use std::sync::atomic::{AtomicUsize, Ordering};
use std::{
    fmt::{self, Write as _},
    fs::{self, File, OpenOptions},
    io::{self, Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
};

use tracing::{
    Event, Level, Subscriber,
    field::{Field, Visit},
};
use tracing_subscriber::{Layer, layer::Context};

pub const FILE_NAME: &str = "VRCInventoryManager.debug.log";
pub const MAX_LOG_BYTES: u64 = 2 * 1024 * 1024;
pub const MAX_MESSAGE_CHARS: usize = 1000;

#[cfg(any(windows, test))]
const MAX_EVENT_LOG_REPORTS: usize = 8;

#[cfg(test)]
thread_local! {
    static DIAGNOSTICS: std::cell::RefCell<Vec<String>> =
        const { std::cell::RefCell::new(Vec::new()) };
}

pub fn report_diagnostic(message: &str) {
    let _ = writeln!(io::stderr().lock(), "{message}");
    #[cfg(windows)]
    {
        use windows_sys::Win32::System::Diagnostics::Debug::OutputDebugStringW;

        let message: Vec<u16> = message
            .encode_utf16()
            .chain([u16::from(b'\n'), 0])
            .collect();
        // SAFETY: The buffer is live and NUL-terminated for the call.
        unsafe { OutputDebugStringW(message.as_ptr()) };
    }
    #[cfg(all(windows, not(test)))]
    report_to_event_log(message);
    #[cfg(test)]
    DIAGNOSTICS.with_borrow_mut(|diagnostics| diagnostics.push(message.to_owned()));
}

#[cfg(any(windows, test))]
fn reserve_event_log_report(reports: &AtomicUsize) -> bool {
    reports
        .try_update(Ordering::Relaxed, Ordering::Relaxed, |count| {
            (count < MAX_EVENT_LOG_REPORTS).then_some(count + 1)
        })
        .is_ok()
}

#[cfg(all(windows, not(test)))]
fn report_to_event_log(message: &str) {
    use std::ptr::{null, null_mut};
    use windows_sys::Win32::System::EventLog::{
        DeregisterEventSource, EVENTLOG_ERROR_TYPE, RegisterEventSourceW, ReportEventW,
    };

    static REPORTS: AtomicUsize = AtomicUsize::new(0);
    if !reserve_event_log_report(&REPORTS) {
        return;
    }

    let source: Vec<u16> = "VRCInventoryManager".encode_utf16().chain([0]).collect();
    // SAFETY: The server may be null and the source is a live NUL-terminated buffer.
    let handle = unsafe { RegisterEventSourceW(null(), source.as_ptr()) };
    if handle.is_null() {
        return;
    }

    let message: Vec<u16> = message.encode_utf16().chain([0]).collect();
    let strings = [message.as_ptr()];
    // SAFETY: The handle is open and the array contains one live NUL-terminated string.
    let _ = unsafe {
        ReportEventW(
            handle,
            EVENTLOG_ERROR_TYPE,
            0,
            0,
            null_mut(),
            1,
            0,
            strings.as_ptr(),
            null(),
        )
    };
    // SAFETY: The handle came from RegisterEventSourceW and has not been closed.
    let _ = unsafe { DeregisterEventSource(handle) };
}

#[derive(Debug)]
pub struct LogOpenError {
    path: PathBuf,
    source: io::Error,
}

impl LogOpenError {
    #[must_use]
    pub fn path(&self) -> &Path {
        &self.path
    }

    #[must_use]
    pub fn io_error(&self) -> &io::Error {
        &self.source
    }
}

impl fmt::Display for LogOpenError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            formatter,
            "Could not open the debug log at {}: {}",
            self.path().display(),
            self.io_error()
        )
    }
}

impl std::error::Error for LogOpenError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        Some(self.io_error())
    }
}

#[derive(Debug)]
pub struct StartupLogError {
    primary: Option<LogOpenError>,
    roaming: LogOpenError,
}

impl fmt::Display for StartupLogError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        if let Some(primary) = &self.primary {
            writeln!(formatter, "{primary}")?;
        }
        write!(formatter, "{}", self.roaming)
    }
}

impl std::error::Error for StartupLogError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        Some(&self.roaming)
    }
}

pub struct FileLog {
    path: PathBuf,
    fallback_path: Option<PathBuf>,
}

impl FileLog {
    pub fn near_executable(executable: &Path, roaming: &Path) -> io::Result<Self> {
        Self::near_executable_with_errors(Some(executable), roaming)
            .map(|(log, _)| log)
            .map_err(|error| error.roaming.source)
    }

    pub fn near_executable_with_errors(
        executable: Option<&Path>,
        roaming: &Path,
    ) -> Result<(Self, Option<LogOpenError>), StartupLogError> {
        let roaming_path = roaming.join(FILE_NAME);
        let mut primary = None;
        if let Some(parent) = executable
            .and_then(Path::parent)
            .filter(|&parent| parent != roaming)
        {
            match Self::open_with_fallback(parent.join(FILE_NAME), roaming_path.clone()) {
                Ok(log) => return Ok((log, None)),
                Err(error) => primary = Some(error),
            }
        }
        match Self::open_with_fallback(roaming_path, std::env::temp_dir().join(FILE_NAME)) {
            Ok(log) => Ok((log, primary)),
            Err(roaming) => Err(StartupLogError { primary, roaming }),
        }
    }

    pub fn open(path: PathBuf) -> io::Result<Self> {
        Self::open_with_fallback(path, std::env::temp_dir().join(FILE_NAME))
            .map_err(|error| error.source)
    }

    fn open_with_fallback(path: PathBuf, fallback_path: PathBuf) -> Result<Self, LogOpenError> {
        if let Err(source) = create_parent(&path).and_then(|()| open_log(&path)) {
            return Err(LogOpenError { path, source });
        }
        let fallback_path = Some(fallback_path).filter(|fallback| *fallback != path);
        Ok(Self {
            path,
            fallback_path,
        })
    }

    #[must_use]
    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn write(&self, level: Level, message: &str) -> io::Result<()> {
        append(&self.path, event_line(level, message).as_bytes())
    }
}

impl<S: Subscriber> Layer<S> for FileLog {
    fn on_event(&self, event: &Event<'_>, _context: Context<'_, S>) {
        let mut message = Message::default();
        event.record(&mut message);
        let line = event_line(*event.metadata().level(), &message.0);
        if let Err(error) = append(&self.path, line.as_bytes()) {
            let mut notice = format!(
                "Could not write to the debug log at {}: {error}",
                self.path.display()
            );
            if let Some(fallback_path) = &self.fallback_path {
                let mut fallback_lines = log_line(Level::WARN, &notice);
                fallback_lines.push_str(&line);
                if let Err(error) = create_parent(fallback_path)
                    .and_then(|()| append(fallback_path, fallback_lines.as_bytes()))
                {
                    let _ = write!(
                        notice,
                        "\nCould not write to the fallback debug log at {}: {error}",
                        fallback_path.display()
                    );
                }
            }
            report_diagnostic(&notice);
        }
    }
}

#[derive(Default)]
struct Message(String);

impl Visit for Message {
    fn record_str(&mut self, field: &Field, value: &str) {
        if field.name() == "message" {
            self.0.push_str(value);
        } else {
            let _ = write!(self.0, " {}={value}", field.name());
        }
    }

    fn record_debug(&mut self, field: &Field, value: &dyn fmt::Debug) {
        if field.name() == "message" {
            let _ = write!(self.0, "{value:?}");
        } else {
            let _ = write!(self.0, " {}={value:?}", field.name());
        }
    }
}

fn event_line(level: Level, message: &str) -> String {
    let message: String = message.chars().take(MAX_MESSAGE_CHARS).collect();
    log_line(level, &message)
}

fn log_line(level: Level, message: &str) -> String {
    let now = jiff::ZonedDateTime::now();
    let timestamp = now.strftime("%Y-%m-%dT%H:%M:%S%.f%:z");
    format!("{timestamp} [{level}] {}\n", escape(message))
}

fn create_parent(path: &Path) -> io::Result<()> {
    if let Some(parent) = path
        .parent()
        .filter(|parent| !parent.as_os_str().is_empty())
    {
        fs::create_dir_all(parent)?;
    }
    Ok(())
}

fn open_log(path: &Path) -> io::Result<File> {
    OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)
}

fn append(path: &Path, line: &[u8]) -> io::Result<()> {
    let mut file = open_log(path)?;
    file.lock()?;
    let size = file.seek(SeekFrom::End(0))?;
    let length = u64::try_from(line.len()).map_err(io::Error::other)?;
    if size.saturating_add(length) > MAX_LOG_BYTES {
        let mut backup = File::create(path.with_added_extension("1"))?;
        file.seek(SeekFrom::Start(size.saturating_sub(MAX_LOG_BYTES)))?;
        io::copy(&mut (&mut file).take(MAX_LOG_BYTES), &mut backup)?;
        file.set_len(0)?;
        file.rewind()?;
    }
    file.write_all(line)
}

fn escape(text: &str) -> String {
    text.replace('\r', "\\r").replace('\n', "\\n")
}

#[cfg(test)]
mod tests {
    use tracing_subscriber::layer::SubscriberExt as _;

    use super::*;

    #[test]
    fn retains_the_primary_open_error_when_the_roaming_log_opens() {
        DIAGNOSTICS.with_borrow_mut(Vec::clear);
        let directory = tempfile::tempdir().unwrap();
        let executable = directory.path().join("app.exe");
        let primary_path = directory.path().join(FILE_NAME);
        let roaming = directory.path().join("roaming");
        fs::create_dir(&primary_path).unwrap();
        let primary_error = open_log(&primary_path).unwrap_err();

        let (log, failure) =
            FileLog::near_executable_with_errors(Some(&executable), &roaming).unwrap();

        let failure = failure.unwrap();
        assert_eq!(log.path(), roaming.join(FILE_NAME));
        assert_eq!(failure.path(), primary_path);
        assert_eq!(failure.io_error().kind(), primary_error.kind());
        assert_eq!(
            failure.io_error().raw_os_error(),
            primary_error.raw_os_error()
        );
    }

    #[test]
    fn caps_event_log_reports_at_eight() {
        DIAGNOSTICS.with_borrow_mut(Vec::clear);
        let reports = AtomicUsize::new(0);
        for _ in 0..8 {
            assert!(reserve_event_log_report(&reports));
        }
        assert!(!reserve_event_log_report(&reports));
        assert!(!reserve_event_log_report(&reports));
    }

    #[test]
    fn records_the_write_failure_and_event_in_the_fallback_log() {
        DIAGNOSTICS.with_borrow_mut(Vec::clear);
        let directory = tempfile::tempdir().unwrap();
        let roaming = directory.path().join("roaming");
        let log = FileLog::near_executable(&directory.path().join("app.exe"), &roaming).unwrap();
        fs::remove_file(log.path()).unwrap();
        fs::create_dir(log.path()).unwrap();
        let error = open_log(log.path()).unwrap_err();
        let notice = format!(
            "Could not write to the debug log at {}: {error}",
            log.path().display()
        );

        tracing::subscriber::with_default(tracing_subscriber::registry().with(log), || {
            tracing::info!("Original event");
        });

        let contents = fs::read_to_string(roaming.join(FILE_NAME)).unwrap();
        let mut lines = contents.lines();
        assert!(lines.next().unwrap().ends_with(&format!("[WARN] {notice}")));
        assert!(lines.next().unwrap().ends_with("[INFO] Original event"));
        assert_eq!(lines.next(), None);
        assert_eq!(DIAGNOSTICS.with_borrow_mut(std::mem::take), [notice]);
    }

    #[test]
    fn reports_both_write_errors_when_the_fallback_log_also_fails() {
        DIAGNOSTICS.with_borrow_mut(Vec::clear);
        let directory = tempfile::tempdir().unwrap();
        let roaming = directory.path().join("roaming");
        let fallback_path = roaming.join(FILE_NAME);
        let log = FileLog::near_executable(&directory.path().join("app.exe"), &roaming).unwrap();
        fs::remove_file(log.path()).unwrap();
        fs::create_dir(log.path()).unwrap();
        fs::create_dir_all(&fallback_path).unwrap();
        let primary_error = open_log(log.path()).unwrap_err();
        let fallback_error = open_log(&fallback_path).unwrap_err();
        let notice = format!(
            "Could not write to the debug log at {}: {primary_error}\n\
             Could not write to the fallback debug log at {}: {fallback_error}",
            log.path().display(),
            fallback_path.display()
        );

        tracing::subscriber::with_default(tracing_subscriber::registry().with(log), || {
            tracing::info!("Original event");
        });

        assert_eq!(DIAGNOSTICS.with_borrow_mut(std::mem::take), [notice]);
    }

    #[cfg(windows)]
    #[test]
    fn reports_a_diagnostic_on_windows() {
        DIAGNOSTICS.with_borrow_mut(Vec::clear);
        report_diagnostic("Windows debug output test");
        assert_eq!(
            DIAGNOSTICS.with_borrow_mut(std::mem::take),
            ["Windows debug output test"]
        );
    }
}
