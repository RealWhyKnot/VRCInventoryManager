use std::{
    collections::BTreeMap,
    fs,
    path::{Path, PathBuf},
    sync::LazyLock,
    time::UNIX_EPOCH,
};

use image::ImageReader;
use num_traits::ToPrimitive;
use regex::Regex;
use serde::Serialize;
use sha2::{Digest, Sha256};

pub const ANIMATION_STYLES: [&str; 27] = [
    "aura",
    "bats",
    "bees",
    "bounce",
    "cloud",
    "confetti",
    "crying",
    "dislike",
    "fire",
    "idea",
    "lasers",
    "like",
    "magnet",
    "mistletoe",
    "money",
    "noise",
    "orbit",
    "pizza",
    "rain",
    "rotate",
    "shake",
    "snow",
    "snowball",
    "spin",
    "splash",
    "stop",
    "zzz",
];
pub const LOOP_STYLES: [&str; 2] = ["linear", "pingpong"];

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Metadata {
    pub frames: Option<u32>,
    pub fps: Option<u32>,
    pub animation_style: &'static str,
    pub loop_style: &'static str,
}

impl Metadata {
    #[must_use]
    pub fn from_name(name: &str) -> Self {
        static ANIMATION: LazyLock<Regex> =
            LazyLock::new(|| pattern(r"(?i-u:([a-z]+)animationStyle)"));
        static LOOP: LazyLock<Regex> = LazyLock::new(|| pattern(r"(?i-u:([a-z]+)loopStyle)"));
        static FRAMES: LazyLock<Regex> =
            LazyLock::new(|| pattern(r"(?i)(?:^|_)([0-9]+)frames(?:_|\.|$)"));
        static FPS: LazyLock<Regex> =
            LazyLock::new(|| pattern(r"(?i)(?:^|_)([0-9]+)fps(?:_|\.|$)"));
        let read_number = |regex: &Regex| {
            let value: i32 = regex.captures(name)?.get(1)?.as_str().parse().ok()?;
            u32::try_from(value).ok()
        };
        let read_style = |regex: &Regex, styles: &[&'static str], default| {
            let style = regex.captures(name).and_then(|capture| capture.get(1));
            style
                .and_then(|value| {
                    styles
                        .iter()
                        .copied()
                        .find(|style| style.eq_ignore_ascii_case(value.as_str()))
                })
                .unwrap_or(default)
        };
        Self {
            frames: read_number(&FRAMES),
            fps: read_number(&FPS),
            animation_style: read_style(&ANIMATION, &ANIMATION_STYLES, "stop"),
            loop_style: read_style(&LOOP, &LOOP_STYLES, "linear"),
        }
    }
}

fn pattern(regex: &str) -> Regex {
    Regex::new(regex).expect("the sprite sheet pattern is valid")
}

#[derive(Clone, Debug)]
pub struct LocalAsset {
    pub id: String,
    pub name: String,
    pub path: PathBuf,
    pub relative_dir: String,
    pub bucket: String,
    pub ext: String,
    pub size_bytes: u64,
    pub modified_ms: i64,
    pub dimensions: Option<(u32, u32)>,
    pub metadata: Metadata,
}

impl LocalAsset {
    #[must_use]
    pub fn read(path: &Path, root: &Path) -> Option<Self> {
        let path = std::path::absolute(path).ok()?;
        let name = path.file_name()?.to_string_lossy().into_owned();
        let ext = path.extension()?.to_str()?.to_ascii_lowercase();
        if !matches!(
            ext.as_str(),
            "png" | "gif" | "jpg" | "jpeg" | "bmp" | "tif" | "tiff"
        ) {
            return None;
        }
        let file = fs::metadata(&path).ok()?;
        if !file.is_file() {
            return None;
        }
        let directory = path.parent()?;
        let relative_dir = directory
            .strip_prefix(root)
            .unwrap_or(Path::new(""))
            .to_string_lossy()
            .into_owned();
        let bucket = directory.file_name().map_or_else(
            || "Unfiled".into(),
            |name| name.to_string_lossy().into_owned(),
        );
        let modified_ms =
            file.modified()
                .ok()
                .map_or(0, |modified| match modified.duration_since(UNIX_EPOCH) {
                    Ok(duration) => i64::try_from(duration.as_millis()).unwrap_or(i64::MAX),
                    Err(err) => -i64::try_from(err.duration().as_millis()).unwrap_or(i64::MAX),
                });
        let dimensions = ImageReader::open(&path)
            .ok()
            .and_then(|reader| reader.with_guessed_format().ok())
            .and_then(|reader| reader.into_dimensions().ok());
        let id = hex::encode(Sha256::digest(path.to_string_lossy().as_bytes()));
        Some(Self {
            id,
            metadata: Metadata::from_name(&name),
            name,
            path,
            relative_dir,
            bucket,
            ext,
            size_bytes: file.len(),
            modified_ms,
            dimensions,
        })
    }

    #[must_use]
    pub fn is_gif(&self) -> bool {
        self.ext == "gif"
    }

    #[must_use]
    pub fn is_sprite_sheet(&self) -> bool {
        self.ext == "png"
            && self.metadata.frames.unwrap_or(0) > 1
            && self.metadata.fps.unwrap_or(0) > 0
    }

    #[must_use]
    pub fn is_animated(&self) -> bool {
        self.is_gif() || self.is_sprite_sheet()
    }

    fn size_as_float(&self) -> f64 {
        self.size_bytes.to_f64().unwrap_or(f64::MAX)
    }

    #[must_use]
    pub fn size_text(&self) -> String {
        match self.size_bytes {
            0..1024 => format!("{} B", self.size_bytes),
            1024..1_048_576 => format!("{:.1} KB", self.size_as_float() / 1024.0),
            _ => format!("{:.1} MB", self.size_as_float() / 1_048_576.0),
        }
    }

    #[must_use]
    pub fn matches(&self, folder: &str, query: &str, include_subfolders: bool) -> bool {
        let folder = normalize_folder(folder);
        if !folder.is_empty() {
            let relative = normalize_folder(&self.relative_dir);
            let in_folder = relative == folder
                || (include_subfolders
                    && relative
                        .strip_prefix(&folder)
                        .is_some_and(|suffix| suffix.starts_with('/')));
            if !in_folder {
                return false;
            }
        }
        let query = query.trim().to_lowercase();
        query.is_empty()
            || self.name.to_lowercase().contains(&query)
            || self
                .path
                .parent()
                .is_some_and(|dir| dir.to_string_lossy().to_lowercase().contains(&query))
            || self.relative_dir.to_lowercase().contains(&query)
    }
}

fn normalize_folder(path: &str) -> String {
    let path = path.trim().trim_matches(['/', '\\']);
    if path == "." {
        String::new()
    } else {
        path.replace('\\', "/").to_lowercase()
    }
}

#[must_use]
pub fn scan(root: &Path, exclude_inventory_folders: bool) -> Vec<LocalAsset> {
    let Ok(root) = std::path::absolute(root) else {
        return Vec::new();
    };
    let mut pending = vec![root.clone()];
    let mut assets = Vec::new();
    while let Some(path) = pending.pop() {
        let Ok(entries) = fs::read_dir(&path) else {
            continue;
        };
        for entry in entries.flatten() {
            let Ok(kind) = entry.file_type() else {
                continue;
            };
            if kind.is_dir() {
                if exclude_inventory_folders
                    && path == root
                    && ["emoji", "stickers", "prints"]
                        .iter()
                        .any(|name| entry.file_name().eq_ignore_ascii_case(name))
                {
                    continue;
                }
                if is_reparse_point(&entry) {
                    continue;
                }
                pending.push(entry.path());
            } else if kind.is_file()
                && let Some(asset) = LocalAsset::read(&entry.path(), &root)
            {
                assets.push(asset);
            }
        }
    }
    sort_assets(&mut assets);
    assets
}

#[cfg(windows)]
fn is_reparse_point(entry: &fs::DirEntry) -> bool {
    use std::os::windows::fs::MetadataExt;
    entry
        .metadata()
        .is_ok_and(|meta| meta.file_attributes() & 0x400 != 0)
}

#[cfg(not(windows))]
fn is_reparse_point(_: &fs::DirEntry) -> bool {
    false
}

pub fn sort_assets(assets: &mut [LocalAsset]) {
    assets.sort_by_cached_key(|asset| {
        (
            std::cmp::Reverse(asset.modified_ms),
            asset.name.to_lowercase(),
            asset.id.clone(),
        )
    });
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Folder {
    pub path: PathBuf,
    pub name: String,
    pub relative_path: String,
    pub direct_count: usize,
    pub total_count: usize,
    pub children: Vec<Folder>,
}

#[must_use]
pub fn folder_tree(root: &Path, assets: &[LocalAsset]) -> Folder {
    let mut accumulator = FolderAccumulator::default();
    for asset in assets {
        let mut current = &mut accumulator;
        for name in asset
            .relative_dir
            .split(['/', '\\'])
            .filter(|segment| !segment.is_empty())
        {
            current = current
                .children
                .entry(name.to_lowercase())
                .or_insert_with(|| FolderAccumulator {
                    name: name.to_owned(),
                    ..Default::default()
                });
        }
        current.direct_count += 1;
    }
    accumulator.name = "All".into();
    accumulator.into_folder(root, Path::new(""))
}

#[derive(Default)]
struct FolderAccumulator {
    name: String,
    direct_count: usize,
    children: BTreeMap<String, Self>,
}

impl FolderAccumulator {
    fn into_folder(self, root: &Path, relative_path: &Path) -> Folder {
        let children: Vec<_> = self
            .children
            .into_values()
            .rev()
            .map(|child| {
                let relative = relative_path.join(&child.name);
                child.into_folder(root, &relative)
            })
            .collect();
        let total_count = self.direct_count
            + children
                .iter()
                .map(|child| child.total_count)
                .sum::<usize>();
        Folder {
            path: root.join(relative_path),
            name: self.name,
            relative_path: relative_path.to_string_lossy().into_owned(),
            direct_count: self.direct_count,
            total_count,
            children,
        }
    }
}
