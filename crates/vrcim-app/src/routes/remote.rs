use std::sync::Arc;

use axum::{
    Json, Router,
    extract::{Path as Id, State},
    response::Response,
    routing::{delete, get, post},
};
use serde::Deserialize;
use serde_json::{Value, json};
use uuid::Uuid;
use vrcim_core::{
    image_pipeline::{self, ImageCache, MAX_FRAMES},
    vrchat_api::{self, ANIMATED, INVENTORY_LIMIT, RemoteItem, UploadKind, UploadPayload},
};

use crate::state::{App, Snapshot, remote_failure};

use super::{ApiError, ApiResult, image_response, image_work, local::asset};

pub(super) fn router() -> Router<App> {
    Router::new()
        .route("/api/remote/items", get(remote_items))
        .route("/api/remote/thumb/{id}", get(remote_thumb))
        .route("/api/remote/preview/{id}", get(remote_preview))
        .route("/api/remote/reconnect", post(reconnect))
        .route("/api/remote/refresh", post(refresh_remote))
        .route("/api/remote/{id}", delete(delete_remote))
        .route("/api/upload", post(upload))
}

async fn remote_items(State(app): State<App>) -> Json<Value> {
    let (items, connected, auth_error) = {
        let data = app.read();
        (
            Arc::clone(&data.items),
            data.user.is_some(),
            data.auth_error.clone(),
        )
    };
    let (stickers, emojis) = vrchat_api::counts(&items);
    let views: Vec<_> = items
        .iter()
        .map(|item| {
            json!({
                "id": item.id, "name": item.name, "tag": item.tag,
                "createdAt": item.created_at.map(|timestamp| timestamp.to_string()),
                "status": item.status, "frames": item.frames, "fps": item.fps,
                "animationStyle": item.animation_style, "loopStyle": item.loop_style,
                "thumbUrl": format!("/api/remote/thumb/{}", encode_id(&item.id)),
                "previewUrl": format!("/api/remote/preview/{}", encode_id(&item.id)),
            })
        })
        .collect();
    Json(json!({
        "connected": connected,
        "authError": auth_error,
        "stickersCount": stickers,
        "stickersMax": INVENTORY_LIMIT,
        "emojisCount": emojis,
        "emojisMax": INVENTORY_LIMIT,
        "items": views,
    }))
}

fn encode_id(id: &str) -> String {
    const HEX: &[u8] = b"0123456789ABCDEF";
    let mut encoded = String::with_capacity(id.len());
    for byte in id.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_' | b'.' | b'~') {
            encoded.push(char::from(byte));
        } else {
            encoded.push('%');
            encoded.push(char::from(HEX[usize::from(byte >> 4)]));
            encoded.push(char::from(HEX[usize::from(byte & 15)]));
        }
    }
    encoded
}

async fn remote_thumb(State(app): State<App>, Id(id): Id<String>) -> ApiResult<Response> {
    remote_image(&app, &id, true).await
}

async fn remote_preview(State(app): State<App>, Id(id): Id<String>) -> ApiResult<Response> {
    remote_image(&app, &id, false).await
}

async fn remote_image(app: &App, id: &str, thumbnail: bool) -> ApiResult<Response> {
    let (item, api) = {
        let data = app.read();
        let item = data
            .items
            .iter()
            .find(|item| item.id == id)
            .cloned()
            .ok_or_else(|| ApiError::not_found("This VRChat item is no longer available."))?;
        (item, data.api.clone())
    };
    if item.preview_url.is_empty() {
        return Err(ApiError::not_found("This VRChat item has no preview."));
    }
    let cache: &ImageCache = if thumbnail {
        &app.remote_thumbs
    } else {
        &app.remote_previews
    };
    let key = format!("{id}:{}", item.preview_url);
    if let Some(image) = cache.get(&key) {
        return Ok(image_response(&image));
    }
    let frames = item
        .frames
        .filter(|&frames| item.tag == ANIMATED && (2..=MAX_FRAMES).contains(&frames));
    let bytes = if let Some(original) = app.remote_previews.get(&key) {
        original.data.clone()
    } else if app.fixture.is_some() {
        tokio::fs::read(&item.preview_url).await?
    } else {
        let _permit = app
            .image_gate
            .acquire()
            .await
            .map_err(|error| ApiError::from(anyhow::Error::from(error)))?;
        let api = api.ok_or_else(|| ApiError::conflict("Reconnect to VRChat first."))?;
        api.image(&item.preview_url).await?
    };
    let image = image_work(app, move || {
        if thumbnail {
            image_pipeline::thumbnail(&bytes, frames)
        } else {
            image_pipeline::web_image(bytes)
        }
    })
    .await?;
    let image = cache.insert(key, image);
    Ok(image_response(&image))
}

async fn reconnect(State(app): State<App>) -> ApiResult<Json<Snapshot>> {
    {
        let _operation = app.begin("Connecting to VRChat…")?;
        app.reconnect().await;
    }
    Ok(Json(app.snapshot()))
}

async fn refresh_remote(State(app): State<App>) -> ApiResult<Json<Snapshot>> {
    {
        let _operation = app.begin("Refreshing VRChat inventory…")?;
        app.refresh_remote().await?;
    }
    Ok(Json(app.snapshot()))
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Upload {
    asset_id: String,
    kind: String,
    animation_style: Option<String>,
    loop_style: Option<String>,
    fps_override: Option<u32>,
}

async fn upload(State(app): State<App>, Json(request): Json<Upload>) -> ApiResult<Json<Value>> {
    let kind = match request.kind.to_ascii_lowercase().as_str() {
        "sticker" => UploadKind::Sticker,
        "emoji" | "static" | "staticemoji" | "static-emoji" => UploadKind::Emoji,
        "animated" | "emojianimated" | "animatedemoji" | "animated-emoji" => UploadKind::Animated,
        "auto" | "autoemoji" | "auto-emoji" => UploadKind::AutoEmoji,
        _ => return Err(ApiError::bad("Choose sticker, emoji, or animated emoji.")),
    };
    let _operation = app.begin("Uploading to VRChat…")?;
    let asset = asset(&app, &request.asset_id)?;
    let api = {
        let data = app.read();
        if data.user.is_none() {
            return Err(ApiError::conflict("Reconnect to VRChat first."));
        }
        let (stickers, emojis) = vrchat_api::counts(&data.items);
        if kind == UploadKind::Sticker && stickers >= INVENTORY_LIMIT {
            return Err(ApiError::conflict("Your 18 sticker slots are full."));
        }
        if kind != UploadKind::Sticker && emojis >= INVENTORY_LIMIT {
            return Err(ApiError::conflict("Your 18 emoji slots are full."));
        }
        data.api.clone()
    };
    let name = asset.name.clone();
    let payload = tokio::task::spawn_blocking(move || {
        UploadPayload::prepare(
            &asset,
            kind,
            request.animation_style.as_deref(),
            request.loop_style.as_deref(),
            request.fps_override,
        )
    })
    .await?
    .map_err(|error| ApiError::bad(error.to_string()))?;
    let tag = payload.tag;
    let file_id = if let Some(fixture) = &app.fixture {
        let id = format!("file_{}", Uuid::new_v4());
        let image = fixture.remote_image(&id);
        tokio::fs::write(&image, payload.png).await?;
        let item = RemoteItem {
            id: id.clone(),
            name,
            tag: payload.tag.into(),
            mime_type: "image/png".into(),
            created_at: Some(jiff::Timestamp::now()),
            status: "complete".into(),
            frames: payload.frames,
            fps: payload.fps,
            animation_style: payload.animation_style.unwrap_or_default().into(),
            loop_style: payload.loop_style.unwrap_or_default().into(),
            mask_tag: "square".into(),
            preview_url: image.to_string_lossy().into_owned(),
        };
        let mut data = app.write();
        let items = Arc::make_mut(&mut data.items);
        items.push(item);
        vrchat_api::sort_items(items);
        drop(data);
        id
    } else {
        let api = api.ok_or_else(|| ApiError::conflict("Reconnect to VRChat first."))?;
        let id = api.upload(payload).await?;
        if let Err(error) = app.refresh_remote().await {
            tracing::warn!("Uploaded an image. {}.", remote_failure(&error));
            if !id.is_empty() {
                let mut data = app.write();
                let items = Arc::make_mut(&mut data.items);
                items.push(RemoteItem {
                    id: id.clone(),
                    name,
                    tag: tag.into(),
                    status: "waiting".into(),
                    created_at: Some(jiff::Timestamp::now()),
                    ..RemoteItem::default()
                });
                vrchat_api::sort_items(items);
                drop(data);
            }
        }
        id
    };
    app.notify("remote");
    Ok(Json(json!({ "id": file_id, "tag": tag })))
}

async fn delete_remote(State(app): State<App>, Id(id): Id<String>) -> ApiResult<Json<Value>> {
    let _operation = app.begin("Deleting from VRChat…")?;
    let api = {
        let data = app.read();
        if data.user.is_none() {
            return Err(ApiError::conflict("Reconnect to VRChat first."));
        }
        if !data.items.iter().any(|item| item.id == id) {
            return Err(ApiError::not_found(
                "This VRChat item is no longer available.",
            ));
        }
        data.api.clone()
    };
    if let Some(fixture) = &app.fixture {
        let _ = tokio::fs::remove_file(fixture.remote_image(&id)).await;
    } else {
        api.ok_or_else(|| ApiError::conflict("Reconnect to VRChat first."))?
            .delete(&id)
            .await?;
    }
    Arc::make_mut(&mut app.write().items).retain(|item| item.id != id);
    app.remote_thumbs.clear();
    app.remote_previews.clear();
    app.notify("remote");
    Ok(Json(json!({ "id": id })))
}
