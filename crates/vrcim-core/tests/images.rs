use std::{
    fs,
    io::Cursor,
    path::Path,
    time::{Duration, SystemTime},
};

use image::{
    AnimationDecoder, Delay, DynamicImage, Frame, GenericImageView, ImageFormat, Rgb, RgbImage,
    Rgba, RgbaImage,
    codecs::gif::{GifDecoder, GifEncoder, Repeat},
};
use tempfile::tempdir;
use vrcim_core::{
    image_pipeline::{self, ImageBytes, ImageCache, gif_sprite_sheet, png_payload},
    local_assets::{ANIMATION_STYLES, LocalAsset, Metadata, folder_tree, scan},
    vrchat_api::{ANIMATED, UploadKind, UploadPayload},
};

fn png(path: &Path, width: u32, height: u32) -> Vec<u8> {
    fs::create_dir_all(path.parent().expect("the test PNG is written"))
        .expect("the test PNG is written");
    let bytes = image_pipeline::encode_png(&RgbaImage::from_pixel(
        width,
        height,
        Rgba([17, 101, 205, 250]),
    ))
    .expect("the test PNG is written");
    fs::write(path, &bytes).expect("the test PNG is written");
    bytes
}

fn gif(path: &Path, frames: u32, delay: u32) {
    let mut encoder =
        GifEncoder::new_with_speed(fs::File::create(path).expect("the test GIF is written"), 30);
    encoder
        .set_repeat(Repeat::Infinite)
        .expect("the test GIF is written");
    for frame in 0..frames {
        let pixels = RgbaImage::from_pixel(
            18,
            12,
            Rgba([
                u8::try_from(frame).expect("test GIFs have under 256 frames"),
                100,
                220,
                255,
            ]),
        );
        encoder
            .encode_frame(Frame::from_parts(
                pixels,
                0,
                0,
                Delay::from_numer_denom_ms(delay, 1),
            ))
            .expect("the test GIF is written");
    }
}

fn asset(root: &Path, name: &str) -> LocalAsset {
    let path = root.join(name);
    fs::create_dir_all(path.parent().expect("the test asset is written"))
        .expect("the test asset is written");
    fs::write(&path, []).expect("the test asset is written");
    LocalAsset::read(&path, root).expect("the test asset is written")
}

#[test]
fn scans_supported_extensions_recursively_and_skips_unsupported_files() {
    let root = tempdir().unwrap();
    let paths = [
        "main.png",
        "nested/Pic.JPG",
        "nested/second.jpeg",
        "nested/tiny.gif",
        "other/first.bmp",
        "other/second.tif",
        "other/third.tiff",
    ];
    for path in paths {
        asset(root.path(), path);
    }
    fs::write(root.path().join("ignore.txt"), []).unwrap();
    let assets = scan(root.path(), false);
    assert_eq!(assets.len(), paths.len());
    let main = assets
        .iter()
        .find(|asset| asset.name == "main.png")
        .unwrap();
    assert_eq!(main.relative_dir, "");
    let nested = assets.iter().find(|asset| asset.name == "Pic.JPG").unwrap();
    assert_eq!(nested.relative_dir, "nested");
    assert_eq!(nested.bucket, "nested");
    assert_eq!(nested.ext, "jpg");
}

#[test]
fn photos_filter_excludes_only_top_level_inventory_folders() {
    let root = tempdir().unwrap();
    for path in [
        "root.png",
        "eMoJi/s.png",
        "PRINTS/s.png",
        "Stickers/s.png",
        "Album/Emoji/s.png",
    ] {
        asset(root.path(), path);
    }
    let assets = scan(root.path(), true);
    assert_eq!(assets.len(), 2);
    assert!(
        assets
            .iter()
            .any(|asset| Path::new(&asset.relative_dir) == Path::new("Album/Emoji"))
    );
}

#[test]
fn orders_assets_by_last_write_time_then_name() {
    let root = tempdir().unwrap();
    let first = asset(root.path(), "first.png");
    let second = asset(root.path(), "SECOND.png");
    let newest = asset(root.path(), "zzz.png");
    let start = SystemTime::UNIX_EPOCH + Duration::from_secs(1_700_000_000);
    for (file, modified) in [
        (&first, start),
        (&second, start),
        (&newest, start + Duration::from_secs(1)),
    ] {
        fs::OpenOptions::new()
            .write(true)
            .open(&file.path)
            .unwrap()
            .set_modified(modified)
            .unwrap();
    }
    let assets = scan(root.path(), false);
    assert_eq!(
        assets
            .iter()
            .map(|asset| asset.name.as_str())
            .collect::<Vec<_>>(),
        ["zzz.png", "first.png", "SECOND.png"]
    );
}

#[test]
fn builds_descending_folder_tree_with_direct_and_total_counts() {
    let root = tempdir().unwrap();
    let assets: Vec<_> = [
        "root.png",
        "2026-06/a.png",
        "2026-06/b.png",
        "2026-06/Quest/c.png",
        "2026-05/d.png",
    ]
    .into_iter()
    .map(|path| asset(root.path(), path))
    .collect();
    let tree = folder_tree(root.path(), &assets);
    assert_eq!(tree.name, "All");
    assert_eq!(tree.relative_path, "");
    assert_eq!((tree.direct_count, tree.total_count), (1, 5));
    assert_eq!(tree.children.len(), 2);
    let june = &tree.children[0];
    assert_eq!(
        (&june.name[..], &june.relative_path[..]),
        ("2026-06", "2026-06")
    );
    assert_eq!(june.path, root.path().join("2026-06"));
    assert_eq!((june.direct_count, june.total_count), (2, 3));
    let quest = &june.children[0];
    assert_eq!(quest.name, "Quest");
    assert_eq!(Path::new(&quest.relative_path), Path::new("2026-06/Quest"));
    assert_eq!((quest.direct_count, quest.total_count), (1, 1));
}

#[test]
fn matches_folders_and_search_fields_case_insensitively() {
    let root = tempdir().unwrap();
    let asset = asset(root.path(), "2026-06/Quest/Wave.png");
    assert!(asset.matches("2026-06", "  WaVe ", true));
    assert!(asset.matches("2026-06\\QUEST", "quest", false));
    assert!(asset.matches("2026-06/Quest", &root.path().to_string_lossy(), false));
    assert!(!asset.matches("2026-06", "wave", false));
    assert!(!asset.matches("2026-0", "wave", true));
    assert!(!asset.matches("2026-06/Quest", "unmatched", true));
    assert!(asset.matches(".", "", false));
}

#[test]
fn formats_bytes_kilobytes_and_megabytes() {
    let root = tempdir().unwrap();
    let mut asset = asset(root.path(), "wave_likeanimationStyle.png");
    for (size, label) in [(1, "1 B"), (2048, "2.0 KB"), (1_572_864, "1.5 MB")] {
        asset.size_bytes = size;
        assert_eq!(asset.size_text(), label);
    }
    assert_eq!(asset.metadata.animation_style, "like");
}

#[test]
fn parses_every_animation_style_with_case_insensitive_matching() {
    assert_eq!(ANIMATION_STYLES.len(), 27);
    for style in ANIMATION_STYLES {
        let name = format!(
            "emoji_{}ANIMATIONSTYLE_64frames.png",
            style.to_ascii_uppercase()
        );
        assert_eq!(Metadata::from_name(&name).animation_style, style);
    }
    assert_eq!(
        Metadata::from_name("avatar_unknownanimationStyle.png").animation_style,
        "stop"
    );
    assert_eq!(Metadata::from_name("plain.png").animation_style, "stop");
}

#[test]
fn parses_loop_frames_and_fps_and_rejects_bad_boundaries() {
    let metadata =
        Metadata::from_name("emoji_snowanimationStyle_64frames_24fps_PINGPONGloopStyle.png");
    assert_eq!((metadata.frames, metadata.fps), (Some(64), Some(24)));
    assert_eq!(metadata.loop_style, "pingpong");
    assert_eq!(
        Metadata::from_name("item_linearloopStyle.gif").loop_style,
        "linear"
    );
    assert_eq!(
        Metadata::from_name("item_unsupportedloopStyle.gif").loop_style,
        "linear"
    );
    assert_eq!(Metadata::from_name("20fps").fps, Some(20));
    assert_eq!(Metadata::from_name("32frames._18fps").frames, Some(32));
    assert_eq!(
        Metadata::from_name("bad64frames_24fpsX_999999999999frames").frames,
        None
    );
    assert_eq!(
        Metadata::from_name("bad64frames_24fpsX_999999999999frames").fps,
        None
    );
}

#[test]
fn classifies_static_gif_and_sprite_sheet_uploads() {
    let root = tempdir().unwrap();
    for (file, animated, gif, sprite_sheet) in [
        ("plain.png", false, false, false),
        ("animated.gif", true, true, false),
        ("sheet_64frames_24fps.png", true, false, true),
        ("sheet_64frames.png", false, false, false),
        ("sheet_1frames_24fps.png", false, false, false),
        ("sheet_64frames_24fps.jpeg", false, false, false),
        ("sheet_64frames_0fps.png", false, false, false),
    ] {
        let asset = asset(root.path(), file);
        assert_eq!(
            (asset.is_animated(), asset.is_gif(), asset.is_sprite_sheet()),
            (animated, gif, sprite_sheet)
        );
    }
}

#[test]
fn passes_through_upload_ready_static_png() {
    let root = tempdir().unwrap();
    let path = root.path().join("still.png");
    let original = png(&path, 80, 120);
    assert_eq!(png_payload(&path, false).unwrap(), original);
}

#[test]
fn converts_jpeg_to_rgba_png_without_padding() {
    let root = tempdir().unwrap();
    let path = root.path().join("still.jpg");
    RgbImage::from_pixel(80, 120, Rgb([20, 40, 60]))
        .save(&path)
        .unwrap();
    let bytes = png_payload(&path, false).unwrap();
    assert_eq!(image::guess_format(&bytes).unwrap(), ImageFormat::Png);
    let output = image::load_from_memory(&bytes).unwrap();
    assert_eq!(output.dimensions(), (80, 120));
    assert_eq!(output.color(), image::ColorType::Rgba8);
}

#[test]
fn downscales_large_static_png_and_preserves_aspect_ratio() {
    let root = tempdir().unwrap();
    let path = root.path().join("large.png");
    png(&path, 1536, 768);
    let bytes = png_payload(&path, false).unwrap();
    assert_eq!(
        image::load_from_memory(&bytes).unwrap().dimensions(),
        (1024, 512)
    );
}

#[test]
fn reencodes_oversized_png_even_when_dimensions_fit() {
    let root = tempdir().unwrap();
    let path = root.path().join("large.png");
    png(&path, 128, 256);
    fs::OpenOptions::new()
        .write(true)
        .open(&path)
        .unwrap()
        .set_len(image_pipeline::MAX_UPLOAD_BYTES + 1)
        .unwrap();
    let bytes = png_payload(&path, false).unwrap();
    assert!(u64::try_from(bytes.len()).unwrap() < image_pipeline::MAX_UPLOAD_BYTES);
    assert_eq!(
        image::load_from_memory(&bytes).unwrap().dimensions(),
        (128, 256)
    );
}

#[test]
fn passes_square_sprite_sheet_and_rejects_rectangle() {
    let root = tempdir().unwrap();
    let square = root.path().join("square.png");
    let rectangle = root.path().join("rectangle.png");
    assert_eq!(png(&square, 32, 32), png_payload(&square, true).unwrap());
    png(&rectangle, 32, 16);
    assert!(
        png_payload(&rectangle, true)
            .unwrap_err()
            .to_string()
            .contains("square PNG")
    );
}

#[test]
fn converts_animated_gif_to_png_canvas_and_power_of_two_grid() {
    let root = tempdir().unwrap();
    let path = root.path().join("animated.gif");
    for (frames, grid) in [(2, 2), (6, 4)] {
        gif(&path, frames, 100);
        let sheet = gif_sprite_sheet(&path).unwrap();
        assert_eq!((sheet.frames, sheet.fps), (frames, 10));
        assert_eq!(image_pipeline::grid(sheet.frames).unwrap(), grid);
        assert_eq!(image::guess_format(&sheet.png).unwrap(), ImageFormat::Png);
        assert_eq!(
            image::load_from_memory(&sheet.png).unwrap().dimensions(),
            (1024, 1024)
        );
    }
}

#[test]
fn samples_first_and_last_frames_and_preserves_gif_duration() {
    let root = tempdir().unwrap();
    let path = root.path().join("animated.gif");
    gif(&path, 120, 100);
    let sheet = gif_sprite_sheet(&path).unwrap();
    assert_eq!((sheet.frames, sheet.fps), (64, 5));
    let png = image::load_from_memory(&sheet.png).unwrap();
    assert_eq!(png.get_pixel(64, 64)[0], 0);
    assert_eq!(png.get_pixel(960, 960)[0], 119);
}

#[test]
fn composes_partial_gif_frames_and_centers_nonsquare_content() {
    let root = tempdir().unwrap();
    let path = root.path().join("partial.gif");
    let red = Rgba([240, 15, 0, 255]);
    let blue = Rgba([0, 15, 240, 255]);
    let mut encoder = gif::Encoder::new(
        fs::File::create(&path).unwrap(),
        16,
        8,
        &[240, 15, 0, 0, 15, 240],
    )
    .unwrap();
    encoder
        .write_frame(&gif::Frame {
            width: 16,
            height: 8,
            delay: 10,
            dispose: gif::DisposalMethod::Keep,
            buffer: std::borrow::Cow::Owned(vec![0; 16 * 8]),
            ..gif::Frame::default()
        })
        .unwrap();
    encoder
        .write_frame(&gif::Frame {
            width: 8,
            height: 8,
            left: 8,
            delay: 10,
            dispose: gif::DisposalMethod::Keep,
            buffer: std::borrow::Cow::Owned(vec![1; 8 * 8]),
            ..gif::Frame::default()
        })
        .unwrap();
    drop(encoder);
    let sheet = gif_sprite_sheet(&path).unwrap();
    let img = image::load_from_memory(&sheet.png).unwrap();
    assert_eq!(img.get_pixel(100, 20)[3], 0);
    assert_eq!(img.get_pixel(100, 240), red);
    assert_eq!(img.get_pixel(612, 240), red);
    assert_eq!(img.get_pixel(900, 240), blue);
    assert_eq!((sheet.frames, sheet.fps), (2, 10));
}

#[test]
fn rejects_single_frame_gifs_and_clamps_animated_fps() {
    let root = tempdir().unwrap();
    let path = root.path().join("animated.gif");
    gif(&path, 1, 100);
    assert!(
        gif_sprite_sheet(&path)
            .unwrap_err()
            .to_string()
            .contains("at least two frames")
    );
    gif(&path, 2, 10);
    assert_eq!(gif_sprite_sheet(&path).unwrap().fps, 64);
    gif(&path, 2, 2000);
    assert_eq!(gif_sprite_sheet(&path).unwrap().fps, 1);
}

#[test]
fn extracts_frames_in_row_major_order() {
    let colors = [
        Rgba([255, 0, 0, 255]),
        Rgba([0, 255, 0, 255]),
        Rgba([0, 0, 255, 255]),
        Rgba([255, 255, 0, 255]),
    ];
    let sheet = DynamicImage::ImageRgba8(RgbaImage::from_fn(16, 16, |column, row| {
        colors[usize::try_from((row / 8) * 2 + column / 8).expect("the quadrant fits in usize")]
    }));
    for (color, index) in colors.into_iter().zip(0_u32..) {
        let frame = image_pipeline::frame_png(&sheet, 4, index, 8).unwrap();
        let frame = image::load_from_memory(&frame).unwrap();
        assert_eq!(frame.dimensions(), (8, 8));
        assert_eq!(frame.get_pixel(1, 1), color);
    }
}

#[test]
fn uses_sparse_grid_and_validates_frame_bounds() {
    assert_eq!(image_pipeline::grid(16).unwrap(), 4);
    assert_eq!(image_pipeline::grid(35).unwrap(), 8);
    assert_eq!(
        image_pipeline::frame_rect(16, 16, 6, 3).unwrap(),
        (12, 0, 4, 4)
    );
    assert_eq!(
        image_pipeline::frame_rect(16, 16, 6, 4).unwrap(),
        (0, 4, 4, 4)
    );
    assert_eq!(
        image_pipeline::frame_rect(16, 16, 6, 5).unwrap(),
        (4, 4, 4, 4)
    );
    assert!(image_pipeline::frame_rect(16, 16, 6, 6).is_err());
    assert!(image_pipeline::frame_rect(1, 1, 6, 1).is_err());
    assert!(image_pipeline::grid(0).is_err());
}

#[test]
fn rejects_animated_sources_for_static_emoji_before_upload() {
    let root = tempdir().unwrap();
    let paths = [
        root.path().join("animated.gif"),
        root.path().join("sheet_6frames_24fps.png"),
    ];
    gif(&paths[0], 3, 100);
    png(&paths[1], 16, 16);
    for path in paths {
        let error = UploadPayload::for_path(&path, UploadKind::Emoji)
            .err()
            .unwrap()
            .to_string();
        assert_eq!(
            error,
            "Animated emoji sources must be uploaded with Upload Animated Emoji."
        );
    }
}

#[test]
fn validates_animation_metadata_before_upload() {
    let root = tempdir().unwrap();
    for (name, error) in [
        ("sheet_1frames_24fps.png", "at least two"),
        ("sheet_65frames_24fps.png", "at most 64"),
        ("sheet_6frames_0fps.png", "positive frame rate"),
    ] {
        let path = root.path().join(name);
        png(&path, 16, 16);
        assert!(
            UploadPayload::for_path(&path, UploadKind::Animated)
                .err()
                .unwrap()
                .to_string()
                .contains(error)
        );
    }
}

#[test]
fn routes_gif_auto_upload_to_animated_png_with_filename_metadata() {
    let root = tempdir().unwrap();
    let path = root
        .path()
        .join("wave_spinanimationStyle_pingpongloopStyle.gif");
    gif(&path, 4, 100);
    let payload = UploadPayload::for_path(&path, UploadKind::AutoEmoji).unwrap();
    assert_eq!(payload.tag, ANIMATED);
    assert_eq!(payload.frames, Some(4));
    assert_eq!(payload.fps, Some(10));
    assert_eq!(payload.animation_style, Some("spin"));
    assert_eq!(payload.loop_style, Some("pingpong"));
    assert_eq!(image::guess_format(&payload.png).unwrap(), ImageFormat::Png);
}

#[test]
fn thumbnails_shrink_static_images_and_retain_gif_frames() {
    let root = tempdir().unwrap();
    let path = root.path().join("thumb.png");
    let bytes = png(&path, 400, 300);
    let thumb = image_pipeline::thumbnail(&bytes, None).unwrap();
    assert_eq!(thumb.content_type, "image/png");
    assert_eq!(
        image::load_from_memory(&thumb.data).unwrap().dimensions(),
        (240, 180)
    );
    let path = root.path().join("thumb.gif");
    gif(&path, 3, 100);
    let thumb = image_pipeline::thumbnail(&fs::read(path).unwrap(), None).unwrap();
    assert_eq!(thumb.content_type, "image/gif");
    let gif = GifDecoder::new(Cursor::new(thumb.data)).unwrap();
    assert_eq!(gif.into_frames().collect_frames().unwrap().len(), 3);
}

#[test]
fn thumbnail_cache_evicts_least_recent_items_and_retains_borrowed_images() {
    let cache = ImageCache::new(2.try_into().unwrap());
    let held = cache.insert("a".into(), ImageBytes::png(vec![1]));
    cache.insert("b".into(), ImageBytes::png(vec![2]));
    assert!(cache.get("a").is_some());
    cache.insert("c".into(), ImageBytes::png(vec![3]));
    assert!(cache.get("b").is_none());
    assert!(cache.get("a").is_some());
    cache.clear();
    assert!(cache.get("a").is_none());
    assert_eq!(held.data, [1]);
}
