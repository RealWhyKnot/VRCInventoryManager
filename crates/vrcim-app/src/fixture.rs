use std::{
    fs,
    path::{Path, PathBuf},
};

use anyhow::Result;
use image::{
    Delay, Frame, Rgba, RgbaImage,
    codecs::gif::{GifEncoder, Repeat},
};
use num_traits::ToPrimitive;
use serde::Deserialize;
use tempfile::TempDir;
use vrcim_core::{
    image_pipeline::encode_png,
    paths::KnownFolders,
    updater::Release,
    vrchat_api::{ANIMATED, EMOJI, RemoteItem, STICKER, User},
};

#[derive(Debug, Clone, Copy, Default, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub(crate) enum Mode {
    #[default]
    Full,
    Empty,
    Loading,
    Error,
    Update,
    Limit,
}

pub(crate) struct Fixture {
    _temp: TempDir,
    pub mode: Mode,
    pub folders: KnownFolders,
    pub remote_dir: PathBuf,
}

impl Fixture {
    pub(crate) fn new(mode: Mode) -> Result<Self> {
        let temp = tempfile::Builder::new()
            .prefix("vrcim-fixture-")
            .tempdir()?;
        let folders = KnownFolders::from_photos(temp.path().join("VRChat"));
        let remote_dir = temp.path().join("Remote");
        for folder in [
            &folders.photos,
            &folders.emoji,
            &folders.stickers,
            &folders.prints,
            &remote_dir,
        ] {
            fs::create_dir_all(folder)?;
        }
        if !matches!(mode, Mode::Empty | Mode::Loading) {
            for ((folder, name), index) in [
                ("Favorites", "Moon Jelly_auraanimationStyle.png"),
                ("Favorites", "Neon Heart_likeanimationStyle.png"),
                ("Favorites/Night", "Lantern_splashanimationStyle.png"),
                ("Favorites/Night", "Cloud Garden.png"),
                (
                    "Reactions",
                    "Signal_orbitanimationStyle_9frames_12fps_pingpongloopStyle.png",
                ),
                ("Reactions", "Star Burst_confettianimationStyle.gif"),
                ("Reactions", "Space Cat.png"),
                ("Reactions", "Happy Ghost.png"),
                ("Seasonal", "Snow Day_snowanimationStyle.png"),
                ("Seasonal", "Midnight Pizza_pizzaanimationStyle.png"),
                ("Seasonal/Winter", "Winter Bloom.png"),
                ("Archive", "Citrus Slice.png"),
                ("Archive", "Blue Planet.png"),
                ("", "Sunset Meadow.png"),
                ("", "Bubble Tea.png"),
                ("", "Tiny World.png"),
            ]
            .into_iter()
            .zip(0..)
            {
                let path = folders.emoji.join(folder).join(name);
                write_image(&path, index)?;
            }
            for ((folder, file), index) in [
                (&folders.photos, "VRChat_2026-11-24_21-34.png"),
                (&folders.photos, "Night at the Observatory.png"),
                (&folders.stickers, "Favorite Wave.png"),
                (&folders.stickers, "Mossy Friend.png"),
                (&folders.prints, "Postcard from the Moon.png"),
            ]
            .into_iter()
            .zip(17..)
            {
                write_image(&folder.join(file), index)?;
            }
        }
        Ok(Self {
            _temp: temp,
            mode,
            folders,
            remote_dir,
        })
    }

    pub(crate) fn user(&self) -> Option<User> {
        (!matches!(self.mode, Mode::Loading | Mode::Error)).then(|| User {
            id: "usr_fixture".into(),
            display_name: "Alex Chen".into(),
        })
    }

    pub(crate) fn auth_error(&self) -> Option<String> {
        (self.mode == Mode::Error)
            .then(|| "Couldn't connect to VRChat. Sign in through VRCX, then reconnect.".into())
    }

    pub(crate) fn release(&self) -> Option<Release> {
        (self.mode == Mode::Update).then(|| Release {
            tag: "v2026.12.1.0".into(),
            html_url:
                "https://github.com/RealWhyKnot/VRCInventoryManager/releases/tag/v2026.12.1.0"
                    .into(),
            ..Release::default()
        })
    }

    pub(crate) fn inventory(&self) -> Result<Vec<RemoteItem>> {
        if matches!(self.mode, Mode::Empty | Mode::Loading | Mode::Error) {
            return Ok(Vec::new());
        }
        let count = if self.mode == Mode::Limit { 18 } else { 7 };
        let names = [
            "Paper Satellites",
            "Moon Jelly",
            "Star Burst",
            "Citrus Slice",
            "Blue Planet",
            "Tiny World",
            "Soft Glow",
        ];
        let mut items = Vec::new();
        for tag in [STICKER, EMOJI] {
            for (index, seed) in (0..count).zip(0_u32..) {
                let id = format!("file_{tag}_{index}");
                let animated = tag == EMOJI && index % 3 == 0;
                let path = self.remote_image(&id);
                if !path.is_file() {
                    write_image(&path, seed + u32::from(tag == EMOJI) * 7)?;
                }
                if animated {
                    fs::write(&path, encode_png(&sheet(seed))?)?;
                }
                let name = if index < names.len() {
                    names[index].into()
                } else {
                    format!("{} {}", names[index % names.len()], index + 1)
                };
                items.push(RemoteItem {
                    id,
                    name,
                    tag: if animated { ANIMATED } else { tag }.into(),
                    mime_type: "image/png".into(),
                    created_at: jiff::Timestamp::from_second(
                        1_795_500_000 - i64::from(seed) * 86_400,
                    )
                    .ok(),
                    status: "complete".into(),
                    frames: animated.then_some(9),
                    fps: animated.then_some(12),
                    animation_style: if tag == EMOJI {
                        if animated { "orbit" } else { "stop" }
                    } else {
                        ""
                    }
                    .into(),
                    loop_style: if animated { "pingpong" } else { "" }.into(),
                    mask_tag: "square".into(),
                    preview_url: path.to_string_lossy().into_owned(),
                });
            }
        }
        vrcim_core::vrchat_api::sort_items(&mut items);
        Ok(items)
    }

    pub(crate) fn remote_image(&self, id: &str) -> PathBuf {
        self.remote_dir.join(format!("{id}.png"))
    }
}

fn art(width: u32, height: u32, seed: u32) -> RgbaImage {
    let colors: [([u8; 3], [u8; 3], [u8; 3]); 6] = [
        ([27, 43, 88], [98, 171, 232], [246, 159, 184]),
        ([51, 27, 67], [215, 108, 149], [253, 191, 130]),
        ([18, 56, 56], [104, 208, 164], [226, 238, 161]),
        ([48, 36, 91], [150, 117, 239], [235, 190, 241]),
        ([71, 32, 46], [238, 144, 99], [255, 218, 137]),
        ([30, 68, 93], [92, 191, 196], [210, 239, 232]),
    ];
    let palette = usize::try_from(seed).unwrap_or_default() % colors.len();
    let (dark, middle, light) = colors[palette];
    let phase = f64::from(seed % 10) * 0.06;
    RgbaImage::from_fn(width, height, |column, row| {
        let horizontal = f64::from(column) / f64::from(width);
        let vertical = f64::from(row) / f64::from(height);
        let center_horizontal = 0.35 + phase * 0.5;
        let center_vertical = 0.38 + phase * 0.2;
        let distance = (horizontal - center_horizontal).hypot(vertical - center_vertical);
        let glow = (1.0 - distance * 2.1).max(0.0).powf(1.8);
        let band = f64::from(u8::from(
            vertical + horizontal * 0.18 + (horizontal * 9.0 + phase).sin() * 0.11 > 0.72,
        ));
        let star = f64::from(u8::from(
            (column.wrapping_mul(53) ^ row.wrapping_mul(199) ^ seed) % 657 == 30,
        ));
        let planet = if distance < 0.17 {
            0.58 + glow * 0.35
        } else {
            glow * 0.34
        };
        let mut rgb = [0, 0, 0, 255];
        for (channel, value) in rgb.iter_mut().take(3).enumerate() {
            let background = f64::from(dark[channel])
                + (f64::from(middle[channel]) - f64::from(dark[channel]))
                    * (0.24 + 0.55 * vertical);
            *value = (background * (1.0 - planet) + f64::from(light[channel]) * planet
                - band * 16.0
                + star * 70.0)
                .clamp(0.0, 255.0)
                .to_u8()
                .unwrap_or(u8::MAX);
        }
        Rgba(rgb)
    })
}

fn sheet(seed: u32) -> RgbaImage {
    let mut sheet = RgbaImage::new(384, 384);
    for index in 0..9 {
        image::imageops::replace(
            &mut sheet,
            &art(96, 96, seed + index),
            i64::from((index % 4) * 96),
            i64::from((index / 4) * 96),
        );
    }
    sheet
}

fn write_image(path: &Path, index: u32) -> Result<()> {
    fs::create_dir_all(path.parent().expect("fixture image has a parent directory"))?;
    let name = path
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or_default();
    let is_gif = path
        .extension()
        .is_some_and(|extension| extension.eq_ignore_ascii_case("gif"));
    if is_gif {
        let file = fs::File::create(path)?;
        let mut encoder = GifEncoder::new_with_speed(file, 10);
        encoder.set_repeat(Repeat::Infinite)?;
        for frame in 0..6 {
            encoder.encode_frame(Frame::from_parts(
                art(192, 192, index + frame),
                0,
                0,
                Delay::from_numer_denom_ms(100, 1),
            ))?;
        }
    } else if name.contains("frames") {
        fs::write(path, encode_png(&sheet(index))?)?;
    } else {
        fs::write(path, encode_png(&art(300 + (index % 3) * 60, 270, index))?)?;
    }
    Ok(())
}
