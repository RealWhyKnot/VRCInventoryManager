use std::{ffi::OsStr, path::PathBuf, sync::Arc, time::Duration};

use axum::{
    Router,
    body::{Body, to_bytes},
    http::{Request, StatusCode, header},
    response::Response,
};
use image::{GenericImageView, ImageFormat};
use serde_json::{Value, json};
use tokio_stream::StreamExt;
use tower::ServiceExt;

use crate::{Options, fixture::Mode, routes, state::AppState};

#[cfg(windows)]
mod pageframe;

fn app(mode: Mode) -> Router {
    routes::router(AppState::new(Some(mode), PathBuf::from("fixture.exe")).unwrap())
}

async fn request(app: &Router, method: &str, uri: &str, body: Option<Value>) -> Response {
    let mut request = Request::builder()
        .method(method)
        .uri(uri)
        .header(header::HOST, "127.0.0.1:47655");
    let body = if let Some(body) = body {
        request = request.header(header::CONTENT_TYPE, "application/json");
        Body::from(body.to_string())
    } else {
        Body::empty()
    };
    app.clone()
        .oneshot(request.body(body).unwrap())
        .await
        .unwrap()
}

async fn json_body(response: Response) -> Value {
    let status = response.status();
    let bytes = to_bytes(response.into_body(), 10 * 1024 * 1024)
        .await
        .unwrap();
    serde_json::from_slice(&bytes)
        .unwrap_or_else(|error| panic!("{status}: {error}. {}", String::from_utf8_lossy(&bytes)))
}

async fn get(app: &Router, uri: &str) -> Value {
    let response = request(app, "GET", uri, None).await;
    assert_eq!(response.status(), StatusCode::OK, "{uri}");
    json_body(response).await
}

async fn post(app: &Router, uri: &str, body: Value) -> Value {
    let response = request(app, "POST", uri, Some(body)).await;
    let status = response.status();
    let json = json_body(response).await;
    assert_eq!(status, StatusCode::OK, "{uri}: {json}");
    json
}

#[test]
fn parses_fixture_cli_modes_and_optional_port() {
    for mode in ["full", "empty", "loading", "error", "update", "limit"] {
        let args = ["--serve-only", "--port", "49201", "--fixture", mode]
            .into_iter()
            .map(str::to_owned);
        let parsed = Options::parse(args).unwrap();
        assert!(parsed.serve_only);
        assert_eq!(parsed.port, 49201);
        assert!(parsed.fixture.is_some());
    }
    let args = ["--fixture", "--serve-only"].into_iter().map(str::to_owned);
    assert_eq!(Options::parse(args).unwrap().fixture, Some(Mode::Full));
    assert!(Options::parse(["--port", "65536"].into_iter().map(str::to_owned)).is_err());
    assert!(Options::parse(["--fixture", "invalid"].into_iter().map(str::to_owned)).is_err());
}

#[tokio::test]
async fn full_fixture_exposes_state_rollups_and_all_inventory_fields() {
    let app = app(Mode::Full);
    let state = get(&app, "/api/state").await;
    assert!(state["version"].is_string());
    assert_eq!(state["connected"], true);
    assert_eq!(state["busy"], false);
    assert_eq!(state["selectedRootKind"], "emoji");
    assert_eq!(state["settings"]["updateCheck"], true);
    assert_eq!(
        state["counts"],
        json!({"stickers":7, "stickersMax":18, "emojis":7, "emojisMax":18})
    );
    assert!(state["user"]["displayName"].is_string());
    let listing = get(&app, "/api/local/assets").await;
    assert_eq!(listing["root"], state["localRoot"]);
    assert_eq!(listing["totalCount"], 16);
    assert_eq!(listing["folders"][0]["name"], "All");
    assert_eq!(listing["folders"][0]["directCount"], 3);
    assert_eq!(listing["folders"][0]["totalCount"], 16);
    let first = &listing["assets"][0];
    for field in [
        "id",
        "name",
        "path",
        "relativeDir",
        "bucket",
        "ext",
        "sizeBytes",
        "sizeText",
        "modifiedMs",
        "width",
        "height",
        "isAnimated",
        "isGif",
        "isSpriteSheet",
        "frames",
        "fps",
        "animationStyle",
        "loopStyle",
        "thumbUrl",
        "previewUrl",
        "spriteSheetUrl",
    ] {
        assert!(first.get(field).is_some(), "missing {field}");
    }
    let items = get(&app, "/api/remote/items").await;
    assert_eq!(items["stickersCount"], 7);
    assert_eq!(items["emojisCount"], 7);
    assert_eq!(items["items"].as_array().unwrap().len(), 14);
    let last = &items["items"][13];
    for field in [
        "id",
        "name",
        "tag",
        "createdAt",
        "status",
        "frames",
        "fps",
        "animationStyle",
        "loopStyle",
        "thumbUrl",
        "previewUrl",
    ] {
        assert!(last.get(field).is_some(), "missing {field}");
    }
}

#[tokio::test]
async fn lists_assets_by_folder_by_search_and_with_or_without_descendants() {
    let app = app(Mode::Full);
    let direct = get(
        &app,
        "/api/local/assets?folder=Favorites&includeSubfolders=false",
    )
    .await;
    assert_eq!(direct["assets"].as_array().unwrap().len(), 2);
    let nested = get(
        &app,
        "/api/local/assets?folder=favorites&includeSubfolders=true",
    )
    .await;
    assert_eq!(nested["assets"].as_array().unwrap().len(), 4);
    let searched = get(
        &app,
        "/api/local/assets?folder=Favorites&query=cLoud&includeSubfolders=true",
    )
    .await;
    assert_eq!(searched["assets"].as_array().unwrap().len(), 1);
    assert_eq!(searched["assets"][0]["name"], "Cloud Garden.png");
    let state = post(&app, "/api/settings", json!({"includeSubfolders": false})).await;
    assert_eq!(state["includeSubfolders"], false);
    let listing = get(&app, "/api/local/assets?folder=Favorites").await;
    assert_eq!(listing["assets"].as_array().unwrap().len(), 2);
    let response = request(
        &app,
        "GET",
        &format!("/api/local/assets?query={}", "x".repeat(1025)),
        None,
    )
    .await;
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
}

#[tokio::test]
async fn local_assets_serve_gif_png_preview_metadata_and_individual_frames() {
    let app = app(Mode::Full);
    let listing = get(&app, "/api/local/assets").await;
    let assets = listing["assets"].as_array().unwrap();
    let gif = assets.iter().find(|asset| asset["isGif"] == true).unwrap();
    for field in ["thumbUrl", "previewUrl"] {
        let response = request(&app, "GET", gif[field].as_str().unwrap(), None).await;
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(response.headers()[header::CONTENT_TYPE], "image/gif");
        assert_eq!(
            image::guess_format(
                &to_bytes(response.into_body(), 10 * 1024 * 1024)
                    .await
                    .unwrap()
            )
            .unwrap(),
            ImageFormat::Gif
        );
    }
    let sprite = assets
        .iter()
        .find(|asset| asset["isSpriteSheet"] == true)
        .unwrap();
    let sprite_url = sprite["spriteSheetUrl"].as_str().unwrap();
    let metadata = get(&app, sprite_url).await;
    assert_eq!(metadata["frames"], 9);
    assert_eq!(metadata["fps"], 12);
    assert_eq!(metadata["cols"], 4);
    assert_eq!(metadata["rows"], 4);
    let response = request(&app, "GET", metadata["imageUrl"].as_str().unwrap(), None).await;
    assert_eq!(response.headers()[header::CONTENT_TYPE], "image/png");
    assert_eq!(
        image::load_from_memory(&to_bytes(response.into_body(), 1024 * 1024).await.unwrap())
            .unwrap()
            .dimensions(),
        (384, 384)
    );
    for uri in [
        sprite["thumbUrl"].as_str().unwrap().to_owned(),
        format!("{sprite_url}?frame=0"),
    ] {
        let frame = request(&app, "GET", &uri, None).await;
        assert_eq!(frame.headers()[header::CONTENT_TYPE], "image/png");
        assert_eq!(
            image::load_from_memory(&to_bytes(frame.into_body(), 1024 * 1024).await.unwrap())
                .unwrap()
                .dimensions(),
            (96, 96)
        );
    }
    assert_eq!(
        request(&app, "GET", &format!("{sprite_url}?frame=10"), None)
            .await
            .status(),
        StatusCode::NOT_FOUND
    );
    assert_eq!(
        request(&app, "GET", "/api/local/thumb/not-present", None)
            .await
            .status(),
        StatusCode::NOT_FOUND
    );
}

#[tokio::test]
async fn selects_and_refreshes_photos_with_inventory_folders_excluded() {
    let app = app(Mode::Full);
    let state = post(
        &app,
        "/api/local/select-root",
        json!({"kind":"photos", "includeSubfolders":false}),
    )
    .await;
    assert_eq!(state["selectedRootKind"], "photos");
    assert_eq!(state["includeSubfolders"], false);
    assert_eq!(state["localRoot"], state["knownFolders"]["photos"]);
    assert_eq!(get(&app, "/api/local/assets").await["totalCount"], 2);
    post(&app, "/api/local/refresh", json!({})).await;
    assert_eq!(get(&app, "/api/local/assets").await["totalCount"], 2);
    let browse = post(&app, "/api/local/browse", json!({})).await;
    assert_eq!(browse["cancelled"], false);
    assert_eq!(browse["selectedRootKind"], "custom");
    assert_eq!(browse["path"], state["knownFolders"]["photos"]);
}

#[tokio::test]
async fn imports_dropped_paths_and_preserves_files_across_refresh() {
    let app = app(Mode::Empty);
    let root = tempfile::tempdir().unwrap();
    let folder = root.path().join("Dropped");
    std::fs::create_dir(&folder).unwrap();
    let loose = root.path().join("loose.png");
    for path in [folder.join("inside.png"), loose.clone()] {
        let img = image::RgbaImage::from_pixel(4, 4, image::Rgba([255, 10, 100, 255]));
        img.save(path).unwrap();
    }
    let json = post(
        &app,
        "/api/local/import-paths",
        json!({"paths":[folder, loose]}),
    )
    .await;
    assert_eq!(json["selectedRootKind"], "custom");
    assert_eq!(json["localRoot"], folder.to_string_lossy().as_ref());
    assert_eq!(get(&app, "/api/local/assets").await["totalCount"], 2);
    post(&app, "/api/local/refresh", json!({})).await;
    assert_eq!(get(&app, "/api/local/assets").await["totalCount"], 2);
    let invalid = request(
        &app,
        "POST",
        "/api/local/import-paths",
        Some(json!({"paths":["relative.png"]})),
    )
    .await;
    assert_eq!(invalid.status(), StatusCode::BAD_REQUEST);
    let oversized = request(
        &app,
        "POST",
        "/api/local/import-paths",
        Some(json!({"paths": vec!["file";257]})),
    )
    .await;
    assert_eq!(oversized.status(), StatusCode::BAD_REQUEST);
}

#[tokio::test]
async fn mock_upload_exports_preview_increments_count_and_can_be_deleted() {
    let app = app(Mode::Empty);
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("Upload_6frames_15fps.png");
    image::RgbaImage::from_pixel(32, 32, image::Rgba([40, 80, 120, 255]))
        .save(&path)
        .unwrap();
    post(&app, "/api/local/import-paths", json!({"paths":[path]})).await;
    let library = get(&app, "/api/local/assets").await;
    let asset = &library["assets"][0];
    let rejected = request(
        &app,
        "POST",
        "/api/upload",
        Some(json!({"assetId":asset["id"], "kind":"emoji"})),
    )
    .await;
    assert_eq!(rejected.status(), StatusCode::BAD_REQUEST);
    let uploaded = post(
        &app,
        "/api/upload",
        json!({"assetId":asset["id"], "kind":"emojianimated",
         "animationStyle":"confetti", "loopStyle":"pingpong", "fpsOverride":22}),
    )
    .await;
    assert_eq!(uploaded["tag"], "emojianimated");
    post(&app, "/api/remote/refresh", json!({})).await;
    post(&app, "/api/remote/reconnect", json!({})).await;
    let remote = get(&app, "/api/remote/items").await;
    assert_eq!(remote["emojisCount"], 1);
    let item = &remote["items"][0];
    assert_eq!(item["id"], uploaded["id"]);
    assert_eq!(item["frames"], 6);
    assert_eq!(item["fps"], 22);
    assert_eq!(item["animationStyle"], "confetti");
    assert_eq!(item["loopStyle"], "pingpong");
    for (kind, size) in [("thumbUrl", 8), ("previewUrl", 32)] {
        let response = request(&app, "GET", item[kind].as_str().unwrap(), None).await;
        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(response.headers()[header::CONTENT_TYPE], "image/png");
        assert_eq!(
            image::load_from_memory(
                &to_bytes(response.into_body(), 10 * 1024 * 1024)
                    .await
                    .unwrap()
            )
            .unwrap()
            .dimensions(),
            (size, size)
        );
    }
    let deleted = request(
        &app,
        "DELETE",
        &format!("/api/remote/{}", item["id"].as_str().unwrap()),
        None,
    )
    .await;
    assert_eq!(deleted.status(), StatusCode::OK);
    assert_eq!(get(&app, "/api/remote/items").await["emojisCount"], 0);
}

#[tokio::test]
async fn limit_fixture_counts_static_and_animated_emoji_together() {
    let app = app(Mode::Limit);
    let remote = get(&app, "/api/remote/items").await;
    assert_eq!(remote["stickersCount"], 18);
    assert_eq!(remote["emojisCount"], 18);
    let asset = get(&app, "/api/local/assets").await["assets"][0]["id"].clone();
    for kind in ["sticker", "emoji", "emojianimated"] {
        let response = request(
            &app,
            "POST",
            "/api/upload",
            Some(json!({"kind":kind,"assetId":asset})),
        )
        .await;
        assert_eq!(response.status(), StatusCode::CONFLICT);
    }
}

#[tokio::test]
async fn loading_and_error_fixtures_expose_expected_action_guards() {
    let loading = app(Mode::Loading);
    let state = get(&loading, "/api/state").await;
    assert_eq!(state["busy"], true);
    assert!(state["busyMessage"].is_string());
    for route in [
        "/api/local/refresh",
        "/api/remote/refresh",
        "/api/remote/reconnect",
    ] {
        let response = request(&loading, "POST", route, Some(json!({}))).await;
        assert_eq!(response.status(), StatusCode::CONFLICT);
    }

    let error = app(Mode::Error);
    let state = get(&error, "/api/state").await;
    assert_eq!(state["connected"], false);
    assert!(state["authError"].is_string());
    let remote = get(&error, "/api/remote/items").await;
    assert_eq!(remote["connected"], false);
    assert_eq!(remote["items"], json!([]));
    let asset = get(&error, "/api/local/assets").await["assets"][0]["id"].clone();
    let upload = request(
        &error,
        "POST",
        "/api/upload",
        Some(json!({"kind":"sticker", "assetId":asset})),
    )
    .await;
    assert_eq!(upload.status(), StatusCode::CONFLICT);
}

#[tokio::test]
async fn supports_update_check_skip_later_mock_install_and_nullable_settings() {
    let app = app(Mode::Update);
    let state = get(&app, "/api/state").await;
    let tag = state["updateOffer"]["tag"].clone();
    assert!(tag.is_string());
    let state = post(&app, "/api/update/action", json!({"action":"later"})).await;
    assert!(state["updateOffer"].is_null());
    let state = post(&app, "/api/update/action", json!({"action":"check"})).await;
    assert_eq!(state["updateOffer"]["tag"], tag);
    let state = post(&app, "/api/update/action", json!({"action":"skip"})).await;
    assert_eq!(state["settings"]["skippedUpdate"], tag);
    assert!(state["updateOffer"].is_null());
    let state = post(
        &app,
        "/api/settings",
        json!({"updateCheck":false, "skippedUpdate":null}),
    )
    .await;
    assert_eq!(state["settings"]["updateCheck"], false);
    assert!(state["settings"]["skippedUpdate"].is_null());
    post(&app, "/api/update/action", json!({"action":"check"})).await;
    let state = post(&app, "/api/update/action", json!({"action":"install"})).await;
    assert!(state["updateOffer"].is_null());
    assert_eq!(state["busy"], false);
}

#[tokio::test]
async fn only_opens_allowlisted_links_and_indexed_paths() {
    let app = app(Mode::Empty);
    let state = get(&app, "/api/state").await;
    assert_eq!(
        post(
            &app,
            "/api/system/open-external",
            json!({"revealPath":state["localRoot"]})
        )
        .await["ok"],
        true
    );
    let page = "https://github.com/RealWhyKnot/VRCInventoryManager/releases/tag/v2026.12.1.0";
    assert_eq!(
        post(&app, "/api/system/open-external", json!({ "url": page })).await["ok"],
        true
    );
    for body in [
        json!({"url":"http://github.com/RealWhyKnot/VRCInventoryManager"}),
        json!({"url":"https://github.com.evil.example/RealWhyKnot/VRCInventoryManager/releases"}),
        json!({"url":"https://other@github.com/RealWhyKnot/VRCInventoryManager/releases"}),
        json!({"url":"https://github.com/RealWhyKnot/VRCInventoryManager/../other"}),
        json!({"revealPath":std::env::temp_dir()}),
    ] {
        let response = request(&app, "POST", "/api/system/open-external", Some(body)).await;
        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
    }
}

#[tokio::test]
async fn rejects_external_web_origins_and_untrusted_host_headers() {
    let app = app(Mode::Empty);
    for (host, origin) in [
        ("evil.example:47655", None),
        ("127.0.0.1:47655", Some("https://evil.example")),
        ("127.0.0.1:47655", Some("http://127.0.0.1:47656")),
    ] {
        let mut req = Request::builder()
            .uri("/api/state")
            .header(header::HOST, host);
        if let Some(origin) = origin {
            req = req.header(header::ORIGIN, origin);
        }
        let response = app
            .clone()
            .oneshot(req.body(Body::empty()).unwrap())
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::FORBIDDEN);
    }
}

#[tokio::test]
async fn streams_ready_and_mutation_events() {
    let app = app(Mode::Empty);
    let response = request(&app, "GET", "/api/events", None).await;
    assert_eq!(
        response.headers()[header::CONTENT_TYPE],
        "text/event-stream"
    );
    let mut stream = response.into_body().into_data_stream();
    let initial = stream.next().await.unwrap().unwrap();
    assert!(String::from_utf8_lossy(&initial).contains(r#"{"type":"ready"}"#));
    post(&app, "/api/local/refresh", json!({})).await;
    let result = tokio::time::timeout(Duration::from_secs(2), async {
        while let Some(Ok(bytes)) = stream.next().await {
            if String::from_utf8_lossy(&bytes).contains(r#"{"type":"local"}"#) {
                return true;
            }
        }
        false
    })
    .await
    .unwrap();
    assert!(result);
}

#[tokio::test]
async fn serves_the_frontend_entry_and_rejects_traversal_paths() {
    let app = app(Mode::Empty);
    let response = request(&app, "GET", "/", None).await;
    assert_eq!(response.status(), StatusCode::OK);
    assert!(
        response.headers()[header::CONTENT_TYPE]
            .to_str()
            .unwrap()
            .starts_with("text/html")
    );
    assert!(
        String::from_utf8_lossy(
            &to_bytes(response.into_body(), 2 * 1024 * 1024)
                .await
                .unwrap()
        )
        .contains("VRCInventoryManager")
    );
    assert_eq!(
        request(&app, "GET", "/%2e%2e/Cargo.toml", None)
            .await
            .status(),
        StatusCode::NOT_FOUND
    );
    assert_eq!(
        request(&app, "GET", "/api/missing", None).await.status(),
        StatusCode::NOT_FOUND
    );
}

#[tokio::test]
async fn remote_mutations_are_exclusive() {
    let state = AppState::new(Some(Mode::Empty), PathBuf::from(OsStr::new("fixture.exe"))).unwrap();
    let app = routes::router(Arc::clone(&state));
    let held = state.begin("Test operation").unwrap();
    assert!(get(&app, "/api/state").await["busy"].as_bool().unwrap());
    let response = request(&app, "POST", "/api/remote/refresh", Some(json!({}))).await;
    assert_eq!(response.status(), StatusCode::CONFLICT);
    drop(held);
    let snapshot = post(&app, "/api/remote/refresh", json!({})).await;
    assert_eq!(snapshot["busy"], false);
}
