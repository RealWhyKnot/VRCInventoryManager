use std::{
    env, fmt,
    fs::{self, File, OpenOptions},
    io::{self, Write},
    path::{Path, PathBuf},
    process::Command,
    time::Duration,
};

use anyhow::{Context, Result, bail, ensure};
use reqwest::Client;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tokio::io::AsyncWriteExt;

pub const RELEASES_URL: &str =
    "https://api.github.com/repos/RealWhyKnot/VRCInventoryManager/releases?per_page=20";
const EXE_NAME: &str = "VRCInventoryManager.exe";

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum Channel {
    Dev,
    Beta,
    Stable,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub struct ReleaseVersion {
    numbers: [u32; 4],
    pub channel: Channel,
}

impl ReleaseVersion {
    #[must_use]
    pub fn parse(text: &str) -> Option<Self> {
        let tag = bare(
            text.trim()
                .split_once('+')
                .map_or(text.trim(), |(version, _)| version),
        );
        let (numeric, suffix) = tag.split_once('-').unwrap_or((tag, ""));
        let parts: [&str; 4] = numeric.split('.').collect::<Vec<_>>().try_into().ok()?;
        let mut numbers = [0; 4];
        for (index, part) in parts.into_iter().enumerate() {
            if !part.bytes().all(|byte| byte.is_ascii_digit()) {
                return None;
            }
            numbers[index] = u32::try_from(part.parse::<i32>().ok()?).ok()?;
        }
        let channel = if suffix.is_empty() {
            Channel::Stable
        } else if suffix.eq_ignore_ascii_case("beta") {
            Channel::Beta
        } else {
            Channel::Dev
        };
        Some(Self { numbers, channel })
    }
}

impl fmt::Display for ReleaseVersion {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        let [year, month, day, revision] = self.numbers;
        let suffix = if self.channel == Channel::Beta {
            "-beta"
        } else {
            ""
        };
        write!(formatter, "{year}.{month}.{day}.{revision}{suffix}")
    }
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default)]
pub struct Release {
    #[serde(rename = "tag_name")]
    pub tag: String,
    pub html_url: String,
    pub prerelease: bool,
    pub draft: bool,
    pub assets: Vec<ReleaseAsset>,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(default)]
pub struct ReleaseAsset {
    pub name: String,
    pub browser_download_url: String,
}

#[must_use]
pub fn select_release(releases: &[Release], current: ReleaseVersion) -> Option<&Release> {
    if current.channel == Channel::Dev {
        return None;
    }
    let mut best = None;
    let mut newest = current;
    for release in releases {
        if release.draft || (current.channel == Channel::Stable && release.prerelease) {
            continue;
        }
        if let Some(version) = ReleaseVersion::parse(&release.tag)
            && version.channel != Channel::Dev
            && version > newest
        {
            newest = version;
            best = Some(release);
        }
    }
    best
}

fn bare(tag: &str) -> &str {
    tag.strip_prefix(['v', 'V']).unwrap_or(tag)
}

#[must_use]
pub fn setup_name(tag: &str) -> String {
    format!("VRCInventoryManager-v{}-Setup.exe", bare(tag))
}

#[must_use]
pub fn zip_name(tag: &str) -> String {
    format!("VRCInventoryManager-v{}-win-x64.zip", bare(tag))
}

#[must_use]
pub fn integrity_name(tag: &str) -> String {
    format!("VRCInventoryManager-v{}.integrity.tsv", bare(tag))
}

pub fn parse_integrity(tsv: &str, name: &str) -> Result<(String, u64)> {
    for line in tsv.trim_start_matches('\u{feff}').lines() {
        let mut fields = line.split('\t');
        let (Some(asset), Some(hash), Some(size), None) =
            (fields.next(), fields.next(), fields.next(), fields.next())
        else {
            continue;
        };
        if asset != name {
            continue;
        }
        let hash = hash.trim();
        ensure!(
            hash.len() == 64 && hash.bytes().all(|byte| byte.is_ascii_hexdigit()),
            "The checksum for {name} is not 64 hex characters."
        );
        let bytes = size
            .parse::<i64>()
            .ok()
            .filter(|&bytes| bytes > 0 && size.bytes().all(|byte| byte.is_ascii_digit()))
            .and_then(|bytes| u64::try_from(bytes).ok())
            .context(format!("The size for {name} is not a positive number."))?;
        return Ok((hash.to_ascii_lowercase(), bytes));
    }
    bail!("The checksum file has no row for {name}.")
}

pub fn verify(
    actual_hash: &str,
    actual_bytes: u64,
    expected_hash: &str,
    expected_bytes: u64,
    name: &str,
) -> Result<()> {
    ensure!(
        actual_bytes == expected_bytes,
        "{name} is {actual_bytes} bytes, expected {expected_bytes}."
    );
    ensure!(
        actual_hash.eq_ignore_ascii_case(expected_hash),
        "{name} does not match its published checksum."
    );
    Ok(())
}

#[derive(Clone)]
pub struct Updater {
    executable: PathBuf,
    data: PathBuf,
    version: String,
    http: Client,
}

impl Updater {
    pub fn new(executable: PathBuf, data: PathBuf, version: &str) -> Result<Self> {
        let http = Client::builder()
            .timeout(Duration::from_secs(300))
            .user_agent(format!(
                "VRCInventoryManager/{}",
                version.split('+').next().unwrap_or(version)
            ))
            .build()?;
        Ok(Self {
            executable,
            data,
            version: version.into(),
            http,
        })
    }

    #[must_use]
    pub fn is_installed(&self) -> bool {
        self.executable.with_file_name("Uninstall.exe").is_file()
    }

    fn asset_name(&self, tag: &str) -> String {
        if self.is_installed() {
            setup_name(tag)
        } else {
            zip_name(tag)
        }
    }

    pub async fn find(&self, skipped: Option<&str>) -> Result<Option<Release>> {
        let Some(version) =
            ReleaseVersion::parse(&self.version).filter(|version| version.channel != Channel::Dev)
        else {
            return Ok(None);
        };
        let url = env::var("VRCINVENTORYMANAGER_RELEASES_URL")
            .ok()
            .filter(|url| !url.is_empty())
            .unwrap_or_else(|| RELEASES_URL.into());
        let releases: Vec<Release> = self
            .http
            .get(url)
            .timeout(Duration::from_secs(30))
            .header("Accept", "application/vnd.github+json")
            .send()
            .await?
            .error_for_status()?
            .json()
            .await?;
        let offer = select_release(&releases, version).filter(|release| {
            Some(release.tag.as_str()) != skipped && self.assets(release).is_some()
        });
        Ok(offer.cloned())
    }

    fn assets<'a>(&self, release: &'a Release) -> Option<(&'a ReleaseAsset, &'a ReleaseAsset)> {
        if release.tag.contains(['/', '\\', ':']) {
            return None;
        }
        let find = |name: &str| release.assets.iter().find(|asset| asset.name == name);
        Some((
            find(&self.asset_name(&release.tag))?,
            find(&integrity_name(&release.tag))?,
        ))
    }

    fn lock(&self) -> Result<File> {
        fs::create_dir_all(&self.data)?;
        let lock = OpenOptions::new()
            .create(true)
            .truncate(false)
            .write(true)
            .open(self.data.join("update.lock"))?;
        lock.try_lock()?;
        Ok(lock)
    }

    pub async fn clean_up(&self) {
        for _ in 0..20 {
            if self.clean_up_once().is_ok() {
                return;
            }
            tokio::time::sleep(Duration::from_millis(500)).await;
        }
        tracing::warn!("Could not clean up the files left by the last update.");
    }

    fn clean_up_once(&self) -> Result<()> {
        let _lock = self.lock()?;
        if self.clean_staging()? {
            return Ok(());
        }
        let mut old = self.executable.as_os_str().to_owned();
        old.push(".old");
        ignore_missing(fs::remove_file(PathBuf::from(old)))?;
        ignore_missing(fs::remove_dir_all(self.data.join("update")))?;
        Ok(())
    }

    fn clean_staging(&self) -> io::Result<bool> {
        let entries = match fs::read_dir(self.data.join("update")) {
            Ok(entries) => entries,
            Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(false),
            Err(error) => return Err(error),
        };
        let mut active = false;
        for entry in entries {
            let entry = entry?;
            if !entry.file_type()?.is_dir()
                || !entry.file_name().to_string_lossy().starts_with("apply-")
            {
                continue;
            }
            let stage = entry.path();
            if helper_is_running(&stage.join("lease"))? {
                active = true;
            } else {
                ignore_missing(fs::remove_dir_all(stage))?;
            }
        }
        Ok(active)
    }

    pub async fn install(&self, release: &Release, progress: impl Fn(f64)) -> Result<()> {
        let _lock = self.lock()?;
        ensure!(
            !self.clean_staging()?,
            "An update is already being installed."
        );
        let stage_root = self.data.join("update");
        fs::create_dir_all(&stage_root)?;
        let staged = tempfile::Builder::new()
            .prefix("apply-")
            .tempdir_in(stage_root)?;
        let downloaded = self.download(release, staged.path(), progress).await?;
        self.apply(&downloaded, staged.path()).await?;
        tracing::info!(tag = %release.tag, "Started the update installer.");
        let _ = staged.keep();
        Ok(())
    }

    pub async fn download(
        &self,
        release: &Release,
        staging: &Path,
        progress: impl Fn(f64),
    ) -> Result<PathBuf> {
        let (asset, integrity) = self
            .assets(release)
            .context("This release is missing update files.")?;
        let content = self
            .http
            .get(&integrity.browser_download_url)
            .timeout(Duration::from_secs(30))
            .send()
            .await?
            .error_for_status()?
            .text()
            .await?;
        let (hash, expected) = parse_integrity(&content, &asset.name)?;
        let mut response = self
            .http
            .get(&asset.browser_download_url)
            .send()
            .await?
            .error_for_status()?;
        let temp = tempfile::NamedTempFile::new_in(staging)?;
        let mut target = tokio::fs::File::from_std(temp.reopen()?);
        let mut hasher = Sha256::new();
        let mut total = 0;
        let mut last_percent = None;
        while let Some(chunk) = response.chunk().await? {
            total += u64::try_from(chunk.len())?;
            ensure!(
                total <= expected,
                "{} is larger than the published size.",
                asset.name
            );
            target.write_all(&chunk).await?;
            hasher.update(&chunk);
            let percent = u32::try_from(u128::from(total) * 100 / u128::from(expected))?;
            if last_percent != Some(percent) {
                last_percent = Some(percent);
                progress(f64::from(percent) / 100.0);
            }
        }
        target.flush().await?;
        target.sync_all().await?;
        drop(target);
        verify(
            &hex::encode(hasher.finalize()),
            total,
            &hash,
            expected,
            &asset.name,
        )?;
        let path = staging.join(&asset.name);
        temp.persist(&path)?;
        Ok(path)
    }

    async fn apply(&self, downloaded: &Path, staging: &Path) -> Result<()> {
        let install_dir = self
            .executable
            .parent()
            .context("Couldn't locate the app folder.")?;
        let log = self.data.join("update.log");
        let pid = std::process::id();
        let script = if self.is_installed() {
            setup_script(
                pid,
                downloaded,
                staging,
                install_dir,
                &self.executable,
                &log,
            )
        } else {
            let fresh = staging.join(EXE_NAME);
            let mut zip = zip::ZipArchive::new(File::open(downloaded)?)?;
            let mut entry = zip
                .by_name(EXE_NAME)
                .context("VRCInventoryManager.exe is missing from the update archive.")?;
            let mut target = File::create(&fresh)?;
            io::copy(&mut entry, &mut target)?;
            target.sync_all()?;
            update_script(
                pid,
                downloaded,
                staging,
                install_dir,
                &self.executable,
                &log,
                Some(&fresh),
            )
        };
        let script_path = staging.join("apply.ps1");
        let mut file = File::create(&script_path)?;
        file.write_all("\u{feff}".as_bytes())?;
        file.write_all(script.as_bytes())?;
        drop(file);
        let mut helper = hidden_command("powershell.exe")
            .current_dir(env::temp_dir())
            .args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-WindowStyle",
                "Hidden",
                "-File",
            ])
            .arg(script_path)
            .spawn()?;
        let ready = staging.join("ready");
        let started = tokio::time::timeout(Duration::from_secs(10), async {
            while !ready.is_file() {
                ensure!(
                    helper.try_wait()?.is_none(),
                    "The updater closed before it was ready."
                );
                tokio::time::sleep(Duration::from_millis(50)).await;
            }
            Ok::<_, anyhow::Error>(())
        })
        .await;
        if let Ok(result) = started {
            result?;
        } else {
            let _ = helper.kill();
            let _ = helper.wait();
            bail!("The updater did not start. Try again.");
        }
        Ok(())
    }
}

#[cfg(windows)]
fn helper_is_running(lease: &Path) -> io::Result<bool> {
    use std::os::windows::fs::OpenOptionsExt;

    match OpenOptions::new().write(true).share_mode(0).open(lease) {
        Ok(_) => Ok(false),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(false),
        Err(error) if error.raw_os_error() == Some(32) => Ok(true),
        Err(error) => Err(error),
    }
}

#[cfg(not(windows))]
fn helper_is_running(_: &Path) -> io::Result<bool> {
    Ok(false)
}

fn ignore_missing(result: io::Result<()>) -> io::Result<()> {
    match result {
        Err(err) if err.kind() == io::ErrorKind::NotFound => Ok(()),
        result => result,
    }
}

#[must_use]
pub fn hidden_command(program: &str) -> Command {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        let mut command = Command::new(program);
        command.creation_flags(CREATE_NO_WINDOW);
        command
    }
    #[cfg(not(windows))]
    Command::new(program)
}

fn quote(path: &str) -> String {
    format!(
        "'{}'",
        path.replace('\'', "''")
            .replace('\u{2018}', "\u{2018}\u{2018}")
            .replace('\u{2019}', "\u{2019}\u{2019}")
    )
}

#[must_use]
pub fn setup_script(
    pid: u32,
    setup: &Path,
    staging: &Path,
    install_dir: &Path,
    exe: &Path,
    log: &Path,
) -> String {
    update_script(pid, setup, staging, install_dir, exe, log, None)
}

fn update_script(
    pid: u32,
    setup: &Path,
    staging: &Path,
    install_dir: &Path,
    exe: &Path,
    log: &Path,
    fresh: Option<&Path>,
) -> String {
    let replacements = [
        ("[int] $ProcessId = 0", format!("[int] $ProcessId = {pid}")),
        ("'__SETUP_PATH__'", quote(&setup.to_string_lossy())),
        (
            "'__SETUP_ARGUMENTS__'",
            quote(&format!("/S /D={}", install_dir.display())),
        ),
        ("'__STAGING_DIRECTORY__'", quote(&staging.to_string_lossy())),
        (
            "'__INSTALL_DIRECTORY__'",
            quote(&install_dir.to_string_lossy()),
        ),
        ("'__EXECUTABLE_PATH__'", quote(&exe.to_string_lossy())),
        ("'__LOG_PATH__'", quote(&log.to_string_lossy())),
        (
            "'__FRESH_PATH__'",
            quote(&fresh.map_or_else(String::new, |path| path.to_string_lossy().into_owned())),
        ),
    ];
    include_str!("apply_setup.ps1")
        .lines()
        .map(|line| {
            replacements
                .iter()
                .find(|(token, _)| line.contains(token))
                .map_or_else(
                    || line.to_owned(),
                    |(token, value)| line.replace(token, value),
                )
        })
        .collect::<Vec<_>>()
        .join("\n")
}
