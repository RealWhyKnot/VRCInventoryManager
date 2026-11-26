use std::{
    convert::Infallible,
    io,
    path::{Component, Path, PathBuf},
    time::Duration,
};

use anyhow::Result;
use axum::{
    Json, Router,
    body::Body,
    extract::{Request, State},
    http::{StatusCode, header},
    middleware::{self, Next},
    response::{
        IntoResponse, Response, Sse,
        sse::{Event as SseEvent, KeepAlive},
    },
    routing::{get, post},
};
use serde::{Deserialize, Deserializer};
use serde_json::{Value, json};
use tokio_stream::{StreamExt, wrappers::BroadcastStream};
use url::Url;
use vrcim_core::{image_pipeline::ImageBytes, updater::hidden_command};

use crate::state::{App, Snapshot, same_path};

mod local;
mod remote;

include!(concat!(env!("OUT_DIR"), "/web_assets.rs"));

pub(crate) fn router(app: App) -> Router {
    Router::new()
        .route("/api/state", get(snapshot))
        .merge(local::router())
        .merge(remote::router())
        .route("/api/settings", post(settings))
        .route("/api/update/action", post(update_action))
        .route("/api/system/open-external", post(open_external))
        .route("/api/events", get(events))
        .fallback(get(static_asset))
        .layer(middleware::from_fn(local_origin))
        .with_state(app)
}

pub(crate) type ApiResult<T> = Result<T, ApiError>;

#[derive(Debug)]
pub(crate) struct ApiError {
    status: StatusCode,
    message: String,
}

impl ApiError {
    fn new(status: StatusCode, message: impl Into<String>) -> Self {
        Self {
            status,
            message: message.into(),
        }
    }

    pub(crate) fn bad(message: impl Into<String>) -> Self {
        Self::new(StatusCode::BAD_REQUEST, message)
    }

    pub(crate) fn conflict(message: impl Into<String>) -> Self {
        Self::new(StatusCode::CONFLICT, message)
    }

    fn not_found(message: impl Into<String>) -> Self {
        Self::new(StatusCode::NOT_FOUND, message)
    }
}

impl IntoResponse for ApiError {
    fn into_response(self) -> Response {
        (
            self.status,
            Json(json!({ "error": self.message, "message": self.message })),
        )
            .into_response()
    }
}

impl From<anyhow::Error> for ApiError {
    fn from(error: anyhow::Error) -> Self {
        Self::new(StatusCode::INTERNAL_SERVER_ERROR, format!("{error:#}"))
    }
}

impl From<io::Error> for ApiError {
    fn from(error: io::Error) -> Self {
        anyhow::Error::from(error).into()
    }
}

impl From<tokio::task::JoinError> for ApiError {
    fn from(error: tokio::task::JoinError) -> Self {
        anyhow::Error::from(error).into()
    }
}

async fn local_origin(request: Request, next: Next) -> Response {
    let headers = request.headers();
    let host = headers
        .get(header::HOST)
        .and_then(|value| value.to_str().ok())
        .or_else(|| {
            request
                .uri()
                .authority()
                .map(axum::http::uri::Authority::as_str)
        });
    let valid_host = host
        .and_then(|host| host.parse::<axum::http::uri::Authority>().ok())
        .is_some_and(|host| {
            matches!(host.host(), "127.0.0.1" | "localhost") && !host.as_str().contains('@')
        });
    let valid_origin = headers.get(header::ORIGIN).is_none_or(|origin| {
        host.is_some_and(|host| origin.as_bytes() == format!("http://{host}").as_bytes())
    });
    if !valid_host
        || !valid_origin
        || headers
            .get("sec-fetch-site")
            .is_some_and(|site| site == "cross-site")
    {
        return ApiError::new(
            StatusCode::FORBIDDEN,
            "This request must come from the local app.",
        )
        .into_response();
    }
    let api = request.uri().path().starts_with("/api/");
    let mut response = next.run(request).await;
    let headers = response.headers_mut();
    headers.insert(
        "x-content-type-options",
        "nosniff"
            .parse()
            .expect("static header value contains valid ASCII"),
    );
    headers.insert(
        "cross-origin-resource-policy",
        "same-origin"
            .parse()
            .expect("static header value contains valid ASCII"),
    );
    if api {
        headers.insert(
            header::CACHE_CONTROL,
            "no-store"
                .parse()
                .expect("static header value contains valid ASCII"),
        );
    }
    response
}

async fn snapshot(State(app): State<App>) -> Json<Snapshot> {
    Json(app.snapshot())
}

async fn image_work<T: Send + 'static>(
    app: &App,
    work: impl FnOnce() -> Result<T> + Send + 'static,
) -> ApiResult<T> {
    let _permit = app
        .image_gate
        .acquire()
        .await
        .map_err(|error| ApiError::from(anyhow::Error::from(error)))?;
    tokio::task::spawn_blocking(work)
        .await?
        .map_err(|error| ApiError::new(StatusCode::UNPROCESSABLE_ENTITY, format!("{error:#}")))
}

fn image_response(image: &ImageBytes) -> Response {
    (
        [(header::CONTENT_TYPE, image.content_type)],
        image.data.clone(),
    )
        .into_response()
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SettingsPatch {
    include_subfolders: Option<bool>,
    update_check: Option<bool>,
    #[serde(default, deserialize_with = "replacement")]
    skipped_update: SkippedUpdatePatch,
}

#[derive(Default)]
enum SkippedUpdatePatch {
    #[default]
    Keep,
    Replace(Option<String>),
}

fn replacement<'de, D: Deserializer<'de>>(deserializer: D) -> Result<SkippedUpdatePatch, D::Error> {
    Option::deserialize(deserializer).map(SkippedUpdatePatch::Replace)
}

async fn settings(
    State(app): State<App>,
    Json(request): Json<SettingsPatch>,
) -> ApiResult<Json<Snapshot>> {
    {
        let _operation = app.begin("Saving settings…")?;
        let mut settings = app.read().settings.clone();
        if let Some(include_subfolders) = request.include_subfolders {
            settings.include_subfolders = include_subfolders;
        }
        if let Some(update_check) = request.update_check {
            settings.update_check = update_check;
        }
        if let SkippedUpdatePatch::Replace(skipped) = request.skipped_update {
            settings.skipped_update = skipped;
        }
        app.save(&settings)?;
        let mut data = app.write();
        if !settings.update_check
            || data
                .update
                .as_ref()
                .is_some_and(|offer| Some(&offer.tag) == settings.skipped_update.as_ref())
        {
            data.update = None;
        }
        data.settings = settings;
    }
    app.notify("update");
    Ok(Json(app.snapshot()))
}

#[derive(Deserialize)]
#[serde(rename_all = "lowercase")]
enum UpdateAction {
    Install,
    Skip,
    Later,
    Check,
}

#[derive(Deserialize)]
struct UpdateRequest {
    action: UpdateAction,
}

async fn update_action(
    State(app): State<App>,
    Json(request): Json<UpdateRequest>,
) -> ApiResult<Json<Snapshot>> {
    {
        let _operation = app.begin(match request.action {
            UpdateAction::Install => "Downloading update…",
            UpdateAction::Check => "Checking for updates…",
            UpdateAction::Later | UpdateAction::Skip => "Saving update preference…",
        })?;
        match request.action {
            UpdateAction::Check => app.check_update(true).await?,
            UpdateAction::Later => app.write().update = None,
            UpdateAction::Skip => {
                let (mut settings, tag) = {
                    let data = app.read();
                    (
                        data.settings.clone(),
                        data.update.as_ref().map(|offer| offer.tag.clone()),
                    )
                };
                if let Some(tag) = tag {
                    settings.skipped_update = Some(tag);
                    app.save(&settings)?;
                    let mut data = app.write();
                    data.settings = settings;
                    data.update = None;
                }
            }
            UpdateAction::Install => {
                let release = app
                    .read()
                    .update
                    .clone()
                    .ok_or_else(|| ApiError::conflict("No update is available."))?;
                if app.fixture.is_some() {
                    app.progress(1.0);
                } else {
                    app.updater
                        .install(&release, |progress| app.progress(progress))
                        .await?;
                    app.shutdown.send_replace(true);
                }
                app.write().update = None;
            }
        }
    }
    app.notify("update");
    Ok(Json(app.snapshot()))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct OpenExternal {
    url: Option<String>,
    reveal_path: Option<PathBuf>,
}

async fn open_external(
    State(app): State<App>,
    Json(request): Json<OpenExternal>,
) -> ApiResult<Json<Value>> {
    match (request.url, request.reveal_path) {
        (Some(url), None) => {
            let Ok(url) = Url::parse(&url) else {
                return Err(ApiError::bad("The address is invalid."));
            };
            if !allowed_external(&url) {
                return Err(ApiError::bad("This address isn't available from the app."));
            }
            if app.fixture.is_none() {
                open_url(&url)?;
            }
        }
        (None, Some(path)) => {
            let permitted = {
                let data = app.read();
                same_path(&path, &data.root)
                    || data.assets.iter().any(|asset| {
                        same_path(&path, &asset.path)
                            || asset
                                .path
                                .parent()
                                .is_some_and(|parent| same_path(&path, parent))
                    })
                    || [
                        &data.known.photos,
                        &data.known.emoji,
                        &data.known.stickers,
                        &data.known.prints,
                    ]
                    .into_iter()
                    .any(|folder| same_path(&path, folder))
            };
            if !permitted || !path.is_absolute() || !path.exists() {
                return Err(ApiError::bad("Choose a file or folder from the inventory."));
            }
            if app.fixture.is_none() {
                let mut explorer = hidden_command("explorer.exe");
                if path.is_dir() {
                    explorer.arg(path);
                } else {
                    explorer.arg(format!("/select,{}", path.display()));
                }
                explorer.spawn()?;
            }
        }
        _ => return Err(ApiError::bad("Choose a link or a file to open.")),
    }
    Ok(Json(json!({ "ok": true })))
}

pub(crate) fn allowed_external(url: &Url) -> bool {
    if url.scheme() != "https"
        || url.port().is_some()
        || !url.username().is_empty()
        || url.password().is_some()
    {
        return false;
    }
    let path = url.path().to_ascii_lowercase();
    match url.host_str() {
        Some("github.com") => ["/realwhyknot/vrcinventorymanager", "/vrcx-team/vrcx"]
            .iter()
            .any(|repo| {
                path == *repo
                    || path.strip_prefix(repo).is_some_and(|suffix| {
                        suffix == "/" || suffix == "/releases" || suffix.starts_with("/releases/")
                    })
            }),
        Some("vrchat.com") => matches!(path.as_str(), "/" | "/home" | "/home/"),
        _ => false,
    }
}

#[cfg(windows)]
pub(crate) fn open_url(url: &Url) -> Result<()> {
    use windows::{
        Win32::UI::{Shell::ShellExecuteW, WindowsAndMessaging::SW_SHOWNORMAL},
        core::{HSTRING, w},
    };
    // SAFETY: every string argument is a live, null-terminated wide string for the call.
    let outcome = unsafe {
        ShellExecuteW(
            None,
            w!("open"),
            &HSTRING::from(url.as_str()),
            None,
            None,
            SW_SHOWNORMAL,
        )
    };
    anyhow::ensure!(outcome.0.addr() > 32, "Couldn't open this link.");
    Ok(())
}

#[cfg(not(windows))]
fn open_url(_: &Url) -> Result<()> {
    anyhow::bail!("Opening external links is available on Windows.")
}

async fn events(
    State(app): State<App>,
) -> Sse<impl tokio_stream::Stream<Item = Result<SseEvent, Infallible>>> {
    let live = BroadcastStream::new(app.events.subscribe()).map(|event| {
        let event = match event {
            Ok(event) => SseEvent::default()
                .data(serde_json::to_string(&event).expect("event fields serialize as JSON")),
            Err(_) => SseEvent::default().data(r#"{"type":"ready"}"#),
        };
        Ok(event)
    });
    let initial = tokio_stream::once(Ok(SseEvent::default().data(r#"{"type":"ready"}"#)));
    Sse::new(initial.chain(live)).keep_alive(KeepAlive::new().interval(Duration::from_secs(10)))
}

async fn static_asset(request: Request) -> Response {
    let name = request.uri().path().trim_start_matches('/');
    let name = if name.is_empty() { "index.html" } else { name };
    if name.starts_with("api/")
        || name.contains(['%', '\\', ':'])
        || Path::new(name)
            .components()
            .any(|part| !matches!(part, Component::Normal(_)))
    {
        return StatusCode::NOT_FOUND.into_response();
    }
    let content_type = mime_guess::from_path(name).first_or_octet_stream();
    let content_type = if content_type.as_ref() == "text/html" {
        "text/html; charset=utf-8"
    } else {
        content_type.as_ref()
    };
    if let Some(bytes) = embedded_asset(name) {
        return ([(header::CONTENT_TYPE, content_type)], Body::from(bytes)).into_response();
    }
    StatusCode::NOT_FOUND.into_response()
}
