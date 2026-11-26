use std::{
    fs::{self, File},
    io::{BufReader, Cursor},
    num::NonZeroUsize,
    path::Path,
    sync::{Arc, Mutex},
};

use anyhow::{Result, bail, ensure};
use image::{
    AnimationDecoder, Delay, DynamicImage, Frame, ImageFormat, ImageReader, RgbaImage,
    codecs::gif::{GifDecoder, GifEncoder, Repeat},
    imageops::{self, FilterType},
};
use lru::LruCache;
use num_traits::ToPrimitive;

pub const MAX_SIDE: u32 = 1024;
pub const MAX_UPLOAD_BYTES: u64 = 10 * 1024 * 1024;
pub const MAX_FRAMES: u32 = 64;
pub const THUMB_SIDE: u32 = 240;

pub fn png_payload(path: &Path, sprite_sheet: bool) -> Result<Vec<u8>> {
    let reader = ImageReader::open(path)?.with_guessed_format()?;
    let is_png = reader.format() == Some(ImageFormat::Png)
        && path
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("png"));
    ensure!(
        !sprite_sheet || is_png,
        "Animated sprite sheet upload requires a PNG file."
    );
    let image = reader.decode()?;
    ensure!(
        !sprite_sheet || image.width() == image.height(),
        "Animated sprite sheet upload requires a square PNG."
    );
    if is_png
        && image.width() <= MAX_SIDE
        && image.height() <= MAX_SIDE
        && fs::metadata(path)?.len() <= MAX_UPLOAD_BYTES
    {
        return Ok(fs::read(path)?);
    }
    encode_png(&fit(&image.into_rgba8(), MAX_SIDE, false))
}

pub fn encode_png(pixels: &RgbaImage) -> Result<Vec<u8>> {
    let mut bytes = Cursor::new(Vec::new());
    pixels.write_to(&mut bytes, ImageFormat::Png)?;
    Ok(bytes.into_inner())
}

fn fit(pixels: &RgbaImage, side: u32, upscale: bool) -> RgbaImage {
    let longest = pixels.width().max(pixels.height()).max(1);
    if !upscale && longest <= side {
        return pixels.clone();
    }
    let scale = f64::from(side) / f64::from(longest);
    let scaled = |length: u32| {
        (f64::from(length) * scale)
            .round_ties_even()
            .max(1.0)
            .to_u32()
            .unwrap_or(side)
    };
    let width = scaled(pixels.width());
    let height = scaled(pixels.height());
    imageops::resize(pixels, width, height, FilterType::Lanczos3)
}

pub fn grid(frames: u32) -> Result<u32> {
    ensure!(frames > 0, "Frame count must be positive.");
    Ok([1, 2, 4, 8]
        .into_iter()
        .find(|side| frames <= side * side)
        .unwrap_or(8))
}

pub fn frame_rect(
    width: u32,
    height: u32,
    frames: u32,
    index: u32,
) -> Result<(u32, u32, u32, u32)> {
    ensure!(
        index < frames && frames <= MAX_FRAMES,
        "Frame is outside the sprite sheet."
    );
    let grid = grid(frames)?;
    let cell_width = (width / grid).max(1);
    let cell_height = (height / grid).max(1);
    let left = (index % grid) * cell_width;
    let top = (index / grid) * cell_height;
    ensure!(
        left < width && top < height,
        "Frame count exceeds the available sprite sheet cells."
    );
    Ok((
        left,
        top,
        cell_width.min(width - left),
        cell_height.min(height - top),
    ))
}

pub fn frame_png(image: &DynamicImage, frames: u32, index: u32, max_side: u32) -> Result<Vec<u8>> {
    let (left, top, width, height) = frame_rect(image.width(), image.height(), frames, index)?;
    let cell = image.crop_imm(left, top, width, height).into_rgba8();
    encode_png(&fit(&cell, max_side, false))
}

#[derive(Debug)]
pub struct SpriteSheet {
    pub png: Vec<u8>,
    pub frames: u32,
    pub fps: u32,
}

fn delay_ms(frame: &Frame) -> f64 {
    let (numerator, denominator) = frame.delay().numer_denom_ms();
    if numerator == 0 {
        100.0
    } else {
        f64::from(numerator) / f64::from(denominator)
    }
}

pub fn gif_sprite_sheet(path: &Path) -> Result<SpriteSheet> {
    let decode = || GifDecoder::new(BufReader::new(File::open(path)?));
    let mut duration = 0.0;
    let mut total = 0_u32;
    for frame in decode()?.into_frames() {
        duration += delay_ms(&frame?);
        total += 1;
    }
    ensure!(
        total >= 2,
        "Animated emoji upload requires a GIF with at least two frames."
    );
    let frames = total.min(MAX_FRAMES);
    let fps = (f64::from(frames) * 1000.0 / duration.max(10.0))
        .round_ties_even()
        .clamp(1.0, 64.0)
        .to_u32()
        .unwrap_or(1);
    let grid = grid(frames)?;
    let cell_size = MAX_SIDE / grid;
    let mut sheet = RgbaImage::new(MAX_SIDE, MAX_SIDE);
    let indexes = (0..frames).map(|index| {
        (f64::from(index) * f64::from(total - 1) / f64::from(frames - 1))
            .round_ties_even()
            .to_usize()
            .unwrap_or_default()
    });
    let mut source = decode()?.into_frames();
    let mut consumed = 0;
    for (output, index) in (0..frames).zip(indexes) {
        let Some(frame) = source.nth(index - consumed) else {
            bail!("GIF changed while it was being read.");
        };
        consumed = index + 1;
        let pixels = fit(frame?.buffer(), cell_size, true);
        let left = (output % grid) * cell_size + (cell_size - pixels.width()) / 2;
        let top = (output / grid) * cell_size + (cell_size - pixels.height()) / 2;
        imageops::replace(&mut sheet, &pixels, i64::from(left), i64::from(top));
    }
    Ok(SpriteSheet {
        png: encode_png(&sheet)?,
        frames,
        fps,
    })
}

pub struct ImageBytes {
    pub content_type: &'static str,
    pub data: Vec<u8>,
}

impl ImageBytes {
    #[must_use]
    pub fn png(data: Vec<u8>) -> Self {
        Self {
            content_type: "image/png",
            data,
        }
    }
}

pub fn thumbnail(source: &[u8], sprite_frames: Option<u32>) -> Result<ImageBytes> {
    if image::guess_format(source)? == ImageFormat::Gif {
        let decoder = GifDecoder::new(Cursor::new(source))?;
        let mut data = Vec::new();
        {
            let mut encoder = GifEncoder::new_with_speed(&mut data, 10);
            encoder.set_repeat(Repeat::Infinite)?;
            for frame in decoder.into_frames() {
                let frame = frame?;
                let delay = if frame.delay().numer_denom_ms().0 == 0 {
                    Delay::from_numer_denom_ms(100, 1)
                } else {
                    frame.delay()
                };
                encoder.encode_frame(Frame::from_parts(
                    fit(frame.buffer(), THUMB_SIDE, false),
                    0,
                    0,
                    delay,
                ))?;
            }
        }
        return Ok(ImageBytes {
            content_type: "image/gif",
            data,
        });
    }
    let image = image::load_from_memory(source)?;
    if let Some(frames) = sprite_frames {
        return Ok(ImageBytes::png(frame_png(&image, frames, 0, THUMB_SIDE)?));
    }
    Ok(ImageBytes::png(encode_png(&fit(
        &image.into_rgba8(),
        THUMB_SIDE,
        false,
    ))?))
}

pub fn web_image(source: Vec<u8>) -> Result<ImageBytes> {
    let content_type = match image::guess_format(&source)? {
        ImageFormat::Gif => "image/gif",
        ImageFormat::Png => "image/png",
        ImageFormat::Jpeg => "image/jpeg",
        ImageFormat::WebP => "image/webp",
        ImageFormat::Bmp => "image/bmp",
        _ => {
            return Ok(ImageBytes::png(encode_png(
                &image::load_from_memory(&source)?.into_rgba8(),
            )?));
        }
    };
    Ok(ImageBytes {
        content_type,
        data: source,
    })
}

pub struct ImageCache(Mutex<LruCache<String, Arc<ImageBytes>>>);

impl ImageCache {
    #[must_use]
    pub fn new(capacity: NonZeroUsize) -> Self {
        Self(Mutex::new(LruCache::new(capacity)))
    }

    pub fn get(&self, key: &str) -> Option<Arc<ImageBytes>> {
        self.0
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .get(key)
            .cloned()
    }

    pub fn insert(&self, key: String, image: ImageBytes) -> Arc<ImageBytes> {
        let image = Arc::new(image);
        self.0
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .put(key, Arc::clone(&image));
        image
    }

    pub fn clear(&self) {
        self.0
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .clear();
    }
}
