use std::{
    fs,
    path::{Path, PathBuf},
};

use base64::{Engine, engine::general_purpose::STANDARD};
use rusqlite::Connection;
use serde_json::{Value, json};
use tempfile::{TempDir, tempdir};
use tracing::Level;
use tracing_subscriber::layer::SubscriberExt as _;
use vrcim_core::{
    log::{FILE_NAME, FileLog, MAX_LOG_BYTES, MAX_MESSAGE_CHARS},
    paths::{UserPaths, read_vrcx_version},
    settings::{Settings, SettingsStore},
    vrcx_cookies::{load_session, parse_cookies},
};

#[test]
fn reads_and_writes_legacy_settings_keys_and_preserves_unknown_data() {
    let root = tempdir().unwrap();
    let path = root.path().join("settings.json");
    fs::write(
        &path,
        r#"{
            "LocalRoot": "folder",
            "UpdateCheck": false,
            "SkippedUpdate": "v2026.6.13.0",
            "Future": {"value": [1, true]}
        }"#,
    )
    .unwrap();
    let store = SettingsStore::new(path.clone());
    let mut settings = store.load();
    assert_eq!(settings.local_root, "folder");
    assert!(settings.include_subfolders);
    assert!(!settings.update_check);
    assert_eq!(settings.skipped_update.as_deref(), Some("v2026.6.13.0"));
    settings.update_check = true;
    settings.include_subfolders = false;
    store.save(&settings).unwrap();
    let written: Value = serde_json::from_slice(&fs::read(path).unwrap()).unwrap();
    assert_eq!(written["LocalRoot"], "folder");
    assert_eq!(written["IncludeSubfolders"], false);
    assert_eq!(written["UpdateCheck"], true);
    assert_eq!(written["SkippedUpdate"], "v2026.6.13.0");
    assert_eq!(written["Future"], json!({"value": [1, true]}));
    assert_eq!(store.load(), settings);
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 1);
}

#[test]
fn leaves_bad_settings_untouched_when_loading_defaults() {
    let root = tempdir().unwrap();
    let path = root.path().join("settings.json");
    fs::write(&path, "{").unwrap();
    assert_eq!(SettingsStore::new(path.clone()).load(), Settings::default());
    assert_eq!(fs::read_to_string(path).unwrap(), "{");
}

#[test]
fn reads_utf16_settings_and_preserves_unknown_keys_on_save() {
    let root = tempdir().unwrap();
    let path = root.path().join("settings.json");
    let json = r#"{"LocalRoot":"Photos","UpdateCheck":false,"Future":42}"#;
    let bytes = [0xfe, 0xff]
        .into_iter()
        .chain(json.encode_utf16().flat_map(u16::to_be_bytes))
        .collect::<Vec<_>>();
    fs::write(&path, bytes).unwrap();
    let store = SettingsStore::new(path.clone());
    let settings = store.load();
    assert_eq!(settings.local_root, "Photos");
    assert!(!settings.update_check);

    store.save(&Settings::default()).unwrap();
    let written: Value = serde_json::from_slice(&fs::read(path).unwrap()).unwrap();
    assert_eq!(written["Future"], 42);
}

#[test]
fn saving_fresh_settings_retains_unknown_keys_written_by_another_version() {
    let root = tempdir().unwrap();
    let path = root.path().join("settings.json");
    fs::write(
        &path,
        r#"{"LocalRoot":"old","UpdateCheck":"bad","Unrecognized":42}"#,
    )
    .unwrap();
    let store = SettingsStore::new(path.clone());
    assert_eq!(store.load(), Settings::default());
    store.save(&Settings::default()).unwrap();
    let value: Value = serde_json::from_slice(&fs::read(path).unwrap()).unwrap();
    assert_eq!(value["Unrecognized"], 42);
    assert_eq!(value["UpdateCheck"], true);
}

#[test]
fn creates_missing_settings_directory() {
    let root = tempdir().unwrap();
    let path = root.path().join("VRCInventoryManager/settings.json");
    let store = SettingsStore::new(path.clone());
    assert_eq!(store.load(), Settings::default());
    store.save(&Settings::default()).unwrap();
    assert!(path.is_file());
}

fn user_paths(root: &Path) -> UserPaths {
    let profile = root.join("User");
    UserPaths {
        pictures: root.join("Redirected Pictures"),
        app_data: profile.join("AppData/Roaming"),
        local_app_data: profile.join("AppData/Local"),
        local_low_app_data: root.join("Redirected LocalLow"),
        program_files: root.join("Program Files"),
        program_files_x86: Some(root.join("Program Files (x86)")),
        profile,
    }
}

fn write_file(path: &Path, bytes: impl AsRef<[u8]>) {
    fs::create_dir_all(path.parent().expect("the test file is written"))
        .expect("the test file is written");
    fs::write(path, bytes).expect("the test file is written");
}

#[test]
fn resolves_configured_photo_database_and_version_paths() {
    let root = tempdir().unwrap();
    let paths = user_paths(root.path());
    let vrchat_config = paths.local_low_app_data.join("VRChat/VRChat/config.json");
    write_file(
        &vrchat_config,
        r#"{"PICTURE_OUTPUT_FOLDER":"%userprofile%/Pictures/Custom"}"#,
    );
    write_file(
        &paths.app_data.join("VRCX/VRCX.json"),
        r#"{"vrcx_databaselocation":"%localappdata%/VRCX/Custom.db"}"#,
    );
    let version = paths.local_app_data.join("Programs/VRCX/Version");
    write_file(&version, "\u{feff} VRCX/3.0\n");
    let resolved = paths.resolve();
    let photos = paths.profile.join("Pictures/Custom");
    assert_eq!(resolved.vrchat_config, vrchat_config);
    assert_eq!(resolved.folders.photos, photos);
    assert_eq!(resolved.folders.emoji, photos.join("Emoji"));
    assert_eq!(resolved.folders.stickers, photos.join("Stickers"));
    assert_eq!(resolved.folders.prints, photos.join("Prints"));
    assert_eq!(
        resolved.database,
        paths.local_app_data.join("VRCX/Custom.db")
    );
    assert_eq!(resolved.version, version);
    assert_eq!(read_vrcx_version(&resolved.version), "VRCX/3.0");
}

#[test]
fn resolves_defaults_with_missing_or_corrupt_configs() {
    let root = tempdir().unwrap();
    let paths = user_paths(root.path());
    let defaults = paths.resolve();
    let photos = paths.pictures.join("VRChat");
    assert_eq!(defaults.folders.photos, photos);
    write_file(&defaults.vrcx_config, "{");
    write_file(&defaults.vrchat_config, "[]");
    let resolved = paths.resolve();
    assert_eq!(resolved.folders.photos, photos);
    assert_eq!(resolved.database, paths.app_data.join("VRCX/VRCX.sqlite3"));
    assert_eq!(resolved.version, paths.program_files.join("VRCX/Version"));
    assert_eq!(read_vrcx_version(&resolved.version), "VRCX");
}

#[test]
fn resolves_directory_overrides_and_orders_version_candidates() {
    let root = tempdir().unwrap();
    let paths = user_paths(root.path());
    let vrcx = paths.resolve().vrcx_config;
    let programs_x86 = paths.program_files_x86.as_ref().unwrap();
    for (variable, base) in [
        ("AppData", &paths.app_data),
        ("LocalAppData", &paths.local_app_data),
        ("UserProfile", &paths.profile),
        ("ProgramFiles", &paths.program_files),
        ("ProgramFiles(x86)", programs_x86),
    ] {
        let folder = base.join("Managed.db");
        fs::create_dir_all(&folder).unwrap();
        write_file(
            &vrcx,
            json!({"VRCX_DatabaseLocation": format!("%{variable}%/Managed.db")}).to_string(),
        );
        assert_eq!(paths.resolve().database, folder.join("VRCX.sqlite3"));
    }
    write_file(&programs_x86.join("VRCX/Version"), "x86");
    assert_eq!(read_vrcx_version(&paths.resolve().version), "x86");
    write_file(&paths.program_files.join("VRCX/Version"), "x64");
    assert_eq!(read_vrcx_version(&paths.resolve().version), "x64");
}

#[test]
fn parses_vrcx_array_cookies_and_rejects_unrelated_domains() {
    let cookies = parse_cookies(&STANDARD.encode(
        r#"[
        {"name":"auth","value":"ignored","domain":"evilvrchat.cloud"},
        {"name":"auth","value":"api_cookie","domain":".api.vrchat.cloud"},
        {"name":"auth","value":"ignored","domain":"vrchat.cloud.example"},
        {"NAME":"twofactorauth","VALUE":"factor","DOMAIN":"assets.vrchat.cloud"}
    ]"#,
    ))
    .unwrap();
    assert_eq!(cookies.header(), "auth=api_cookie; twoFactorAuth=factor");
}

#[test]
fn parses_vrcx_object_cookies_and_empty_domains() {
    let encoded = STANDARD.encode(r#"{"AUTH":"auth","twoFactorAuth":"factor","unrelated":false}"#);
    assert_eq!(
        parse_cookies(&encoded).unwrap().header(),
        "auth=auth; twoFactorAuth=factor"
    );
    let encoded = STANDARD.encode(
        r#"[{"name":"auth","value":"auth","domain":""},{"name":"twoFactorAuth","value":"factor"}]"#,
    );
    assert_eq!(
        parse_cookies(&encoded).unwrap().header(),
        "auth=auth; twoFactorAuth=factor"
    );
}

#[test]
fn rejects_missing_factors_and_keeps_credentials_out_of_errors() {
    let encoded = STANDARD.encode(r#"{"auth":"do_not_leak"}"#);
    let result = parse_cookies(&encoded).err().unwrap().to_string();
    assert_eq!(
        result,
        "VRCX cookie store did not contain usable VRChat auth cookies."
    );
    assert!(!result.contains("do_not_leak"));
}

#[test]
fn keeps_the_invalid_base64_error() {
    assert_eq!(
        parse_cookies("!@#").err().unwrap().to_string(),
        "VRCX cookie payload was not valid base64."
    );
}

struct Database {
    path: PathBuf,
    version: PathBuf,
    copies: PathBuf,
    connection: Connection,
    _root: TempDir,
}

impl Database {
    fn new() -> Self {
        let root = tempdir().expect("the test database is created");
        let path = root.path().join("VRCX.sqlite3");
        let version = root.path().join("Version");
        let copies = root.path().join("copies");
        fs::create_dir(&copies).expect("the test database is created");
        let connection = Connection::open(&path).expect("the test database is created");
        connection
            .execute_batch("CREATE TABLE cookies(key TEXT PRIMARY KEY, value TEXT)")
            .expect("the test database is created");
        Self {
            _root: root,
            path,
            version,
            copies,
            connection,
        }
    }

    fn write(&self, auth: &str) {
        let value = STANDARD.encode(json!({"auth": auth, "TwoFactorAuth": "factor"}).to_string());
        self.connection
            .execute(
                "INSERT OR REPLACE INTO cookies VALUES ('default', ?)",
                [value],
            )
            .expect("the test cookie is stored");
    }

    fn expect_session(&self, auth: &str, user_agent: &str) {
        let session = load_session(&self.path, &self.version, &self.copies)
            .expect("the stored session loads");
        assert_eq!(
            session.cookies.header(),
            format!("auth={auth}; twoFactorAuth=factor")
        );
        assert_eq!(session.user_agent, user_agent);
        assert_eq!(
            fs::read_dir(&self.copies)
                .expect("the stored session loads")
                .count(),
            0
        );
    }
}

#[test]
fn bundled_sqlite_meets_aggregate_fix_version() {
    assert!(
        rusqlite::version_number() >= 3_050_002,
        "{}",
        rusqlite::version()
    );
}

#[test]
fn loads_default_sqlite_row_and_vrcx_version() {
    let db = Database::new();
    db.write("auth_from_db");
    write_file(&db.version, "TestVRCX/1");
    db.expect_session("auth_from_db", "TestVRCX/1");
}

#[test]
fn reads_committed_wal_while_vrcx_has_it_open() {
    let db = Database::new();
    db.connection
        .pragma_update(None, "journal_mode", "WAL")
        .unwrap();
    db.write("auth_in_wal");
    db.expect_session("auth_in_wal", "VRCX");
}

#[test]
fn reads_committed_cookie_during_uncommitted_vrcx_write() {
    let db = Database::new();
    db.write("auth_committed");
    db.connection
        .execute_batch("BEGIN IMMEDIATE; UPDATE cookies SET value = 'not committed'")
        .unwrap();
    db.expect_session("auth_committed", "VRCX");
    db.connection.execute_batch("ROLLBACK").unwrap();
}

#[test]
fn copies_locked_database_and_removes_copy() {
    let db = Database::new();
    db.write("auth_from_copy");
    db.connection.execute_batch("BEGIN EXCLUSIVE").unwrap();
    db.expect_session("auth_from_copy", "VRCX");
    db.connection.execute_batch("ROLLBACK").unwrap();
}

#[test]
fn removes_copy_even_when_the_locked_database_has_no_default_row() {
    let db = Database::new();
    db.connection.execute_batch("BEGIN EXCLUSIVE").unwrap();
    assert!(load_session(&db.path, &db.version, &db.copies).is_err());
    assert_eq!(fs::read_dir(&db.copies).unwrap().count(), 0);
}

#[test]
fn appends_debug_log_next_to_executable_for_multiple_instances() {
    let root = tempdir().unwrap();
    let exe = root.path().join("app/VRCInventoryManager.exe");
    let fallback = root.path().join("fallback");
    let first = FileLog::near_executable(&exe, &fallback).unwrap();
    first.write(Level::INFO, "first").unwrap();
    let second = FileLog::near_executable(&exe, &fallback).unwrap();
    second.write(Level::WARN, "second").unwrap();
    first.write(Level::ERROR, "third").unwrap();
    let path = exe.with_file_name(FILE_NAME);
    assert_eq!(first.path(), path.as_path());
    let contents = fs::read_to_string(path).unwrap();
    let entries: Vec<_> = contents.lines().collect();
    assert_eq!(entries.len(), 3);
    for entry in &entries {
        assert!(
            entry
                .split_once(' ')
                .unwrap()
                .0
                .parse::<jiff::Timestamp>()
                .is_ok()
        );
    }
    assert!(entries[0].ends_with("[INFO] first"));
    assert!(entries[1].ends_with("[WARN] second"));
    assert!(entries[2].ends_with("[ERROR] third"));
}

#[test]
fn escapes_cr_and_lf_in_multiline_messages() {
    let root = tempdir().unwrap();
    let path = root.path().join(FILE_NAME);
    let log = FileLog::open(path.clone()).unwrap();
    log.write(Level::WARN, "one\rtwo\nthree\r\nfour").unwrap();
    let text = fs::read_to_string(path).unwrap();
    assert_eq!(text.lines().count(), 1);
    assert!(text.ends_with(" [WARN] one\\rtwo\\nthree\\r\\nfour\n"));
}

#[test]
fn caps_each_message_at_the_character_limit() {
    let root = tempdir().unwrap();
    let path = root.path().join(FILE_NAME);
    let log = FileLog::open(path.clone()).unwrap();
    let kept = "é".repeat(MAX_MESSAGE_CHARS);
    log.write(Level::INFO, &format!("{kept}dropped")).unwrap();
    let text = fs::read_to_string(path).unwrap();
    assert!(text.ends_with(&format!(" [INFO] {kept}\n")));
}

#[test]
fn writes_tracing_events_with_their_fields() {
    let root = tempdir().unwrap();
    let path = root.path().join(FILE_NAME);
    let log = FileLog::open(path.clone()).unwrap();
    let subscriber = tracing_subscriber::registry().with(log);
    tracing::subscriber::with_default(subscriber, || {
        tracing::warn!(
            status = 503,
            host = "api.vrchat.cloud",
            "VRChat request failed."
        );
    });
    let text = fs::read_to_string(path).unwrap();
    assert_eq!(text.lines().count(), 1);
    assert!(text.ends_with(" [WARN] VRChat request failed. status=503 host=api.vrchat.cloud\n"));
}

#[test]
fn reports_write_errors_to_the_caller() {
    let root = tempdir().unwrap();
    let path = root.path().join(FILE_NAME);
    let log = FileLog::open(path.clone()).unwrap();
    fs::remove_file(&path).unwrap();
    fs::create_dir(&path).unwrap();
    assert!(log.write(Level::ERROR, "lost").is_err());
}

#[test]
fn falls_back_for_unwritable_executable_folder_and_rotates_log() {
    let root = tempdir().unwrap();
    let blocked = root.path().join("file");
    fs::write(&blocked, "file").unwrap();
    let fallback = root.path().join("fallback");
    let expected = fallback.join(FILE_NAME);
    let log =
        FileLog::near_executable(&blocked.join("VRCInventoryManager.exe"), &fallback).unwrap();
    assert_eq!(log.path(), expected.as_path());
    fs::OpenOptions::new()
        .write(true)
        .open(&expected)
        .unwrap()
        .set_len(MAX_LOG_BYTES)
        .unwrap();
    log.write(Level::INFO, "over the limit").unwrap();
    let backup = expected.with_added_extension("1");
    assert_eq!(fs::metadata(&backup).unwrap().len(), MAX_LOG_BYTES);
    assert!(
        fs::read_to_string(&expected)
            .unwrap()
            .ends_with("[INFO] over the limit\n")
    );
    fs::OpenOptions::new()
        .write(true)
        .open(&expected)
        .unwrap()
        .set_len(MAX_LOG_BYTES + 10)
        .unwrap();
    log.write(Level::WARN, "new rotation").unwrap();
    assert_eq!(fs::metadata(&backup).unwrap().len(), MAX_LOG_BYTES);
    assert!(
        fs::read_to_string(&expected)
            .unwrap()
            .ends_with("[WARN] new rotation\n")
    );
}
