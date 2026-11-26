use std::{env, fmt::Write as _, fs, io, path::Path};

fn files(root: &Path, directory: &Path, output: &mut Vec<(String, String)>) -> io::Result<()> {
    for entry in fs::read_dir(directory)? {
        let entry = entry?;
        let path = entry.path();
        let kind = entry.file_type()?;
        if kind.is_dir() {
            files(root, &path, output)?;
        } else if kind.is_file() {
            let relative = path
                .strip_prefix(root)
                .expect("directory traversal starts at web/dist");
            output.push((
                relative.to_string_lossy().replace('\\', "/"),
                path.to_string_lossy().into_owned(),
            ));
        }
    }
    Ok(())
}

fn main() -> io::Result<()> {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    let web = root.join("web/dist");
    let version_path = root.join("version.txt");
    let icon = root.join("assets/AppIcon.ico");
    println!("cargo:rerun-if-changed={}", web.display());
    println!("cargo:rerun-if-changed={}", version_path.display());
    println!("cargo:rerun-if-changed={}", icon.display());
    println!("cargo:rerun-if-env-changed=VRCINVENTORYMANAGER_VERSION");
    let version = env::var("VRCINVENTORYMANAGER_VERSION")
        .ok()
        .or_else(|| fs::read_to_string(&version_path).ok())
        .unwrap_or_else(|| "0.0.0.0-dev".into());
    let version = version.trim();
    println!("cargo:rustc-env=VRCIM_VERSION={version}");

    let mut assets = Vec::new();
    for name in ["index.html", "app.css", "app.js"] {
        if !web.join(name).is_file() {
            return Err(io::Error::new(
                io::ErrorKind::NotFound,
                "Frontend bundle missing. Run npm ci and npm run build.",
            ));
        }
    }
    files(&web, &web, &mut assets)?;
    assets.sort();
    let mut code = String::from(
        "pub(crate) fn embedded_asset(path: &str) -> Option<&'static [u8]> { match path {\n",
    );
    for (name, path) in assets {
        writeln!(code, "{name:?} => Some(include_bytes!({path:?})),").map_err(io::Error::other)?;
    }
    code.push_str("_ => None,\n} }\n");
    let out = env::var_os("OUT_DIR").expect("Cargo sets OUT_DIR for build scripts");
    fs::write(Path::new(&out).join("web_assets.rs"), code)?;

    if env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let numeric = version
            .trim_start_matches(['v', 'V'])
            .split(['-', '+'])
            .next()
            .unwrap_or_default()
            .split('.')
            .try_fold(0_u64, |packed, part| {
                part.parse::<u16>()
                    .map(|part| (packed << 16) | u64::from(part))
            })
            .unwrap_or_default();
        winresource::WindowsResource::new()
            .set_icon(icon.to_str().expect("the application icon path uses UTF-8"))
            .set("ProductName", "VRCInventoryManager")
            .set("FileDescription", "VRCInventoryManager")
            .set("FileVersion", version)
            .set("ProductVersion", version)
            .set_version_info(winresource::VersionInfo::FILEVERSION, numeric)
            .set_version_info(winresource::VersionInfo::PRODUCTVERSION, numeric)
            .compile()?;
    }
    Ok(())
}
