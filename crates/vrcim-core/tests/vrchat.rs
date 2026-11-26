use std::sync::{Arc, Mutex};

use axum::{
    Json, Router,
    body::to_bytes,
    extract::{Path, Query, Request, State},
    http::{HeaderMap, StatusCode},
    response::IntoResponse,
    routing::{delete, get, post},
};
use serde::Deserialize;
use serde_json::{Value, json};
use tokio::{net::TcpListener, task::JoinHandle};
use url::Url;
use vrcim_core::{
    vrchat_api::{ANIMATED, EMOJI, RemoteItem, STICKER, UploadPayload, VrchatApi, counts},
    vrcx_cookies::{Cookies, Session},
};

#[derive(Default)]
struct Requests {
    routes: Vec<String>,
    queries: Vec<(String, usize)>,
    uploads: Vec<String>,
    cookie: Option<String>,
    agent: Option<String>,
}

struct TestServer {
    url: Url,
    requests: Arc<Mutex<Requests>>,
    server: JoinHandle<std::io::Result<()>>,
}

#[derive(Deserialize)]
struct FilesQuery {
    n: usize,
    offset: usize,
    tag: String,
}

async fn user(State(requests): State<Arc<Mutex<Requests>>>, headers: HeaderMap) -> Json<Value> {
    let cookie = headers
        .get("cookie")
        .map(|header| header.to_str().expect("the cookie header is ASCII").into());
    let agent = headers
        .get("user-agent")
        .map(|header| header.to_str().expect("the user agent is ASCII").into());
    let mut requests = requests.lock().expect("no test handler panicked");
    requests.routes.push("auth/user".into());
    requests.cookie = cookie;
    requests.agent = agent;
    drop(requests);
    Json(json!({"id":"usr_amanda","displayName":"Amanda"}))
}

async fn config(State(requests): State<Arc<Mutex<Requests>>>) -> Json<Value> {
    requests
        .lock()
        .expect("no test handler panicked")
        .routes
        .push("config".into());
    Json(json!({}))
}

async fn files(
    State(requests): State<Arc<Mutex<Requests>>>,
    Query(query): Query<FilesQuery>,
) -> impl IntoResponse {
    assert_eq!(query.n, 100);
    requests
        .lock()
        .expect("no test handler panicked")
        .queries
        .push((query.tag.clone(), query.offset));
    if query.tag == "fail" {
        return (StatusCode::BAD_REQUEST, "x".repeat(600)).into_response();
    }
    let count = match (query.tag.as_str(), query.offset) {
        ("paged", 0..200) | ("endless", _) => 100,
        ("paged", 200) => 3,
        (_, 0) => 1,
        _ => 0,
    };
    let items: Vec<_> = (0..count)
        .map(|index| {
            json!({
                "id": format!("file_{}_{}", query.tag, query.offset + index),
                "name": "Test Upload", "tags":["ignored",query.tag],
                "versions": [
                    {"created_at": "2026-10-08T01:00:00Z", "status": "waiting"},
                    {
                        "created_at": "2026-10-09T01:00:00Z",
                        "file": {"url": "https://files.example/latest.png"},
                        "status": "complete"
                    }
                ]
            })
        })
        .collect();
    Json(items).into_response()
}

async fn upload(State(requests): State<Arc<Mutex<Requests>>>, request: Request) -> Json<Value> {
    let bytes = to_bytes(request.into_body(), 20 * 1024 * 1024)
        .await
        .expect("no test handler panicked");
    requests
        .lock()
        .expect("no test handler panicked")
        .uploads
        .push(String::from_utf8_lossy(&bytes).into_owned());
    Json(json!({"id":"file_uploaded"}))
}

async fn delete_file(
    State(requests): State<Arc<Mutex<Requests>>>,
    Path(id): Path<String>,
) -> StatusCode {
    requests
        .lock()
        .expect("no test handler panicked")
        .routes
        .push(format!("delete/{id}"));
    StatusCode::NO_CONTENT
}

impl TestServer {
    async fn start() -> Self {
        let requests = Arc::new(Mutex::new(Requests::default()));
        let router = Router::new()
            .route("/api/1/auth/user", get(user))
            .route("/api/1/config", get(config))
            .route("/api/1/files", get(files))
            .route("/api/1/file/image", post(upload))
            .route("/api/1/file/{id}", delete(delete_file))
            .with_state(Arc::clone(&requests));
        let listener = TcpListener::bind("127.0.0.1:0")
            .await
            .expect("the test server starts");
        let url = Url::parse(&format!(
            "http://{}/api/1/",
            listener.local_addr().expect("the test server starts")
        ))
        .expect("the test server starts");
        let server = tokio::spawn(async move { axum::serve(listener, router).await });
        Self {
            url,
            requests,
            server,
        }
    }

    fn client(&self) -> VrchatApi {
        let session = Session {
            cookies: Cookies {
                auth: "auth_cookie".into(),
                two_factor_auth: "factor_cookie".into(),
            },
            user_agent: "TestVRCX/1".into(),
        };
        VrchatApi::with_base(session, self.url.clone()).expect("the test client is created")
    }
}

impl Drop for TestServer {
    fn drop(&mut self) {
        self.server.abort();
    }
}

#[test]
fn first_recognized_tag_and_created_at_last_status_and_file_win() {
    let item = RemoteItem::from_json(
        &json!({
            "tags": ["custom", ANIMATED, STICKER],
            "versions": [
                {"created_at":"invalid","status":"waiting","file":{"url":"earliest"}},
                {
                    "created_at": "2026-01-01T00:00:00Z",
                    "status": "complete",
                    "file": {"url": "latest-valid"}
                },
                {"created_at":"2026-01-02T00:00:00Z","status":null,"file":{"url":false}},
            ],
            "frames": 8,
            "framesOverTime": 24,
            "maskTag": "square",
            "loopStyle": "pingpong",
            "mimeType": "image/png"
        }),
        EMOJI,
    );
    assert_eq!(item.tag, ANIMATED);
    assert_eq!(item.created_at.unwrap().to_string(), "2026-01-01T00:00:00Z");
    assert_eq!(item.status, "complete");
    assert_eq!(item.preview_url, "latest-valid");
    assert_eq!((item.frames, item.fps), (Some(8), Some(24)));
    assert_eq!(item.mask_tag, "square");
    assert_eq!(item.loop_style, "pingpong");
    assert_eq!(item.mime_type, "image/png");
    assert_eq!(
        RemoteItem::from_json(&json!({"tags":["unknown"]}), EMOJI).tag,
        EMOJI
    );
}

#[test]
fn formats_remote_summary_without_missing_metadata() {
    let item = RemoteItem {
        tag: EMOJI.into(),
        ..RemoteItem::default()
    };
    assert_eq!(item.summary(), "Emoji - static");
    let item = RemoteItem {
        tag: ANIMATED.into(),
        ..RemoteItem::default()
    };
    assert_eq!(item.summary(), "Animated Emoji");
    let item = RemoteItem {
        tag: ANIMATED.into(),
        animation_style: "snow".into(),
        frames: Some(64),
        fps: Some(24),
        ..RemoteItem::default()
    };
    assert_eq!(item.summary(), "Animated Emoji - snow - 64 frames @ 24 fps");
}

#[tokio::test]
async fn requests_auth_config_inventory_and_delete_with_credentials() {
    let server = TestServer::start().await;
    let api = server.client();
    let user = api.user().await.unwrap();
    assert_eq!(user.id, "usr_amanda");
    assert_eq!(user.display_name, "Amanda");
    api.fetch_config().await.unwrap();
    let inventory = api.inventory().await.unwrap();
    assert_eq!(counts(&inventory), (1, 2));
    assert_eq!(inventory[0].id, "file_emoji_0");
    assert_eq!(inventory[0].preview_url, "https://files.example/latest.png");
    api.delete("file_uploaded").await.unwrap();
    api.delete("file with/?").await.unwrap();
    let requests = server.requests.lock().unwrap();
    assert_eq!(
        requests.cookie.as_deref(),
        Some("auth=auth_cookie; twoFactorAuth=factor_cookie")
    );
    assert_eq!(requests.agent.as_deref(), Some("TestVRCX/1"));
    assert_eq!(
        requests.routes,
        [
            "auth/user",
            "config",
            "delete/file_uploaded",
            "delete/file with/?"
        ]
    );
}

#[tokio::test]
async fn paginates_inventory_and_caps_endless_results() {
    let server = TestServer::start().await;
    let api = server.client();
    let items = api.list_files("paged").await.unwrap();
    assert_eq!(items.len(), 203);
    assert_eq!(items.last().unwrap().id, "file_paged_202");
    assert!(
        api.list_files("endless")
            .await
            .unwrap_err()
            .to_string()
            .contains("too many endless files")
    );
    let requests = server.requests.lock().unwrap();
    assert_eq!(
        requests.queries[0..3],
        [
            ("paged".into(), 0),
            ("paged".into(), 100),
            ("paged".into(), 200)
        ]
    );
    assert_eq!(requests.queries.len(), 13);
    assert_eq!(requests.queries.last().unwrap(), &("endless".into(), 900));
}

#[tokio::test]
async fn formats_http_errors_without_echoing_the_response_body() {
    let server = TestServer::start().await;
    let error = server
        .client()
        .list_files("fail")
        .await
        .unwrap_err()
        .to_string();
    assert_eq!(error, "List fail failed (HTTP 400).");
}

#[tokio::test]
async fn sends_the_correct_static_and_animated_multipart_fields() {
    let server = TestServer::start().await;
    let api = server.client();
    let png = vec![0x89, b'P', b'N', b'G'];
    let static_payload = UploadPayload {
        tag: EMOJI,
        png: png.clone(),
        animation_style: Some("snow"),
        loop_style: None,
        frames: None,
        fps: None,
    };
    let animated = UploadPayload {
        tag: ANIMATED,
        png: png.clone(),
        animation_style: Some("spin"),
        loop_style: Some("pingpong"),
        frames: Some(64),
        fps: Some(24),
    };
    let sticker = UploadPayload {
        tag: STICKER,
        png,
        animation_style: None,
        loop_style: None,
        frames: None,
        fps: None,
    };
    for payload in [static_payload, animated, sticker] {
        assert_eq!(api.upload(payload).await.unwrap(), "file_uploaded");
    }
    let requests = server.requests.lock().unwrap();
    assert_eq!(requests.uploads.len(), 3);
    let plain = &requests.uploads[0];
    assert!(plain.contains("name=\"tag\"\r\n\r\nemoji"));
    assert!(plain.contains("name=\"animationStyle\"\r\n\r\nsnow"));
    assert!(!plain.contains("name=\"frames"));
    assert!(!plain.contains("name=\"loopStyle\""));
    let animated = &requests.uploads[1];
    for (field, value) in [
        ("tag", ANIMATED),
        ("frames", "64"),
        ("framesOverTime", "24"),
        ("loopStyle", "pingpong"),
    ] {
        assert!(animated.contains(&format!("name=\"{field}\"\r\n\r\n{value}")));
    }
    assert!(!requests.uploads[2].contains("name=\"animationStyle\""));
    for body in &requests.uploads {
        assert!(body.contains("name=\"maskTag\"\r\n\r\nsquare"));
        assert!(body.contains("name=\"file\"; filename=\"blob\""));
        assert!(
            body.to_ascii_lowercase()
                .contains("content-type: image/png")
        );
    }
}

#[tokio::test]
async fn dropping_unpolled_inventory_request_sends_nothing() {
    let server = TestServer::start().await;
    let api = server.client();
    drop(api.inventory());
    api.user().await.unwrap();
    let requests = server.requests.lock().unwrap();
    assert_eq!(requests.routes, ["auth/user"]);
    assert_eq!(requests.queries, Vec::<(String, usize)>::new());
}
