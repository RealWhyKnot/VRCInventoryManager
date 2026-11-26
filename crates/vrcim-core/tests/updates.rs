use std::{fs, path::Path};

use axum::{Router, routing::get};
use sha2::{Digest, Sha256};
use tempfile::tempdir;
use vrcim_core::updater::{
    Channel, Release, ReleaseAsset, ReleaseVersion, Updater, integrity_name, parse_integrity,
    select_release, setup_name, setup_script, verify, zip_name,
};

enum Published {
    Stable,
    Prerelease,
    Draft,
}

fn release(tag: &str, published: &Published) -> Release {
    Release {
        tag: tag.into(),
        prerelease: matches!(published, Published::Prerelease),
        draft: matches!(published, Published::Draft),
        ..Release::default()
    }
}

#[test]
fn parses_existing_version_channels_and_build_metadata() {
    for (tag, channel) in [
        ("v2026.10.8.0", Channel::Stable),
        ("V2026.10.8.0+5b7e9d9", Channel::Stable),
        ("v2026.10.8.1-beta", Channel::Beta),
        ("2026.10.8.3-AB12", Channel::Dev),
    ] {
        assert_eq!(ReleaseVersion::parse(tag).unwrap().channel, channel);
    }
    for bad in ["", "0.0.0", "v2026.10.8", "2026.10.8.x", "2026.+10.8.0"] {
        assert!(ReleaseVersion::parse(bad).is_none(), "{bad}");
    }
    assert!(ReleaseVersion::parse("2026.10.9.0") > ReleaseVersion::parse("2026.10.8.5"));
    assert!(ReleaseVersion::parse("2026.10.8.0") > ReleaseVersion::parse("2026.10.8.0-beta"));
}

#[test]
fn selects_newer_stable_and_beta_releases_skipping_drafts_and_dev() {
    let releases = [
        release("v2026.10.9.0-beta", &Published::Prerelease),
        release("v2026.10.10.0", &Published::Draft),
        release("v2026.10.8.1", &Published::Stable),
        release("v2026.6.18.0", &Published::Stable),
        release("not-a-version", &Published::Stable),
        release("v2026.11.1.0-DEV", &Published::Stable),
    ];
    let find = |current| {
        select_release(&releases, ReleaseVersion::parse(current).unwrap())
            .map(|release| release.tag.as_str())
    };
    assert_eq!(find("2026.6.18.0"), Some("v2026.10.8.1"));
    assert_eq!(find("2026.10.8.1-beta"), Some("v2026.10.9.0-beta"));
    assert_eq!(find("2026.10.8.1"), None);
    assert_eq!(find("2026.6.18.0-AB12"), None);
}

#[test]
fn preserves_published_release_json_flag_and_asset_names() {
    let release: Release = serde_json::from_str(
        r#"{
            "tag_name": "v2026.6.18.0",
            "draft": true,
            "prerelease": true,
            "html_url": "https://github.com/release",
            "assets": [{"name": "app", "browser_download_url": "https://github.com/asset"}]
        }"#,
    )
    .unwrap();
    assert!(release.draft && release.prerelease);
    let output = serde_json::to_value(&release).unwrap();
    assert_eq!(output["tag_name"], release.tag);
    assert_eq!(output["draft"], true);
    assert_eq!(output["prerelease"], true);
    assert_eq!(
        output["assets"][0]["browser_download_url"],
        "https://github.com/asset"
    );
    assert_eq!(
        setup_name(&release.tag),
        "VRCInventoryManager-v2026.6.18.0-Setup.exe"
    );
    assert_eq!(
        zip_name(&release.tag),
        "VRCInventoryManager-v2026.6.18.0-win-x64.zip"
    );
    assert_eq!(
        integrity_name(&release.tag),
        "VRCInventoryManager-v2026.6.18.0.integrity.tsv"
    );
}

#[test]
fn parses_published_integrity_manifest_and_rejects_bad_entries() {
    let name = "VRCInventoryManager-v2026.6.18.0-Setup.exe";
    let hash = "fddbb4de15dd180e37c2c8c36880d9f6a666f5744195bd612937fb1af0107052";
    let tsv = format!(
        "\u{feff}name\tsha256\tbytes\r\n{name}\t{}\t52480148\r\n",
        hash.to_ascii_uppercase()
    );
    assert_eq!(
        parse_integrity(&tsv, name).unwrap(),
        (hash.into(), 52_480_148)
    );
    assert!(parse_integrity(&tsv, "missing.exe").is_err());
    assert!(parse_integrity("a.exe\tabc\t10\n", "a.exe").is_err());
    for size in ["0", "-10", "+10", " 10", "9223372036854775808"] {
        assert!(
            parse_integrity(&format!("a.exe\t{hash}\t{size}"), "a.exe").is_err(),
            "{size}"
        );
    }
}

#[test]
fn verifies_both_byte_size_and_sha256() {
    assert!(verify("abc", 10, "ABC", 10, "app.exe").is_ok());
    let size = verify("abc", 9, "abc", 10, "app.exe").unwrap_err();
    assert_eq!(size.to_string(), "app.exe is 9 bytes, expected 10.");
    let sha = verify("aba", 10, "abc", 10, "app.exe").unwrap_err();
    assert_eq!(
        sha.to_string(),
        "app.exe does not match its published checksum."
    );
}

#[tokio::test]
async fn detects_installed_copy_and_cleans_startup_staging() {
    let root = tempdir().unwrap();
    let executable = root.path().join("VRCInventoryManager.exe");
    let data = root.path().join("data");
    let updater = Updater::new(executable.clone(), data.clone(), "2026.1.1.0").unwrap();
    assert!(!updater.is_installed());
    fs::write(executable.with_file_name("Uninstall.exe"), []).unwrap();
    assert!(updater.is_installed());
    let old = executable.with_file_name("VRCInventoryManager.exe.old");
    fs::write(&old, []).unwrap();
    fs::create_dir_all(data.join("update/old")).unwrap();
    fs::write(data.join("update/old/apply.ps1"), []).unwrap();
    updater.clean_up().await;
    assert!(!old.exists());
    assert!(!data.join("update").exists());
    assert!(executable.with_file_name("Uninstall.exe").exists());
}

#[tokio::test]
async fn streams_update_with_progress_and_keeps_only_verified_files() {
    let stage = tempdir().unwrap();
    let tag = "v2026.9.9.0";
    let name = zip_name(tag);
    let bytes = b"test archive";
    let digest = hex::encode(Sha256::digest(bytes));
    let good = format!("{name}\t{digest}\t{}\n", bytes.len());
    let bad = format!("{name}\t{}\t{}\n", "f".repeat(64), bytes.len());
    let router = Router::new()
        .route("/verified", get(move || async move { good }))
        .route("/bad", get(move || async move { bad }))
        .route("/download", get(|| async { bytes.as_slice() }));
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let server = tokio::spawn(async move { axum::serve(listener, router).await });
    let updater = Updater::new(
        stage.path().join("VRCInventoryManager.exe"),
        stage.path().into(),
        "2026.1.1.0",
    )
    .unwrap();
    let mut release = Release {
        tag: tag.into(),
        assets: vec![
            ReleaseAsset {
                name: name.clone(),
                browser_download_url: format!("{url}/download"),
            },
            ReleaseAsset {
                name: integrity_name(tag),
                browser_download_url: format!("{url}/bad"),
            },
        ],
        ..Release::default()
    };
    assert!(
        updater
            .download(&release, stage.path(), |_| {})
            .await
            .is_err()
    );
    assert_eq!(stage.path().read_dir().unwrap().count(), 0);
    release.assets[1].browser_download_url = format!("{url}/verified");
    let progress = std::cell::Cell::new(0.0);
    let path = updater
        .download(&release, stage.path(), |ratio| progress.set(ratio))
        .await
        .unwrap();
    assert_eq!(path, stage.path().join(name));
    assert_eq!(fs::read(path).unwrap(), bytes);
    assert_eq!(progress.get(), 1.0);
    server.abort();
}

fn script(setup: &Path, logfile: &Path) -> String {
    setup_script(
        4242,
        setup,
        Path::new(r"C:\Fixture\O'Neil Folder\update"),
        Path::new(r"C:\Fixture\O'Neil Folder\app"),
        Path::new(r"C:\Fixture\O'Neil Folder\app\VRCInventoryManager.exe"),
        logfile,
    )
}

#[test]
fn renders_installed_update_script_with_literal_paths() {
    let setup = Path::new(r"C:\Fixture\O'Neil Folder\update\Setup.exe");
    let log = Path::new(r"C:\Fixture\O'Neil Folder\update.log");
    let script = script(setup, log);
    assert!(script.contains("[int] $ProcessId = 4242"));
    assert!(script.contains(r"$SetupArguments = '/S /D=C:\Fixture\O''Neil Folder\app'"));
    assert!(!script.contains("O'N"));
    assert!(!script.contains("__"));
}

#[test]
fn preserves_template_tokens_inside_rendered_script_paths() {
    let setup = Path::new(r"C:\Fixture\'__LOG_PATH__'\Setup.exe");
    let log = Path::new(r"C:\Fixture\log");
    assert!(script(setup, log).contains(r"$SetupPath = 'C:\Fixture\''__LOG_PATH__''\Setup.exe'"));
}

#[cfg(windows)]
#[tokio::test]
async fn startup_leaves_active_update_staging_in_place() {
    use std::{fs::OpenOptions, os::windows::fs::OpenOptionsExt};

    let root = tempdir().unwrap();
    let root = root.path();
    let stage = root.join("update/apply-active");
    let abandoned = root.join("update/apply-abandoned");
    fs::create_dir_all(&stage).unwrap();
    fs::create_dir_all(&abandoned).unwrap();
    let lease = OpenOptions::new()
        .create_new(true)
        .write(true)
        .share_mode(0)
        .open(stage.join("lease"))
        .unwrap();
    let old = root.join("VRCInventoryManager.exe.old");
    fs::write(&old, b"previous binary").unwrap();
    let updater = Updater::new(
        root.join("VRCInventoryManager.exe"),
        root.into(),
        "2026.11.26.0",
    )
    .unwrap();

    updater.clean_up().await;
    assert!(stage.is_dir() && old.is_file());
    assert!(!abandoned.exists());

    drop(lease);
    updater.clean_up().await;
    assert!(!stage.exists() && !old.exists());
}

#[cfg(windows)]
fn powershell(shell: &str, command: &str, input: &str) -> std::process::Output {
    use std::{io::Write, process::Stdio};
    let mut process = vrcim_core::updater::hidden_command(shell)
        .args(["-NoProfile", "-NonInteractive", "-Command", command])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .expect("the PowerShell test process runs");
    process
        .stdin
        .take()
        .expect("the PowerShell test process runs")
        .write_all(input.as_bytes())
        .expect("the PowerShell test process runs");
    process
        .wait_with_output()
        .expect("the PowerShell test process runs")
}

#[cfg(windows)]
#[test]
fn rendered_script_parses_in_both_powershell_versions() {
    let command = r"
$source = [Console]::In.ReadToEnd()
$tokens = $null
$errors = $null
$parser = [System.Management.Automation.Language.Parser]
$null = $parser::ParseInput($source, [ref] $tokens, [ref] $errors)
if ($errors.Count -gt 0) { [Console]::Error.WriteLine(($errors | Out-String)); exit 1 }
exit 0
";
    let input = script(
        Path::new(r"C:\Fixture\O'Neil Folder\update\Setup.exe"),
        Path::new(r"C:\Fixture\update.log"),
    );
    for shell in ["pwsh.exe", "powershell.exe"] {
        let output = powershell(shell, command, &input);
        assert!(
            output.status.success(),
            "{shell}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
}

#[cfg(windows)]
#[test]
fn setup_script_relaunches_and_cleans_up_even_when_a_step_throws() {
    let root = tempdir().unwrap();
    let log = root.path().join("update.log");
    let stubs = r"
function Wait-Process { param($Id, $Timeout, $ErrorAction) }
function Get-Process { param($Id, $ErrorAction) if ($appRunning) { $true } }
function Start-Process {
    param(
        $FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru, $WorkingDirectory, $WindowStyle
    )
    if ($Wait) {
        if ($WindowStyle -ne 'Hidden') { throw [FormatException]::new('Setup was visible') }
        if ($failSetup) { throw [InvalidOperationException]::new('Setup failed') }
        return [pscustomobject] @{ ExitCode = 0 }
    }
    [Console]::Out.WriteLine('relaunch')
    if ($failRelaunch) { throw [InvalidOperationException]::new('Relaunch failed') }
}
function Remove-Item {
    param($LiteralPath, [switch]$Recurse, [switch]$Force, $ErrorAction)
    [Console]::Out.WriteLine('cleanup')
}
& ([scriptblock]::Create([Console]::In.ReadToEnd()))
";
    for (fail_setup, fail_relaunch, app_running, code) in [
        (false, false, false, 0),
        (true, false, false, 1),
        (false, true, false, 1),
        (true, true, false, 1),
        (false, false, true, 1),
    ] {
        let stage = tempfile::tempdir_in(root.path()).unwrap();
        let input = setup_script(
            4242,
            Path::new(r"C:\Fixture\O'Neil Folder\update\Setup.exe"),
            stage.path(),
            root.path(),
            &root.path().join("VRCInventoryManager.exe"),
            &log,
        );
        let command = format!(
            "$failSetup = ${fail_setup}; $failRelaunch = ${fail_relaunch}; \
             $appRunning = ${app_running}; {stubs}"
        );
        let output = powershell("pwsh.exe", &command, &input);
        assert_eq!(
            output.status.code(),
            Some(code),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert_eq!(
            String::from_utf8_lossy(&output.stdout)
                .lines()
                .collect::<Vec<_>>(),
            if app_running {
                &["cleanup"][..]
            } else {
                &["cleanup", "relaunch"][..]
            }
        );
    }
    let text = fs::read_to_string(log).unwrap();
    assert!(text.contains("starting setup") && text.contains("reopening app"));
    assert!(!text.contains("Setup failed") && !text.contains("Relaunch failed"));
}
