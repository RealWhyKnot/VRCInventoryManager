#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod fixture;
#[cfg(windows)]
mod host;
mod logging;
mod routes;
mod state;
#[cfg(test)]
mod tests;

use std::{
    io::{self, Write as _},
    net::{Ipv4Addr, SocketAddr},
    sync::Arc,
};

use anyhow::{Context, Result, bail};
use fixture::Mode;
use tokio::net::TcpListener;
use vrcim_core::{log::report_diagnostic, paths::UserPaths};

pub(crate) const VERSION: &str = env!("VRCIM_VERSION");

#[derive(Default)]
struct Options {
    serve_only: bool,
    port: u16,
    fixture: Option<Mode>,
}

impl Options {
    fn parse(arguments: impl Iterator<Item = String>) -> Result<Self> {
        let mut options = Self::default();
        let mut arguments = arguments.peekable();
        while let Some(arg) = arguments.next() {
            match arg.as_str() {
                "--serve-only" => options.serve_only = true,
                "--port" => {
                    options.port = arguments
                        .next()
                        .context("Specify a port after --port.")?
                        .parse()
                        .context("The port must be a number from 0 to 65535.")?;
                }
                "--fixture" => {
                    let mode = arguments.next_if(|value| !value.starts_with("--"));
                    options.fixture = Some(
                        mode.map(|value| serde_json::from_value(value.into()))
                            .transpose()
                            .context("Choose full, empty, loading, error, update, or limit.")?
                            .unwrap_or_default(),
                    );
                }
                _ => bail!("Unknown option {arg}."),
            }
        }
        Ok(options)
    }
}

fn main() {
    if !std::env::args_os()
        .skip(1)
        .any(|argument| argument == "--fixture")
    {
        logging::install(
            std::env::current_exe().ok().as_deref(),
            &UserPaths::current().roaming(),
        );
    }
    if let Err(error) = run() {
        tracing::error!("VRCInventoryManager stopped: {error:#}");
        report_diagnostic(&format!("{error:#}"));
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    let arguments = std::env::args_os()
        .skip(1)
        .map(|argument| {
            argument
                .into_string()
                .ok()
                .context("Command-line arguments must be valid Unicode.")
        })
        .collect::<Result<Vec<_>>>()?;
    let options = Options::parse(arguments.into_iter())?;
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()?;
    let executable = std::env::current_exe()?;
    tracing::info!("VRCInventoryManager {VERSION} started.");
    let app = state::AppState::new(options.fixture, executable)?;
    let listener = runtime.block_on(TcpListener::bind(SocketAddr::from((
        Ipv4Addr::LOCALHOST,
        options.port,
    ))))?;
    let url = format!("http://{}/", listener.local_addr()?);
    runtime.spawn(Arc::clone(&app).startup());
    if options.serve_only {
        #[cfg(all(windows, not(debug_assertions)))]
        {
            use windows::Win32::System::Console::{ATTACH_PARENT_PROCESS, AttachConsole};
            // SAFETY: AttachConsole takes no pointers and only joins the parent's console.
            let _ = unsafe { AttachConsole(ATTACH_PARENT_PROCESS) };
        }
        writeln!(io::stdout().lock(), "{url}")?;
        let mut shutdown = app.shutdown.subscribe();
        runtime.block_on(async {
            tokio::select! {
                result = axum::serve(listener, routes::router(app)) => result?,
                _ = shutdown.wait_for(|&done| done) => {}
                signal = tokio::signal::ctrl_c() => signal?,
            }
            Ok(())
        })
    } else {
        #[cfg(windows)]
        {
            let router = routes::router(Arc::clone(&app));
            let server = runtime.spawn(async move { axum::serve(listener, router).await });
            let result = host::run(&url, app, runtime.handle().clone());
            server.abort();
            result.context("Could not start the desktop window.")
        }
        #[cfg(not(windows))]
        bail!("Use --serve-only on this platform.")
    }
}
