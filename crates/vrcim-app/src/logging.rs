use std::path::Path;

use tracing::level_filters::LevelFilter;
use tracing_subscriber::{Layer as _, layer::SubscriberExt as _};
use vrcim_core::{
    log::{FILE_NAME, FileLog, report_diagnostic},
    updater::{Channel, ReleaseVersion},
};

use crate::VERSION;

pub(crate) fn install(executable: Option<&Path>, roaming: &Path) {
    let (log, notice) = match FileLog::near_executable_with_errors(executable, roaming) {
        Ok((log, primary)) => (
            log,
            primary.map(|error| format!("{error}\nUsing the roaming debug log.")),
        ),
        Err(error) => {
            let temp_path = std::env::temp_dir().join(FILE_NAME);
            match FileLog::open(temp_path.clone()) {
                Ok(log) => (
                    log,
                    Some(format!("{error}\nUsing the temporary debug log.")),
                ),
                Err(temp_error) => {
                    report_diagnostic(&format!(
                        "{error}\n\
                         Could not open the temporary debug log at {}: {temp_error}",
                        temp_path.display()
                    ));
                    return;
                }
            }
        }
    };
    let level = if is_verbose() {
        LevelFilter::DEBUG
    } else {
        LevelFilter::INFO
    };
    let subscriber = tracing_subscriber::registry().with(log.with_filter(level));
    if let Err(error) = tracing::subscriber::set_global_default(subscriber) {
        report_diagnostic(&format!("Could not start the debug log: {error}"));
    } else if let Some(notice) = notice {
        tracing::warn!("{notice}");
    }
}

fn is_verbose() -> bool {
    std::env::var_os("VRCIM_DEBUG").is_some_and(|value| value == "1")
        || ReleaseVersion::parse(VERSION).is_some_and(|version| version.channel == Channel::Beta)
}
