use std::{
    path::{Path, PathBuf},
    sync::Arc,
};

use axum::{
    Json, Router,
    extract::{Path as Id, Query, State},
    http::{HeaderMap, header},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use vrcim_core::{
    image_pipeline::{self, ImageBytes, MAX_FRAMES, THUMB_SIDE},
    local_assets::{self, LocalAsset},
};

use crate::state::{App, RootKind, Snapshot, read_asset, same_path};

use super::{ApiError, ApiResult, image_response, image_work};

pub(super) fn router() -> Router<App> {
    Router::new()
        .route("/api/local/assets", get(local_assets_route))
        .route("/api/local/thumb/{id}", get(local_thumb))
        .route("/api/local/preview/{id}", get(local_preview))
        .route("/api/local/spritesheet/{id}", get(local_sprite))
        .route("/api/local/select-root", post(select_root))
        .route("/api/local/browse", post(browse))
        .route("/api/local/refresh", post(refresh_local))
        .route("/api/local/import-paths", post(import_paths))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct AssetFilter {
    #[serde(default)]
    folder: String,
    #[serde(default)]
    query: String,
    include_subfolders: Option<bool>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LocalAssetView<'a> {
    id: &'a str,
    name: &'a str,
    path: &'a Path,
    relative_dir: &'a str,
    bucket: &'a str,
    ext: &'a str,
    size_bytes: u64,
    size_text: String,
    modified_ms: i64,
    width: Option<u32>,
    height: Option<u32>,
    is_animated: bool,
    is_gif: bool,
    is_sprite_sheet: bool,
    frames: Option<u32>,
    fps: Option<u32>,
    animation_style: &'a str,
    loop_style: &'a str,
    thumb_url: String,
    preview_url: String,
    sprite_sheet_url: Option<String>,
}

impl<'a> From<&'a LocalAsset> for LocalAssetView<'a> {
    fn from(asset: &'a LocalAsset) -> Self {
        Self {
            id: &asset.id,
            name: &asset.name,
            path: &asset.path,
            relative_dir: &asset.relative_dir,
            bucket: &asset.bucket,
            ext: &asset.ext,
            size_bytes: asset.size_bytes,
            size_text: asset.size_text(),
            modified_ms: asset.modified_ms,
            width: asset.dimensions.map(|(width, _)| width),
            height: asset.dimensions.map(|(_, height)| height),
            is_animated: asset.is_animated(),
            is_gif: asset.is_gif(),
            is_sprite_sheet: asset.is_sprite_sheet(),
            frames: asset.metadata.frames,
            fps: asset.metadata.fps,
            animation_style: asset.metadata.animation_style,
            loop_style: asset.metadata.loop_style,
            thumb_url: format!("/api/local/thumb/{}", asset.id),
            preview_url: format!("/api/local/preview/{}", asset.id),
            sprite_sheet_url: asset
                .is_sprite_sheet()
                .then(|| format!("/api/local/spritesheet/{}", asset.id)),
        }
    }
}

async fn local_assets_route(
    State(app): State<App>,
    Query(query): Query<AssetFilter>,
) -> ApiResult<Json<Value>> {
    let (root, assets, include) = {
        let data = app.read();
        (
            data.root.clone(),
            Arc::clone(&data.assets),
            query
                .include_subfolders
                .unwrap_or(data.settings.include_subfolders),
        )
    };
    if query.query.len() > 1024 || query.folder.len() > 4096 {
        return Err(ApiError::bad("The folder or search text is too long."));
    }
    let relative_folder = Path::new(&query.folder)
        .strip_prefix(&root)
        .ok()
        .and_then(Path::to_str)
        .unwrap_or(&query.folder);
    let filtered: Vec<_> = assets
        .iter()
        .filter(|asset| asset.matches(relative_folder, &query.query, include))
        .map(LocalAssetView::from)
        .collect();
    let tree = local_assets::folder_tree(&root, &assets);
    Ok(Json(
        json!({ "root": root, "totalCount": assets.len(), "folders": [tree], "assets": filtered }),
    ))
}

pub(super) fn asset(app: &App, id: &str) -> ApiResult<LocalAsset> {
    app.read()
        .assets
        .iter()
        .find(|asset| asset.id == id)
        .cloned()
        .ok_or_else(|| ApiError::not_found("This image is no longer in the selected folder."))
}

async fn local_thumb(State(app): State<App>, Id(id): Id<String>) -> ApiResult<Response> {
    let asset = asset(&app, &id)?;
    let key = format!("{id}:{}:{}", asset.modified_ms, asset.size_bytes);
    if let Some(image) = app.local_thumbs.get(&key) {
        return Ok(image_response(&image));
    }
    let image = image_work(&app, move || {
        let source = read_asset(&asset.path)?;
        image_pipeline::thumbnail(&source, sprite_frames(&asset))
    })
    .await?;
    let image = app.local_thumbs.insert(key, image);
    Ok(image_response(&image))
}

async fn local_preview(State(app): State<App>, Id(id): Id<String>) -> ApiResult<Response> {
    let asset = asset(&app, &id)?;
    let image = image_work(&app, move || {
        image_pipeline::web_image(read_asset(&asset.path)?)
    })
    .await?;
    Ok(image_response(&image))
}

fn sprite_frames(asset: &LocalAsset) -> Option<u32> {
    asset
        .metadata
        .frames
        .filter(|&frames| asset.is_sprite_sheet() && frames <= MAX_FRAMES)
}

#[derive(Deserialize)]
struct SpriteQuery {
    frame: Option<u32>,
    image: Option<String>,
    raw: Option<String>,
}

async fn local_sprite(
    State(app): State<App>,
    Id(id): Id<String>,
    Query(query): Query<SpriteQuery>,
    headers: HeaderMap,
) -> ApiResult<Response> {
    let asset = asset(&app, &id)?;
    let frames = sprite_frames(&asset)
        .ok_or_else(|| ApiError::not_found("This file has no sprite sheet."))?;
    if let Some(index) = query.frame {
        if index >= frames {
            return Err(ApiError::not_found(
                "This frame is outside the sprite sheet.",
            ));
        }
        let image = image_work(&app, move || {
            let sheet = image::load_from_memory(&read_asset(&asset.path)?)?;
            Ok(ImageBytes::png(image_pipeline::frame_png(
                &sheet, frames, index, THUMB_SIDE,
            )?))
        })
        .await?;
        return Ok(image_response(&image));
    }
    let wants_image = headers
        .get(header::ACCEPT)
        .and_then(|value| value.to_str().ok())
        .is_some_and(|value| value.starts_with("image/"));
    if query.image.is_some() || query.raw.is_some() || wants_image {
        let image = image_work(&app, move || {
            image_pipeline::web_image(read_asset(&asset.path)?)
        })
        .await?;
        return Ok(image_response(&image));
    }
    let (width, height) = asset
        .dimensions
        .ok_or_else(|| ApiError::bad("Couldn't read the sprite sheet dimensions."))?;
    let grid = image_pipeline::grid(frames)?;
    Ok(Json(
        json!({ "frames": frames, "fps": asset.metadata.fps.unwrap_or(1).min(64),
        "cols": grid, "rows": grid, "width": width, "height": height,
        "imageUrl": format!("/api/local/spritesheet/{id}?image=1") }),
    )
    .into_response())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SelectRoot {
    kind: RootKind,
    path: Option<PathBuf>,
    include_subfolders: Option<bool>,
}

async fn select_root(
    State(app): State<App>,
    Json(request): Json<SelectRoot>,
) -> ApiResult<Json<Snapshot>> {
    change_root(&app, request).await?;
    Ok(Json(app.snapshot()))
}

async fn change_root(app: &App, request: SelectRoot) -> ApiResult<()> {
    let _operation = app.begin("Scanning images…")?;
    scan_root(app, request).await
}

async fn scan_root(app: &App, request: SelectRoot) -> ApiResult<()> {
    let (root, mut settings, exclude, imported) = {
        let data = app.read();
        let root = if let Some(path) = request.kind.path(&data.known) {
            path.to_owned()
        } else if let Some(path) = request.path {
            if !path.is_absolute() || !path.is_dir() {
                return Err(ApiError::bad("Choose an existing folder."));
            }
            std::path::absolute(path)?
        } else if data.kind == RootKind::Custom {
            data.root.clone()
        } else {
            return Err(ApiError::bad("Choose a folder first."));
        };
        let imported = if same_path(&root, &data.root) {
            data.imported.clone()
        } else {
            Vec::new()
        };
        let exclude = same_path(&root, &data.known.photos);
        (root, data.settings.clone(), exclude, imported)
    };
    let assets = app.scan(root.clone(), exclude, imported.clone()).await?;
    settings.local_root = root.to_string_lossy().into_owned();
    if let Some(include_subfolders) = request.include_subfolders {
        settings.include_subfolders = include_subfolders;
    }
    app.save(&settings)?;
    let mut data = app.write();
    data.kind = request.kind;
    data.root = root;
    data.assets = Arc::new(assets);
    data.settings = settings;
    data.imported = imported;
    drop(data);
    app.local_thumbs.clear();
    app.notify("local");
    Ok(())
}

async fn refresh_local(State(app): State<App>) -> ApiResult<Json<Snapshot>> {
    let (kind, root) = {
        let data = app.read();
        (data.kind, data.root.clone())
    };
    change_root(
        &app,
        SelectRoot {
            kind,
            path: Some(root),
            include_subfolders: None,
        },
    )
    .await?;
    Ok(Json(app.snapshot()))
}

async fn browse(State(app): State<App>) -> ApiResult<Json<Value>> {
    let folder = {
        let _operation = app.begin("Choosing a folder…")?;
        let folder = if app.fixture.is_some() {
            Some(app.read().known.photos.clone())
        } else {
            let root = app.read().root.clone();
            tokio::task::spawn_blocking(move || pick_folder(&root)).await?
        };
        if let Some(path) = &folder {
            scan_root(
                &app,
                SelectRoot {
                    kind: RootKind::Custom,
                    path: Some(path.clone()),
                    include_subfolders: None,
                },
            )
            .await?;
        }
        folder
    };
    let mut response = serde_json::to_value(app.snapshot()).map_err(anyhow::Error::from)?;
    response["cancelled"] = Value::Bool(folder.is_none());
    response["path"] = serde_json::to_value(folder).map_err(anyhow::Error::from)?;
    Ok(Json(response))
}

#[cfg(windows)]
fn pick_folder(root: &Path) -> Option<PathBuf> {
    rfd::FileDialog::new()
        .set_title("Choose an image folder")
        .set_directory(root)
        .pick_folder()
}

#[cfg(not(windows))]
fn pick_folder(_: &Path) -> Option<PathBuf> {
    None
}

#[derive(Deserialize)]
struct ImportPaths {
    paths: Vec<PathBuf>,
}

async fn import_paths(
    State(app): State<App>,
    Json(request): Json<ImportPaths>,
) -> ApiResult<Json<Snapshot>> {
    app.import_paths(request.paths).await?;
    Ok(Json(app.snapshot()))
}
