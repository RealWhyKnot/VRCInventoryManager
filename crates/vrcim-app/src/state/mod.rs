use std::{
    fs,
    num::NonZeroUsize,
    path::{Path, PathBuf},
    sync::{Arc, RwLock, RwLockReadGuard, RwLockWriteGuard},
    time::Instant,
};

use anyhow::{Context, Result};
use serde::Serialize;
use tokio::sync::{Mutex, MutexGuard, Semaphore, broadcast, watch};
use vrcim_core::{
    image_pipeline::ImageCache,
    local_assets::{self, LocalAsset},
    paths::{KnownFolders, UserPaths, read_vrcx_version},
    settings::{Settings, SettingsStore},
    updater::{Release, ReleaseVersion, Updater},
    vrchat_api::{HttpError, INVENTORY_LIMIT, RemoteItem, User, VrchatApi, counts},
    vrcx_cookies,
};

use crate::{
    VERSION,
    fixture::{Fixture, Mode},
    routes::{ApiError, ApiResult},
};

mod roots;

pub(crate) use roots::RootKind;

pub(crate) type App = Arc<AppState>;

pub(crate) struct AppState {
    pub paths: UserPaths,
    pub store: Option<SettingsStore>,
    pub updater: Updater,
    pub fixture: Option<Fixture>,
    pub local_thumbs: ImageCache,
    pub remote_thumbs: ImageCache,
    pub remote_previews: ImageCache,
    pub image_gate: Semaphore,
    pub events: broadcast::Sender<Event>,
    pub shutdown: watch::Sender<bool>,
    data: RwLock<Data>,
    gate: Mutex<()>,
}

pub(crate) struct Data {
    pub known: KnownFolders,
    pub kind: RootKind,
    pub root: PathBuf,
    pub assets: Arc<Vec<LocalAsset>>,
    pub imported: Vec<PathBuf>,
    pub settings: Settings,
    pub busy_message: Option<String>,
    pub user: Option<User>,
    pub auth_error: Option<String>,
    pub version: String,
    pub api: Option<VrchatApi>,
    pub items: Arc<Vec<RemoteItem>>,
    pub update: Option<Release>,
}

pub(crate) fn same_path(left: &Path, right: &Path) -> bool {
    left.to_string_lossy()
        .trim_end_matches(['/', '\\'])
        .replace('\\', "/")
        .eq_ignore_ascii_case(
            &right
                .to_string_lossy()
                .trim_end_matches(['/', '\\'])
                .replace('\\', "/"),
        )
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Event {
    #[serde(rename = "type")]
    pub kind: &'static str,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub progress: Option<f64>,
}

impl AppState {
    pub(crate) fn new(mode: Option<Mode>, executable: PathBuf) -> Result<App> {
        let paths = UserPaths::current();
        let fixture = mode.map(Fixture::new).transpose()?;
        let store = fixture
            .is_none()
            .then(|| SettingsStore::new(paths.roaming().join("settings.json")));
        let settings = store
            .as_ref()
            .map_or_else(Settings::default, SettingsStore::load);
        let resolved = paths.resolve();
        let known = fixture
            .as_ref()
            .map_or(resolved.folders, |fixture| fixture.folders.clone());
        let root = if settings.local_root.trim().is_empty() {
            known.emoji.clone()
        } else {
            std::path::absolute(&settings.local_root)
                .unwrap_or_else(|_| PathBuf::from(&settings.local_root))
        };
        let assets = if fixture.is_some() {
            local_assets::scan(&root, false)
        } else {
            Vec::new()
        };
        let items = fixture
            .as_ref()
            .map(Fixture::inventory)
            .transpose()?
            .unwrap_or_default();
        let version = if fixture.is_some() {
            "VRCX/2026.11.20".into()
        } else {
            read_vrcx_version(&resolved.version)
        };
        let user = fixture.as_ref().and_then(Fixture::user);
        let auth_error = fixture.as_ref().and_then(Fixture::auth_error);
        let update = fixture.as_ref().and_then(Fixture::release);
        let busy_message = (mode == Some(Mode::Loading)).then(|| "Loading inventory…".into());
        let updater = Updater::new(executable, paths.local(), VERSION)?;
        let kind = RootKind::from_path(&root, &known);
        Ok(Arc::new(Self {
            paths,
            store,
            updater,
            fixture,
            local_thumbs: ImageCache::new(
                NonZeroUsize::new(512).expect("cache capacity is greater than zero"),
            ),
            remote_thumbs: ImageCache::new(
                NonZeroUsize::new(128).expect("cache capacity is greater than zero"),
            ),
            remote_previews: ImageCache::new(
                NonZeroUsize::new(16).expect("cache capacity is greater than zero"),
            ),
            image_gate: Semaphore::new(8),
            events: broadcast::channel(128).0,
            shutdown: watch::channel(false).0,
            data: RwLock::new(Data {
                known,
                kind,
                root,
                assets: Arc::new(assets),
                imported: Vec::new(),
                settings,
                busy_message,
                user,
                auth_error,
                version,
                api: None,
                items: Arc::new(items),
                update,
            }),
            gate: Mutex::new(()),
        }))
    }

    pub(crate) fn read(&self) -> RwLockReadGuard<'_, Data> {
        self.data
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }

    pub(crate) fn write(&self) -> RwLockWriteGuard<'_, Data> {
        self.data
            .write()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }

    pub(crate) fn notify(&self, kind: &'static str) {
        let _ = self.events.send(Event {
            kind,
            progress: None,
        });
    }

    pub(crate) fn progress(&self, progress: f64) {
        let _ = self.events.send(Event {
            kind: "update-progress",
            progress: Some(progress),
        });
    }

    pub(crate) fn begin(&self, message: &str) -> ApiResult<Operation<'_>> {
        let Ok(guard) = self.gate.try_lock() else {
            return Err(ApiError::conflict("Another operation is in progress."));
        };
        if self
            .fixture
            .as_ref()
            .is_some_and(|fixture| fixture.mode == Mode::Loading)
        {
            return Err(ApiError::conflict("The inventory is still loading."));
        }
        self.write().busy_message = Some(message.into());
        self.notify("state");
        Ok(Operation {
            app: self,
            _guard: guard,
        })
    }

    pub(crate) fn save(&self, settings: &Settings) -> Result<()> {
        if let Some(store) = &self.store {
            store.save(settings)?;
        }
        Ok(())
    }

    pub(crate) async fn startup(self: App) {
        if self.fixture.is_some() {
            return;
        }
        let Ok(_operation) = self.begin("Loading inventory…") else {
            return;
        };
        let load = async {
            let (root, excluded) = {
                let data = self.read();
                (data.root.clone(), same_path(&data.root, &data.known.photos))
            };
            if let Ok(assets) = self.scan(root, excluded, Vec::new()).await {
                self.write().assets = Arc::new(assets);
                self.notify("local");
            } else {
                tracing::warn!("Could not scan the local folder.");
            }
        };
        tokio::join!(load, self.reconnect());
        self.updater.clean_up().await;
        let enabled = self.read().settings.update_check;
        if enabled && self.check_update(false).await.is_err() {
            tracing::warn!("Could not check for an update.");
        }
    }

    pub(crate) async fn scan(
        &self,
        root: PathBuf,
        excluded: bool,
        imported: Vec<PathBuf>,
    ) -> Result<Vec<LocalAsset>> {
        let started = Instant::now();
        let assets = tokio::task::spawn_blocking(move || {
            let mut assets = local_assets::scan(&root, excluded);
            let mut existing: std::collections::HashSet<_> =
                assets.iter().map(|asset| asset.id.clone()).collect();
            for path in imported {
                if let Some(asset) = LocalAsset::read(&path, &root)
                    && existing.insert(asset.id.clone())
                {
                    assets.push(asset);
                }
            }
            local_assets::sort_assets(&mut assets);
            assets
        })
        .await?;
        tracing::debug!(
            images = assets.len(),
            elapsed_ms = started.elapsed().as_millis(),
            "Scanned the local folder."
        );
        Ok(assets)
    }

    pub(crate) async fn reconnect(&self) {
        if let Some(fixture) = &self.fixture {
            let mut data = self.write();
            data.user = fixture.user();
            data.auth_error = fixture.auth_error();
            drop(data);
            self.notify("remote");
            return;
        }
        let paths = self.paths.clone();
        let session = tokio::task::spawn_blocking(move || {
            let paths = paths.resolve();
            vrcx_cookies::load_session(&paths.database, &paths.version, &std::env::temp_dir())
        })
        .await;
        let connect = async {
            let session = session??;
            self.write().version.clone_from(&session.user_agent);
            let api = VrchatApi::new(session)?;
            let user = api.user().await?;
            api.fetch_config().await?;
            let items = api.inventory().await?;
            Ok::<_, anyhow::Error>((api, user, items))
        }
        .await;
        let mut data = self.write();
        match connect {
            Ok((api, user, items)) => {
                data.api = Some(api);
                data.user = Some(user);
                data.items = Arc::new(items);
                data.auth_error = None;
            }
            Err(error) => {
                let message = format!("{error:#}");
                tracing::warn!("{}.", remote_failure(&error));
                data.auth_error = Some(message);
                data.api = None;
                data.user = None;
                data.items = Arc::default();
            }
        }
        drop(data);
        self.remote_thumbs.clear();
        self.remote_previews.clear();
        self.notify("remote");
    }

    pub(crate) async fn refresh_remote(&self) -> Result<()> {
        if self.fixture.is_some() {
            anyhow::ensure!(self.read().user.is_some(), "Reconnect to VRChat first.");
            self.notify("remote");
            return Ok(());
        }
        let api = self
            .read()
            .api
            .clone()
            .context("Reconnect to VRChat first.")?;
        let items = api.inventory().await?;
        self.write().items = Arc::new(items);
        self.remote_thumbs.clear();
        self.remote_previews.clear();
        self.notify("remote");
        Ok(())
    }

    pub(crate) async fn check_update(&self, manual: bool) -> Result<()> {
        let skipped = self.read().settings.skipped_update.clone();
        let offer = if let Some(fixture) = &self.fixture {
            fixture
                .release()
                .filter(|offer| manual || Some(offer.tag.as_str()) != skipped.as_deref())
        } else {
            self.updater
                .find(if manual { None } else { skipped.as_deref() })
                .await?
        };
        self.write().update = offer;
        self.notify("update");
        Ok(())
    }

    pub(crate) fn snapshot(&self) -> Snapshot {
        let data = self.read();
        let (stickers, emojis) = counts(&data.items);
        Snapshot {
            version: VERSION,
            is_installed: self.fixture.is_none() && self.updater.is_installed(),
            busy: data.busy_message.is_some(),
            busy_message: data.busy_message.clone(),
            account: Account {
                connected: data.user.is_some(),
                auth_error: data.auth_error.clone(),
                user: data.user.clone(),
            },
            vrcx_version: data.version.clone(),
            selected_root_kind: data.kind,
            local_root: data.root.clone(),
            include_subfolders: data.settings.include_subfolders,
            known_folders: data.known.clone(),
            settings: SettingsView::from(&data.settings),
            counts: Counts {
                stickers,
                emojis,
                stickers_max: INVENTORY_LIMIT,
                emojis_max: INVENTORY_LIMIT,
            },
            update_offer: data.update.as_ref().map(|offer| UpdateOffer {
                version: ReleaseVersion::parse(&offer.tag)
                    .map_or_else(|| offer.tag.clone(), |version| version.to_string()),
                tag: offer.tag.clone(),
                html_url: offer.html_url.clone(),
                is_beta: offer.prerelease,
            }),
        }
    }

    pub(crate) async fn import_paths(&self, paths: Vec<PathBuf>) -> ApiResult<()> {
        if paths.len() > 256 {
            return Err(ApiError::bad("Choose up to 256 files or folders at once."));
        }
        let _operation = self.begin("Importing images…")?;
        let mut files = Vec::new();
        let mut directories = Vec::new();
        for path in paths {
            if !path.is_absolute() {
                return Err(ApiError::bad("Choose an absolute file or folder path."));
            }
            if path.is_file() {
                files.push(path);
            } else if path.is_dir() {
                directories.push(path);
            }
        }
        if files.is_empty() && directories.is_empty() {
            return Err(ApiError::bad(
                "None of the selected files or folders are available.",
            ));
        }
        let (mut root, mut kind, mut imported, mut settings, known) = {
            let data = self.read();
            (
                data.root.clone(),
                data.kind,
                data.imported.clone(),
                data.settings.clone(),
                data.known.clone(),
            )
        };
        let has_directory = !directories.is_empty();
        if let Some(folder) = directories.first() {
            root = folder.clone();
            kind = RootKind::from_path(&root, &known);
            imported.clear();
        }
        for folder in directories.into_iter().skip(1) {
            files.extend(
                self.scan(folder, false, Vec::new())
                    .await?
                    .into_iter()
                    .map(|asset| asset.path),
            );
        }
        if files.iter().any(|file| !valid_image_path(file)) {
            files.retain(|file| valid_image_path(file));
        }
        if files.is_empty() && !has_directory {
            return Err(ApiError::bad("Choose a supported image file."));
        }
        imported.extend(files);
        imported.sort_unstable();
        imported.dedup();
        let assets = self
            .scan(
                root.clone(),
                same_path(&root, &known.photos),
                imported.clone(),
            )
            .await?;
        settings.local_root = root.to_string_lossy().into_owned();
        self.save(&settings)?;
        let mut data = self.write();
        data.root = root;
        data.kind = kind;
        data.assets = Arc::new(assets);
        data.imported = imported;
        data.settings = settings;
        drop(data);
        self.local_thumbs.clear();
        self.notify("local");
        Ok(())
    }
}

pub(crate) fn remote_failure(error: &anyhow::Error) -> String {
    if let Some(error) = error.downcast_ref::<HttpError>() {
        return format!("VRChat request failed with HTTP {}", error.status.as_u16());
    }
    "Couldn't complete the VRChat request".into()
}

fn valid_image_path(path: &Path) -> bool {
    path.extension()
        .and_then(|ext| ext.to_str())
        .is_some_and(|ext| {
            matches!(
                ext.to_ascii_lowercase().as_str(),
                "png" | "gif" | "jpg" | "jpeg" | "bmp" | "tif" | "tiff"
            )
        })
}

pub(crate) struct Operation<'a> {
    app: &'a AppState,
    _guard: MutexGuard<'a, ()>,
}

impl Drop for Operation<'_> {
    fn drop(&mut self) {
        self.app.write().busy_message = None;
        self.app.notify("state");
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Snapshot {
    version: &'static str,
    is_installed: bool,
    busy: bool,
    busy_message: Option<String>,
    #[serde(flatten)]
    account: Account,
    vrcx_version: String,
    selected_root_kind: RootKind,
    local_root: PathBuf,
    include_subfolders: bool,
    known_folders: KnownFolders,
    settings: SettingsView,
    counts: Counts,
    update_offer: Option<UpdateOffer>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Account {
    connected: bool,
    auth_error: Option<String>,
    user: Option<User>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SettingsView {
    local_root: String,
    update_check: bool,
    skipped_update: Option<String>,
}

impl From<&Settings> for SettingsView {
    fn from(settings: &Settings) -> Self {
        Self {
            local_root: settings.local_root.clone(),
            update_check: settings.update_check,
            skipped_update: settings.skipped_update.clone(),
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Counts {
    stickers: usize,
    stickers_max: usize,
    emojis: usize,
    emojis_max: usize,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct UpdateOffer {
    version: String,
    tag: String,
    html_url: String,
    is_beta: bool,
}

pub(crate) fn read_asset(path: &Path) -> Result<Vec<u8>> {
    use std::io::Read;
    let mut data = Vec::new();
    let limit = 64 * 1024 * 1024;
    fs::File::open(path)?
        .take(limit + 1)
        .read_to_end(&mut data)?;
    anyhow::ensure!(
        u64::try_from(data.len())? <= limit,
        "This image is too large to preview."
    );
    Ok(data)
}
