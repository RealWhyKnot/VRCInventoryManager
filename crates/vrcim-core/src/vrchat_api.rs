use std::{collections::HashSet, fmt, path::Path, time::Duration};

use anyhow::{Context, Result, bail, ensure};
use jiff::Timestamp;
use reqwest::{
    Client, Method, RequestBuilder, Response, StatusCode,
    multipart::{Form, Part},
};
use serde::Serialize;
use serde_json::Value;
use url::Url;

use crate::{
    image_pipeline::{self, MAX_FRAMES},
    local_assets::{ANIMATION_STYLES, LOOP_STYLES, LocalAsset},
    vrcx_cookies::Session,
};

pub const INVENTORY_LIMIT: usize = 18;
pub const STICKER: &str = "sticker";
pub const EMOJI: &str = "emoji";
pub const ANIMATED: &str = "emojianimated";
pub const TAGS: [&str; 3] = [STICKER, EMOJI, ANIMATED];

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct User {
    pub id: String,
    pub display_name: String,
}

#[derive(Clone, Debug, Default)]
pub struct RemoteItem {
    pub id: String,
    pub name: String,
    pub tag: String,
    pub mime_type: String,
    pub created_at: Option<Timestamp>,
    pub status: String,
    pub frames: Option<u32>,
    pub fps: Option<u32>,
    pub animation_style: String,
    pub loop_style: String,
    pub mask_tag: String,
    pub preview_url: String,
}

impl RemoteItem {
    pub fn from_json(value: &Value, fallback_tag: &str) -> Self {
        let tag = value["tags"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .find(|tag| TAGS.contains(tag))
            .unwrap_or(fallback_tag)
            .to_owned();
        let mut created_at = None;
        let mut status = "";
        let mut preview_url = "";
        for version in value["versions"].as_array().into_iter().flatten() {
            if created_at.is_none() {
                created_at = version["created_at"]
                    .as_str()
                    .and_then(|text| text.parse().ok());
            }
            if let Some(text) = version["status"].as_str() {
                status = text;
            }
            if let Some(text) = version["file"]["url"].as_str() {
                preview_url = text;
            }
        }
        let number = |key: &str| {
            value[key]
                .as_u64()
                .and_then(|number| u32::try_from(number).ok())
        };
        Self {
            id: field(value, "id"),
            name: field(value, "name"),
            tag,
            mime_type: field(value, "mimeType"),
            created_at,
            status: status.into(),
            frames: number("frames"),
            fps: number("framesOverTime"),
            animation_style: field(value, "animationStyle"),
            loop_style: field(value, "loopStyle"),
            mask_tag: field(value, "maskTag"),
            preview_url: preview_url.into(),
        }
    }

    #[must_use]
    pub fn summary(&self) -> String {
        let mut parts = vec![match self.tag.as_str() {
            STICKER => "Sticker".to_owned(),
            EMOJI => "Emoji".to_owned(),
            ANIMATED => "Animated Emoji".to_owned(),
            _ => self.tag.clone(),
        }];
        if !self.animation_style.trim().is_empty() {
            parts.push(self.animation_style.clone());
        }
        if let (Some(frames @ 1..), Some(fps @ 1..)) = (self.frames, self.fps) {
            parts.push(format!("{frames} frames @ {fps} fps"));
        } else if self.tag != ANIMATED {
            parts.push("static".into());
        }
        parts.join(" - ")
    }
}

fn field(value: &Value, key: &str) -> String {
    value[key].as_str().unwrap_or_default().to_owned()
}

pub fn sort_items(items: &mut [RemoteItem]) {
    items.sort_by(|left, right| {
        right
            .created_at
            .cmp(&left.created_at)
            .then_with(|| left.id.cmp(&right.id))
    });
}

#[must_use]
pub fn counts(items: &[RemoteItem]) -> (usize, usize) {
    (
        items.iter().filter(|item| item.tag == STICKER).count(),
        items
            .iter()
            .filter(|item| matches!(item.tag.as_str(), EMOJI | ANIMATED))
            .count(),
    )
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum UploadKind {
    Sticker,
    Emoji,
    Animated,
    AutoEmoji,
}

pub struct UploadPayload {
    pub tag: &'static str,
    pub png: Vec<u8>,
    pub animation_style: Option<&'static str>,
    pub loop_style: Option<&'static str>,
    pub frames: Option<u32>,
    pub fps: Option<u32>,
}

impl UploadPayload {
    pub fn prepare(
        asset: &LocalAsset,
        kind: UploadKind,
        animation: Option<&str>,
        loop_style: Option<&str>,
        fps: Option<u32>,
    ) -> Result<Self> {
        let kind = match kind {
            UploadKind::AutoEmoji if asset.is_animated() => UploadKind::Animated,
            UploadKind::AutoEmoji => UploadKind::Emoji,
            kind => kind,
        };
        let resolve_style =
            |value: Option<&str>, styles: &[&'static str], default: &'static str| {
                value.map_or(Ok(default), |value| {
                    styles
                        .iter()
                        .copied()
                        .find(|style| style.eq_ignore_ascii_case(value))
                        .context("Unknown animation or loop style.")
                })
            };
        let animation_style = if kind == UploadKind::Sticker {
            None
        } else {
            Some(resolve_style(
                animation,
                &ANIMATION_STYLES,
                asset.metadata.animation_style,
            )?)
        };
        let (tag, png, frames, fps, loop_style) = match kind {
            UploadKind::Sticker => (
                STICKER,
                image_pipeline::png_payload(&asset.path, false)?,
                None,
                None,
                None,
            ),
            UploadKind::Emoji | UploadKind::AutoEmoji => {
                ensure!(
                    !asset.is_animated(),
                    "Animated emoji sources must be uploaded with Upload Animated Emoji."
                );
                (
                    EMOJI,
                    image_pipeline::png_payload(&asset.path, false)?,
                    None,
                    None,
                    None,
                )
            }
            UploadKind::Animated => {
                let (png, frames, source_fps) = if asset.is_gif() {
                    let sheet = image_pipeline::gif_sprite_sheet(&asset.path)?;
                    (sheet.png, sheet.frames, sheet.fps)
                } else {
                    let frames = asset.metadata.frames.unwrap_or(0);
                    let fps = asset.metadata.fps.unwrap_or(0);
                    ensure!(
                        frames > 1,
                        "Animated emoji upload requires at least two frames."
                    );
                    ensure!(
                        frames <= MAX_FRAMES,
                        "Animated emoji upload supports at most 64 frames."
                    );
                    ensure!(
                        fps > 0,
                        "Animated emoji upload requires a positive frame rate."
                    );
                    (image_pipeline::png_payload(&asset.path, true)?, frames, fps)
                };
                let fps = fps.unwrap_or(source_fps);
                ensure!(
                    fps > 0,
                    "Animated emoji upload requires a positive frame rate."
                );
                (
                    ANIMATED,
                    png,
                    Some(frames),
                    Some(fps.min(64)),
                    Some(resolve_style(
                        loop_style,
                        &LOOP_STYLES,
                        asset.metadata.loop_style,
                    )?),
                )
            }
        };
        Ok(Self {
            tag,
            png,
            animation_style,
            loop_style,
            frames,
            fps,
        })
    }

    pub fn for_path(path: &Path, kind: UploadKind) -> Result<Self> {
        let asset = LocalAsset::read(path, path.parent().unwrap_or(Path::new("")))
            .context("Choose a supported image file.")?;
        Self::prepare(&asset, kind, None, None, None)
    }

    fn multipart(self) -> Result<Form> {
        let mut form = Form::new().text("tag", self.tag).text("maskTag", "square");
        if let Some(style) = self.animation_style {
            form = form.text("animationStyle", style);
        }
        if let Some(frames) = self.frames {
            form = form.text("frames", frames.to_string());
        }
        if let Some(fps) = self.fps {
            form = form.text("framesOverTime", fps.to_string());
        }
        if let Some(style) = self.loop_style {
            form = form.text("loopStyle", style);
        }
        Ok(form.part(
            "file",
            Part::bytes(self.png)
                .file_name("blob")
                .mime_str("image/png")?,
        ))
    }
}

#[derive(Clone)]
pub struct VrchatApi {
    http: Client,
    assets: Client,
    base: Url,
    cookie: reqwest::header::HeaderValue,
    user_agent: String,
}

#[derive(Debug)]
pub struct HttpError {
    action: String,
    pub status: StatusCode,
}

impl fmt::Display for HttpError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        let detail = match self.status {
            StatusCode::UNAUTHORIZED => "Sign in to VRChat in VRCX, then reconnect.",
            StatusCode::FORBIDDEN => "Check your VRChat sign-in and try again.",
            StatusCode::TOO_MANY_REQUESTS => "Wait a moment and try again.",
            StatusCode::NOT_FOUND => "Refresh the inventory and try again.",
            _ => "",
        };
        write!(
            formatter,
            "{} failed (HTTP {}).",
            self.action,
            self.status.as_u16()
        )?;
        if !detail.is_empty() {
            write!(formatter, " {detail}")?;
        }
        Ok(())
    }
}

impl std::error::Error for HttpError {}

impl VrchatApi {
    pub fn new(session: Session) -> Result<Self> {
        Self::with_base(session, Url::parse("https://api.vrchat.cloud/api/1/")?)
    }

    pub fn with_base(session: Session, base: Url) -> Result<Self> {
        let mut cookie = reqwest::header::HeaderValue::from_str(&session.cookies.header())?;
        cookie.set_sensitive(true);
        let http = Client::builder()
            .timeout(Duration::from_secs(30))
            .redirect(reqwest::redirect::Policy::none())
            .build()?;
        let assets = Client::builder()
            .timeout(Duration::from_secs(30))
            .redirect(reqwest::redirect::Policy::custom(|attempt| {
                if attempt.previous().len() >= 8 || !is_vrchat_image_url(attempt.url()) {
                    attempt.stop()
                } else {
                    attempt.follow()
                }
            }))
            .build()?;
        Ok(Self {
            http,
            assets,
            base,
            cookie,
            user_agent: session.user_agent,
        })
    }

    fn request(&self, method: Method, path: &str) -> Result<RequestBuilder> {
        Ok(self
            .http
            .request(method, self.base.join(path)?)
            .header(reqwest::header::USER_AGENT, &self.user_agent)
            .header(reqwest::header::COOKIE, self.cookie.clone())
            .header(reqwest::header::ACCEPT, "application/json"))
    }

    pub async fn user(&self) -> Result<User> {
        let value: Value = check(
            self.request(Method::GET, "auth/user")?.send().await?,
            "Check auth",
        )?
        .json()
        .await?;
        let id = field(&value, "id");
        ensure!(
            !id.is_empty(),
            "Sign in to VRChat through VRCX, then reconnect."
        );
        Ok(User {
            id,
            display_name: field(&value, "displayName"),
        })
    }

    pub async fn fetch_config(&self) -> Result<()> {
        check(
            self.request(Method::GET, "config")?.send().await?,
            "Fetch config",
        )?;
        Ok(())
    }

    pub async fn list_files(&self, tag: &str) -> Result<Vec<RemoteItem>> {
        let mut items = Vec::new();
        for page in 0..10 {
            let response = self
                .request(Method::GET, "files")?
                .query(&[
                    ("n", "100"),
                    ("offset", &(page * 100).to_string()),
                    ("tag", tag),
                ])
                .send()
                .await?;
            let values: Vec<Value> = check(response, &format!("List {tag}"))?.json().await?;
            let last = values.len() < 100;
            items.extend(values.iter().map(|value| RemoteItem::from_json(value, tag)));
            if last {
                return Ok(items);
            }
        }
        bail!("VRChat returned too many {tag} files. Refresh the inventory to try again.")
    }

    pub async fn inventory(&self) -> Result<Vec<RemoteItem>> {
        let (mut stickers, emojis, animated) = tokio::try_join!(
            self.list_files(STICKER),
            self.list_files(EMOJI),
            self.list_files(ANIMATED)
        )?;
        stickers.extend(emojis);
        stickers.extend(animated);
        sort_items(&mut stickers);
        let mut seen = HashSet::new();
        stickers.retain(|item| !item.id.is_empty() && seen.insert(item.id.clone()));
        Ok(stickers)
    }

    pub async fn upload(&self, payload: UploadPayload) -> Result<String> {
        let response = self
            .request(Method::POST, "file/image")?
            .multipart(payload.multipart()?)
            .send()
            .await?;
        let value: Value = check(response, "Upload image")?.json().await?;
        Ok(field(&value, "id"))
    }

    pub async fn delete(&self, id: &str) -> Result<()> {
        ensure!(
            !id.trim().is_empty() && !matches!(id, "." | ".."),
            "File id is required."
        );
        let mut url = self.base.join("file/")?;
        url.path_segments_mut()
            .map_err(|()| anyhow::anyhow!("Invalid VRChat API address."))?
            .pop_if_empty()
            .push(id);
        let response = self
            .http
            .delete(url)
            .header(reqwest::header::USER_AGENT, &self.user_agent)
            .header(reqwest::header::COOKIE, self.cookie.clone())
            .header(reqwest::header::ACCEPT, "application/json")
            .send()
            .await?;
        check(response, "Delete file")?;
        Ok(())
    }

    pub async fn image(&self, address: &str) -> Result<Vec<u8>> {
        let url = Url::parse(address)?;
        ensure!(
            is_vrchat_image_url(&url),
            "The VRChat image address is invalid."
        );
        let request = self
            .assets
            .get(url)
            .header(reqwest::header::USER_AGENT, &self.user_agent)
            .header(reqwest::header::COOKIE, self.cookie.clone());
        let mut response = check(request.send().await?, "Download image")?;
        let mut bytes = Vec::new();
        while let Some(chunk) = response.chunk().await? {
            ensure!(
                bytes.len() + chunk.len() <= 64 * 1024 * 1024,
                "The VRChat image is too large."
            );
            bytes.extend_from_slice(&chunk);
        }
        Ok(bytes)
    }
}

fn is_vrchat_image_url(url: &Url) -> bool {
    url.scheme() == "https"
        && url.port().is_none()
        && url.username().is_empty()
        && url.password().is_none()
        && url
            .host_str()
            .is_some_and(|host| host == "vrchat.cloud" || host.ends_with(".vrchat.cloud"))
}

fn check(response: Response, action: &str) -> Result<Response> {
    let status = response.status();
    if status.is_success() {
        return Ok(response);
    }
    Err(HttpError {
        action: action.into(),
        status,
    }
    .into())
}
