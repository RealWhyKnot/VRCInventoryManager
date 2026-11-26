use std::{
    env, fs,
    path::{Path, PathBuf},
    sync::LazyLock,
};

use regex::{Captures, Regex};
use serde::Serialize;
use serde_json::Value;
#[cfg(windows)]
use windows_sys::{
    Win32::UI::Shell::{FOLDERID_LocalAppDataLow, FOLDERID_Pictures},
    core::GUID,
};

#[derive(Debug, Clone)]
pub struct UserPaths {
    pub profile: PathBuf,
    pub pictures: PathBuf,
    pub app_data: PathBuf,
    pub local_app_data: PathBuf,
    pub local_low_app_data: PathBuf,
    pub program_files: PathBuf,
    pub program_files_x86: Option<PathBuf>,
}

impl UserPaths {
    pub fn current() -> Self {
        let profile = environment_path("USERPROFILE")
            .or_else(env::home_dir)
            .unwrap_or_default();
        let pictures = profile.join("Pictures");
        let local_low_app_data = profile.join("AppData/LocalLow");
        #[cfg(windows)]
        let pictures = windows_known_folder(&FOLDERID_Pictures).unwrap_or(pictures);
        #[cfg(windows)]
        let local_low_app_data =
            windows_known_folder(&FOLDERID_LocalAppDataLow).unwrap_or(local_low_app_data);
        Self {
            pictures,
            app_data: environment_path("APPDATA")
                .unwrap_or_else(|| profile.join("AppData/Roaming")),
            local_app_data: environment_path("LOCALAPPDATA")
                .unwrap_or_else(|| profile.join("AppData/Local")),
            local_low_app_data,
            program_files: environment_path("ProgramFiles")
                .unwrap_or_else(|| PathBuf::from(r"C:\Program Files")),
            program_files_x86: environment_path("ProgramFiles(x86)"),
            profile,
        }
    }

    #[must_use]
    pub fn roaming(&self) -> PathBuf {
        self.app_data.join("VRCInventoryManager")
    }

    #[must_use]
    pub fn local(&self) -> PathBuf {
        self.local_app_data.join("VRCInventoryManager")
    }

    pub fn resolve(&self) -> KnownPaths {
        let vrchat_config = self.local_low_app_data.join("VRChat/VRChat/config.json");
        let vrcx_data = self.app_data.join("VRCX");
        let vrcx_config = vrcx_data.join("VRCX.json");
        let photos = json_string(&vrchat_config, "picture_output_folder").map_or_else(
            || self.pictures.join("VRChat"),
            |path| self.configured_path(&path),
        );
        let database = json_string(&vrcx_config, "VRCX_DatabaseLocation")
            .map(|path| self.configured_path(&path))
            .map_or_else(|| vrcx_data.join("VRCX.sqlite3"), database_path);
        let primary_version = self.program_files.join("VRCX/Version");
        let version = std::iter::once(primary_version.clone())
            .chain(
                self.program_files_x86
                    .iter()
                    .map(|path| path.join("VRCX/Version")),
            )
            .chain(std::iter::once(
                self.local_app_data.join("Programs/VRCX/Version"),
            ))
            .find(|path| path.is_file())
            .unwrap_or(primary_version);
        KnownPaths {
            folders: KnownFolders::from_photos(photos),
            database,
            version,
            vrcx_config,
            vrchat_config,
        }
    }

    fn configured_path(&self, path: &str) -> PathBuf {
        static VARIABLE: LazyLock<Regex> = LazyLock::new(|| {
            Regex::new(r"%([^%]+)%").expect("the environment variable pattern is valid")
        });
        let expanded = VARIABLE.replace_all(path.trim(), |caps: &Captures<'_>| {
            let path = match caps[1].to_ascii_lowercase().as_str() {
                "appdata" => Some(&self.app_data),
                "localappdata" => Some(&self.local_app_data),
                "userprofile" => Some(&self.profile),
                "programfiles" => Some(&self.program_files),
                "programfiles(x86)" => self.program_files_x86.as_ref(),
                _ => None,
            };
            path.map_or_else(
                || caps[0].to_owned(),
                |path| path.to_string_lossy().into_owned(),
            )
        });
        let path = PathBuf::from(expanded.as_ref());
        if !path.has_root() && expanded.contains('%') {
            return path;
        }
        std::path::absolute(&path).unwrap_or(path)
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct KnownFolders {
    pub photos: PathBuf,
    pub emoji: PathBuf,
    pub stickers: PathBuf,
    pub prints: PathBuf,
}

impl KnownFolders {
    #[must_use]
    pub fn from_photos(photos: PathBuf) -> Self {
        Self {
            emoji: photos.join("Emoji"),
            stickers: photos.join("Stickers"),
            prints: photos.join("Prints"),
            photos,
        }
    }
}

#[derive(Debug, Clone)]
pub struct KnownPaths {
    pub folders: KnownFolders,
    pub database: PathBuf,
    pub version: PathBuf,
    pub vrcx_config: PathBuf,
    pub vrchat_config: PathBuf,
}

fn environment_path(name: &str) -> Option<PathBuf> {
    env::var_os(name)
        .filter(|value| !value.is_empty())
        .map(PathBuf::from)
}

#[cfg(windows)]
fn windows_known_folder(folder: &GUID) -> Option<PathBuf> {
    use std::{ffi::OsString, os::windows::ffi::OsStringExt, ptr, slice};

    use windows_sys::{
        Win32::{
            System::Com::CoTaskMemFree,
            UI::Shell::{KF_FLAG_DONT_VERIFY, SHGetKnownFolderPath},
        },
        core::PWSTR,
    };

    struct FolderBuffer(PWSTR);

    impl Drop for FolderBuffer {
        fn drop(&mut self) {
            // SAFETY: The pointer is null or was returned by SHGetKnownFolderPath.
            unsafe { CoTaskMemFree(self.0.cast()) };
        }
    }

    let mut buffer = FolderBuffer(ptr::null_mut());
    // SAFETY: The folder ID and output pointer are valid; a null token means the current user.
    let result = unsafe {
        SHGetKnownFolderPath(
            folder,
            KF_FLAG_DONT_VERIFY.cast_unsigned(),
            ptr::null_mut(),
            &raw mut buffer.0,
        )
    };
    if result < 0 || buffer.0.is_null() {
        return None;
    }

    // SAFETY: The successful call returned a NUL-terminated UTF-16 buffer owned by `buffer`.
    let path = unsafe {
        let mut length = 0;
        while *buffer.0.add(length) != 0 {
            length += 1;
        }
        OsString::from_wide(slice::from_raw_parts(buffer.0, length))
    };
    (!path.is_empty()).then(|| PathBuf::from(path))
}

fn database_path(path: PathBuf) -> PathBuf {
    let nested = path.join("VRCX.sqlite3");
    let ext = path
        .extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or_default();
    if path.is_dir() || (!path.is_file() && (ext.is_empty() || nested.is_file())) {
        nested
    } else {
        path
    }
}

fn json_string(path: &Path, key: &str) -> Option<String> {
    let text = read_config_text(path)?;
    let value: Value = serde_json::from_str(&text).ok()?;
    value.as_object()?.iter().find_map(|(name, value)| {
        (name.eq_ignore_ascii_case(key))
            .then(|| value.as_str())
            .flatten()
            .filter(|value| !value.trim().is_empty())
            .map(str::to_owned)
    })
}

#[must_use]
pub fn read_vrcx_version(path: &Path) -> String {
    read_config_text(path)
        .map(|text| text.trim().to_owned())
        .filter(|text| !text.is_empty())
        .unwrap_or_else(|| "VRCX".into())
}

pub(crate) fn read_config_text(path: &Path) -> Option<String> {
    let bytes = fs::read(path).ok()?;
    let (text, decode_word): (_, fn([u8; 2]) -> u16) = match bytes.as_slice() {
        [0xff, 0xfe, text @ ..] => (text, u16::from_le_bytes),
        [0xfe, 0xff, text @ ..] => (text, u16::from_be_bytes),
        [0xef, 0xbb, 0xbf, text @ ..] => return std::str::from_utf8(text).ok().map(str::to_owned),
        _ => return String::from_utf8(bytes).ok(),
    };
    let (words, trailing_bytes) = text.as_chunks::<2>();
    if !trailing_bytes.is_empty() {
        return None;
    }
    char::decode_utf16(words.iter().copied().map(decode_word))
        .collect::<Result<String, _>>()
        .ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(windows)]
    #[test]
    fn resolves_windows_pictures_and_local_low() {
        let paths = UserPaths::current();
        let pictures = windows_known_folder(&FOLDERID_Pictures).unwrap();
        let local_low = windows_known_folder(&FOLDERID_LocalAppDataLow).unwrap();
        assert!(pictures.is_absolute());
        assert!(local_low.is_absolute());
        assert_eq!(paths.pictures, pictures);
        assert_eq!(paths.local_low_app_data, local_low);
    }

    #[test]
    fn reads_path_overrides_in_supported_encodings() {
        let root = tempfile::tempdir().unwrap();
        let config = root.path().join("config.json");
        let json = r#"{"picture_output_folder":"Photos 📷","VRCX_DatabaseLocation":"データ.db"}"#;
        let utf8_bom = [b"\xef\xbb\xbf".as_slice(), json.as_bytes()].concat();
        let utf16_little_endian = [0xff, 0xfe]
            .into_iter()
            .chain(json.encode_utf16().flat_map(u16::to_le_bytes))
            .collect::<Vec<_>>();
        let utf16_big_endian = [0xfe, 0xff]
            .into_iter()
            .chain(json.encode_utf16().flat_map(u16::to_be_bytes))
            .collect::<Vec<_>>();
        for (encoding, bytes) in [
            ("UTF-8", json.as_bytes()),
            ("UTF-8 BOM", &utf8_bom),
            ("UTF-16 LE BOM", &utf16_little_endian),
            ("UTF-16 BE BOM", &utf16_big_endian),
        ] {
            fs::write(&config, bytes).unwrap();
            assert_eq!(
                json_string(&config, "picture_output_folder").as_deref(),
                Some("Photos 📷"),
                "{encoding}"
            );
            assert_eq!(
                json_string(&config, "VRCX_DatabaseLocation").as_deref(),
                Some("データ.db"),
                "{encoding}"
            );
        }
    }

    #[test]
    fn reads_utf16_little_endian_vrcx_version_and_rejects_malformed_text() {
        let root = tempfile::tempdir().unwrap();
        let version = root.path().join("Version");
        let mut bytes = [0xff, 0xfe]
            .into_iter()
            .chain(" VRCX/3.0\n".encode_utf16().flat_map(u16::to_le_bytes))
            .collect::<Vec<_>>();
        fs::write(&version, &bytes).unwrap();
        assert_eq!(read_vrcx_version(&version), "VRCX/3.0");

        bytes.push(0);
        fs::write(&version, &bytes).unwrap();
        assert_eq!(read_vrcx_version(&version), "VRCX");

        fs::write(&version, [0xfe, 0xff, 0xd8, 0x00]).unwrap();
        assert_eq!(read_vrcx_version(&version), "VRCX");
    }
}
