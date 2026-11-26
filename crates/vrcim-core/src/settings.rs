use std::{
    fs,
    io::{self, Write},
    path::{Path, PathBuf},
};

use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

use crate::paths::read_config_text;

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(default, rename_all = "PascalCase")]
pub struct Settings {
    pub local_root: String,
    pub include_subfolders: bool,
    pub update_check: bool,
    pub skipped_update: Option<String>,
    #[serde(flatten)]
    extra: Map<String, Value>,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            local_root: String::new(),
            include_subfolders: true,
            update_check: true,
            skipped_update: None,
            extra: Map::new(),
        }
    }
}

#[derive(Clone)]
pub struct SettingsStore {
    path: PathBuf,
}

impl SettingsStore {
    #[must_use]
    pub fn new(path: PathBuf) -> Self {
        Self { path }
    }

    #[must_use]
    pub fn load(&self) -> Settings {
        read_config_text(&self.path)
            .and_then(|text| serde_json::from_str(&text).ok())
            .unwrap_or_default()
    }

    pub fn save(&self, settings: &Settings) -> io::Result<()> {
        let parent = self
            .path
            .parent()
            .filter(|path| !path.as_os_str().is_empty())
            .unwrap_or(Path::new("."));
        fs::create_dir_all(parent)?;
        let mut settings = settings.clone();
        if let Some(text) = read_config_text(&self.path)
            && let Ok(mut fields) = serde_json::from_str::<Map<String, Value>>(&text)
        {
            for key in [
                "LocalRoot",
                "IncludeSubfolders",
                "UpdateCheck",
                "SkippedUpdate",
            ] {
                fields.remove(key);
            }
            settings.extra.extend(fields);
        }
        let mut temp = tempfile::NamedTempFile::new_in(parent)?;
        serde_json::to_writer_pretty(&mut temp, &settings)?;
        temp.write_all(b"\n")?;
        temp.as_file().sync_all()?;
        temp.persist(&self.path)?;
        Ok(())
    }
}
