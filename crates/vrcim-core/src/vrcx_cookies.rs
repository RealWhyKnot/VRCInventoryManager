use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
    time::Duration,
};

use anyhow::{Context, Result, bail};
use base64::{Engine, engine::general_purpose::STANDARD};
use rusqlite::{Connection, Error, ErrorCode, OpenFlags, types::ValueRef};
use serde_json::Value;

use crate::paths::read_vrcx_version;

#[derive(Clone, PartialEq, Eq)]
pub struct Cookies {
    pub auth: String,
    pub two_factor_auth: String,
}

impl Cookies {
    #[must_use]
    pub fn header(&self) -> String {
        format!("auth={}; twoFactorAuth={}", self.auth, self.two_factor_auth)
    }
}

pub struct Session {
    pub cookies: Cookies,
    pub user_agent: String,
}

pub fn load_session(database: &Path, version: &Path, temp_dir: &Path) -> Result<Session> {
    if !database.is_file() {
        bail!("Sign in to VRChat in VRCX, then reconnect.");
    }
    let encoded = match read_database(database, OpenFlags::SQLITE_OPEN_READ_ONLY) {
        Err(Error::SqliteFailure(err, _))
            if matches!(
                err.code,
                ErrorCode::DatabaseBusy
                    | ErrorCode::DatabaseLocked
                    | ErrorCode::ReadOnly
                    | ErrorCode::CannotOpen
            ) =>
        {
            read_copy(database, temp_dir)?
        }
        result => result?,
    };
    Ok(Session {
        cookies: parse_cookies(&encoded)?,
        user_agent: read_vrcx_version(version),
    })
}

pub fn parse_cookies(encoded: &str) -> Result<Cookies> {
    let compact: String = encoded
        .chars()
        .filter(|character| !character.is_ascii_whitespace())
        .collect();
    let decoded = STANDARD
        .decode(compact)
        .context("VRCX cookie payload was not valid base64.")?;
    let value: Value = serde_json::from_slice(&decoded).context("Couldn't read VRCX cookies.")?;
    let mut cookies = HashMap::new();
    match &value {
        Value::Array(items) => {
            for item in items {
                let domain = property(item, "domain")
                    .unwrap_or_default()
                    .trim()
                    .trim_start_matches('.')
                    .to_ascii_lowercase();
                if !(domain.is_empty()
                    || domain == "vrchat.cloud"
                    || domain.ends_with(".vrchat.cloud"))
                {
                    continue;
                }
                if let (Some(name), Some(value)) = (property(item, "name"), property(item, "value"))
                {
                    cookies.insert(name.to_ascii_lowercase(), value);
                }
            }
        }
        Value::Object(items) => {
            for (name, value) in items {
                if let Some(value) = value.as_str() {
                    cookies.insert(name.to_ascii_lowercase(), value);
                }
            }
        }
        _ => {}
    }
    let required = |key| {
        cookies
            .get(key)
            .copied()
            .filter(|value| !value.trim().is_empty() && !value.contains([';', '\r', '\n']))
            .map(str::to_owned)
            .context("VRCX cookie store did not contain usable VRChat auth cookies.")
    };
    Ok(Cookies {
        auth: required("auth")?,
        two_factor_auth: required("twofactorauth")?,
    })
}

fn property<'a>(item: &'a Value, name: &str) -> Option<&'a str> {
    item.as_object()?
        .iter()
        .find(|(key, _)| key.eq_ignore_ascii_case(name))?
        .1
        .as_str()
}

fn read_database(path: &Path, flags: OpenFlags) -> Result<String, Error> {
    let connection = Connection::open_with_flags(path, flags)?;
    connection.busy_timeout(Duration::from_millis(250))?;
    connection.query_row(
        "SELECT value FROM cookies WHERE key = 'default' LIMIT 1",
        [],
        |row| match row.get_ref(0)? {
            ValueRef::Blob(bytes) => String::from_utf8(bytes.to_vec()).map_err(|err| {
                Error::FromSqlConversionFailure(0, rusqlite::types::Type::Blob, Box::new(err))
            }),
            _ => row.get(0),
        },
    )
}

fn sidecar(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.as_os_str().to_owned();
    name.push(suffix);
    PathBuf::from(name)
}

fn read_copy(database: &Path, temp_dir: &Path) -> Result<String> {
    let directory = tempfile::Builder::new()
        .prefix("vrcx-cookies-")
        .tempdir_in(temp_dir)?;
    let copy = directory.path().join("cookies.sqlite3");
    fs::copy(database, &copy)?;
    for suffix in ["-journal", "-wal"] {
        match fs::copy(sidecar(database, suffix), sidecar(&copy, suffix)) {
            Ok(_) => {}
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
            Err(err) => return Err(err.into()),
        }
    }
    Ok(read_database(&copy, OpenFlags::SQLITE_OPEN_READ_WRITE)?)
}
