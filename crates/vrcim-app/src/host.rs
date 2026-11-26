use std::{ffi::c_void, path::PathBuf, sync::Arc};

use anyhow::{Context, Result, ensure};
use pageframe::{
    events::{
        Code, ImeEvent, Key, KeyEvent, KeyState, Modifiers, MouseEventButton, NamedKey, WheelDelta,
    },
    host::{ColorScheme, HostConfig, HostEvent, HostInputEvent, PageframeHost, Url, same_origin},
};
use windows::Win32::{
    Foundation::{COLORREF, HWND},
    Graphics::{
        Dwm::{DWMWA_USE_IMMERSIVE_DARK_MODE, DwmSetWindowAttribute},
        Gdi::CreateSolidBrush,
    },
    UI::WindowsAndMessaging::{GCLP_HBRBACKGROUND, SetClassLongPtrW},
};
use winit::{
    application::ApplicationHandler,
    data_transfer::{DataTransferSendBuilder, TypeHint},
    dpi::{LogicalPosition, LogicalSize, PhysicalPosition},
    event::{ElementState, Ime, MouseButton, MouseScrollDelta, WindowEvent},
    event_loop::{
        ActiveEventLoop, AsyncRequestSerial, ControlFlow, DndAction, EventLoop,
        run_on_demand::EventLoopExtRunOnDemand,
    },
    icon::RgbaIcon,
    keyboard::{Key as WindowKey, ModifiersState, PhysicalKey},
    raw_window_handle::{HasWindowHandle, RawWindowHandle},
    window::{
        ImeCapabilities, ImeEnableRequest, ImeRequest, ImeRequestData, Theme, Window,
        WindowAttributes, WindowId,
    },
};

use crate::state::App;

type View = PageframeHost<Box<dyn Window>>;

pub(crate) fn run(url: &str, app: App, runtime: tokio::runtime::Handle) -> Result<()> {
    let mut event_loop = EventLoop::new()?;
    let url = Url::parse(url)?;
    let mut host = Desktop {
        app,
        runtime,
        url,
        view: None,
        error: None,
        mods: Modifiers::empty(),
        drop_request: None,
        clipboard_request: None,
    };
    event_loop.run_app_on_demand(&mut host)?;
    host.error.map_or(Ok(()), Err)
}

struct Desktop {
    app: App,
    runtime: tokio::runtime::Handle,
    url: Url,
    view: Option<View>,
    error: Option<anyhow::Error>,
    mods: Modifiers,
    drop_request: Option<AsyncRequestSerial>,
    clipboard_request: Option<AsyncRequestSerial>,
}

impl Desktop {
    fn open(&mut self, event_loop: &dyn ActiveEventLoop) -> Result<()> {
        let icon_bytes = include_bytes!("../../../assets/AppIcon.ico");
        let pixels =
            image::load_from_memory_with_format(icon_bytes, image::ImageFormat::Ico)?.into_rgba8();
        let (width, height) = pixels.dimensions();
        let icon = RgbaIcon::new(pixels.into_raw(), width, height)?;
        let attributes = WindowAttributes::default()
            .with_title("VRCInventoryManager")
            .with_surface_size(LogicalSize::new(1360.0, 800.0))
            .with_min_surface_size(LogicalSize::new(1024.0, 640.0))
            .with_visible(false)
            .with_theme(Some(Theme::Dark))
            .with_window_icon(Some(icon.into()));
        let window = Arc::new(event_loop.create_window(attributes)?);
        if let Some(monitor) = window
            .current_monitor()
            .or_else(|| window.primary_monitor())
            && let (Some(at), Some(mode)) = (monitor.position(), monitor.current_video_mode())
        {
            let display = mode.size();
            let size = window.outer_size();
            let middle = |available: u32, used| {
                i32::try_from(available.saturating_sub(used) / 2).unwrap_or_default()
            };
            window.set_outer_position(
                PhysicalPosition::new(
                    at.x + middle(display.width, size.width),
                    at.y + middle(display.height, size.height),
                )
                .into(),
            );
        }
        dark_window(window.as_ref().as_ref());
        let size = window.surface_size();
        let scale = window.scale_factor();
        let proxy = event_loop.create_proxy();
        let shutdown_proxy = proxy.clone();
        let mut shutdown = self.app.shutdown.subscribe();
        self.runtime.spawn(async move {
            if shutdown.wait_for(|&done| done).await.is_ok() {
                shutdown_proxy.wake_up();
            }
        });
        let config = HostConfig::url(self.url.clone())
            .size(
                f64::from(size.width) / scale,
                f64::from(size.height) / scale,
            )
            .scale_factor(scale)
            .color_scheme(ColorScheme::Dark)
            .wake(move || proxy.wake_up());
        let mut view =
            PageframeHost::new(Arc::clone(&window), config).context("Couldn't start Pageframe.")?;
        ensure!(
            view.is_active(),
            "Couldn't start the Pageframe graphics surface."
        );
        let base = self.url.clone();
        view.session_mut()
            .on_navigation(move |url| same_origin(&base, url));
        view.request_redraw();
        window.set_visible(true);
        window.request_redraw();
        self.view = Some(view);
        Ok(())
    }

    fn pump(&mut self, event_loop: &dyn ActiveEventLoop) {
        let shutdown = *self.app.shutdown.borrow();
        if shutdown {
            self.view.take();
            event_loop.exit();
            return;
        }
        let Some(view) = self.view.as_mut() else {
            return;
        };
        if view.poll() {
            view.window().request_redraw();
        }
        for event in view.session_mut().take_events() {
            match event {
                HostEvent::CursorChanged(icon) => {
                    view.window().set_cursor(icon.unwrap_or_default().into());
                }
                HostEvent::ImeEnabled(enabled) => {
                    let request = if enabled {
                        ImeRequest::Enable(
                            ImeEnableRequest::new(
                                ImeCapabilities::new().with_cursor_area(),
                                ImeRequestData::default().with_cursor_area(
                                    LogicalPosition::new(0, 0).into(),
                                    LogicalSize::new(1, 1).into(),
                                ),
                            )
                            .expect("cursor area matches enabled IME capabilities"),
                        )
                    } else {
                        ImeRequest::Disable
                    };
                    let _ = view.window().request_ime_update(request);
                }
                HostEvent::ImeCursorArea {
                    x: left,
                    y: top,
                    width,
                    height,
                } => {
                    let _ = view.window().request_ime_update(ImeRequest::Update(
                        ImeRequestData::default().with_cursor_area(
                            LogicalPosition::new(left, top).into(),
                            LogicalSize::new(width, height).into(),
                        ),
                    ));
                }
                HostEvent::ClipboardRequested => {
                    self.clipboard_request = event_loop.clipboard().ok().flatten().and_then(|id| {
                        event_loop
                            .fetch_data_transfer(id, &TypeHint::Plaintext)
                            .ok()
                    });
                }
                HostEvent::ClipboardText(text) => {
                    let data = DataTransferSendBuilder::new(text)
                        .with_type(TypeHint::Plaintext, |text, _| Some(text.clone()))
                        .build();
                    let _ = event_loop.set_clipboard(data);
                }
                HostEvent::LoadFailed { .. } => {
                    tracing::error!("Pageframe could not load a page.");
                }
                HostEvent::CloseRequested => {
                    self.view.take();
                    event_loop.exit();
                    return;
                }
                _ => {}
            }
        }
        event_loop.set_control_flow(
            view.next_wake()
                .map_or(ControlFlow::Wait, ControlFlow::WaitUntil),
        );
    }

    fn input(&mut self, event: WindowEvent) {
        let Some(view) = self.view.as_mut() else {
            return;
        };
        let input = match event {
            WindowEvent::RedrawRequested => {
                view.redraw();
                return;
            }
            WindowEvent::SurfaceResized(size) => HostInputEvent::Resized {
                width: size.width,
                height: size.height,
            },
            WindowEvent::ScaleFactorChanged { scale_factor, .. } => {
                HostInputEvent::ScaleFactorChanged(scale_factor)
            }
            WindowEvent::Occluded(hidden) => {
                if view.set_hidden(hidden) {
                    view.window().request_redraw();
                }
                return;
            }
            WindowEvent::PointerMoved {
                position,
                primary: true,
                ..
            } => HostInputEvent::PointerMoved {
                x: position.x,
                y: position.y,
            },
            WindowEvent::PointerLeft { primary: true, .. } => HostInputEvent::PointerLeft,
            WindowEvent::PointerButton {
                button,
                position,
                state,
                primary: true,
                ..
            } => {
                let button = match button.mouse_button() {
                    Some(MouseButton::Left) => MouseEventButton::Main,
                    Some(MouseButton::Right) => MouseEventButton::Secondary,
                    Some(MouseButton::Middle) => MouseEventButton::Auxiliary,
                    Some(MouseButton::Back) => MouseEventButton::Fourth,
                    Some(MouseButton::Forward) => MouseEventButton::Fifth,
                    _ => return,
                };
                if state.is_pressed() {
                    HostInputEvent::PointerDown {
                        x: position.x,
                        y: position.y,
                        button,
                    }
                } else {
                    HostInputEvent::PointerUp {
                        x: position.x,
                        y: position.y,
                        button,
                    }
                }
            }
            WindowEvent::MouseWheel { delta, .. } => HostInputEvent::Wheel(match delta {
                MouseScrollDelta::LineDelta(columns, lines) => {
                    WheelDelta::Lines(f64::from(columns), f64::from(lines))
                }
                MouseScrollDelta::PixelDelta(at) => WheelDelta::Pixels(at.x, at.y),
                _ => return,
            }),
            WindowEvent::ModifiersChanged(modifiers) => {
                self.mods = map_mods(modifiers.state());
                HostInputEvent::ModifiersChanged(self.mods)
            }
            WindowEvent::KeyboardInput { event, .. } => key_input(event, self.mods),
            WindowEvent::Ime(ime) => HostInputEvent::Ime(match ime {
                Ime::Commit(text) => ImeEvent::Commit(text),
                Ime::Preedit(text, cursor) => ImeEvent::Preedit(text, cursor),
                Ime::Enabled => ImeEvent::Enabled,
                Ime::Disabled => ImeEvent::Disabled,
                Ime::DeleteSurrounding {
                    before_bytes,
                    after_bytes,
                } => ImeEvent::DeleteSurrounding {
                    before_bytes,
                    after_bytes,
                },
            }),
            WindowEvent::Focused(focused) => HostInputEvent::Focused(focused),
            _ => return,
        };
        if view.handle_input(input) {
            view.window().request_redraw();
        }
    }

    fn file_drag(&mut self, active: bool) {
        if let Some(view) = &mut self.view {
            view.session_mut().eval(&format!(
                "window.dispatchEvent(new CustomEvent('vrcim:file-drag', {{ detail: {active} }}))"
            ));
            view.window().request_redraw();
        }
    }

    fn file_drop(&mut self, paths: &[PathBuf]) {
        if let Some(view) = &mut self.view {
            let paths = serde_json::to_string(&paths).expect("paths from file URIs");
            view.session_mut().eval(&format!(
                "window.dispatchEvent(new CustomEvent('vrcim:file-drop', {{ detail: {paths} }}))"
            ));
            view.window().request_redraw();
        }
    }
}

impl ApplicationHandler for Desktop {
    fn can_create_surfaces(&mut self, event_loop: &dyn ActiveEventLoop) {
        if let Some(view) = &mut self.view {
            view.resume();
        } else if let Err(error) = self.open(event_loop) {
            self.error = Some(error);
            event_loop.exit();
        }
    }

    fn destroy_surfaces(&mut self, _: &dyn ActiveEventLoop) {
        if let Some(view) = &mut self.view {
            view.suspend();
        }
    }

    fn window_event(&mut self, event_loop: &dyn ActiveEventLoop, _: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => {
                self.view.take();
                event_loop.exit();
            }
            WindowEvent::DragEntered { id, .. } => {
                let files = event_loop
                    .data_transfer(id)
                    .is_ok_and(|data| data.has_type(&TypeHint::UriList));
                if files
                    && event_loop
                        .set_valid_dnd_actions(id, &[DndAction::Copy])
                        .is_ok()
                {
                    self.file_drag(true);
                }
            }
            WindowEvent::DragLeft { .. } => {
                self.file_drag(false);
            }
            WindowEvent::DragDropped { id, .. } => {
                self.file_drag(false);
                self.drop_request = event_loop.fetch_data_transfer(id, &TypeHint::UriList).ok();
                if self.drop_request.is_none() {
                    self.file_drop(&[]);
                }
            }
            WindowEvent::DataTransferReceived { serial, value, .. }
                if self.drop_request == Some(serial) =>
            {
                let received = value.try_as_uris();
                if received
                    .as_ref()
                    .is_err_and(|error| error.kind() == std::io::ErrorKind::WouldBlock)
                {
                    return;
                }
                self.drop_request = None;
                let paths: Vec<PathBuf> = received
                    .unwrap_or_default()
                    .into_iter()
                    .filter_map(|uri| Url::parse(&uri).ok()?.to_file_path().ok())
                    .take(257)
                    .collect();
                self.file_drop(&paths);
            }
            WindowEvent::DataTransferReceived { serial, value, .. }
                if self.clipboard_request == Some(serial) =>
            {
                let received = value.try_as_string();
                if received
                    .as_ref()
                    .is_err_and(|error| error.kind() == std::io::ErrorKind::WouldBlock)
                {
                    return;
                }
                self.clipboard_request = None;
                if let (Ok(text), Some(view)) = (received, self.view.as_mut())
                    && view.handle_input(HostInputEvent::Paste(text))
                {
                    view.window().request_redraw();
                }
            }
            other => self.input(other),
        }
    }

    fn proxy_wake_up(&mut self, event_loop: &dyn ActiveEventLoop) {
        self.pump(event_loop);
    }

    fn about_to_wait(&mut self, event_loop: &dyn ActiveEventLoop) {
        self.pump(event_loop);
    }
}

fn dark_window(window: &dyn Window) {
    if let Ok(handle) = window.window_handle()
        && let RawWindowHandle::Win32(handle) = handle.as_raw()
    {
        let hwnd = HWND(std::ptr::with_exposed_provenance_mut::<c_void>(
            handle.hwnd.get().cast_unsigned(),
        ));
        let enabled = 1_i32;
        let enabled_size =
            u32::try_from(std::mem::size_of_val(&enabled)).expect("an i32 is four bytes wide");
        // SAFETY: hwnd is this live window, and enabled_size is the byte size of enabled.
        let _ = unsafe {
            DwmSetWindowAttribute(
                hwnd,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                (&raw const enabled).cast(),
                enabled_size,
            )
        };
        // SAFETY: CreateSolidBrush takes a plain color value and returns a new brush or null.
        let brush = unsafe { CreateSolidBrush(COLORREF(0x13_0E_0B)) };
        if !brush.0.is_null() {
            let brush_handle = brush.0.expose_provenance().cast_signed();
            // SAFETY: hwnd is this live window and the class keeps the brush for its lifetime.
            unsafe { SetClassLongPtrW(hwnd, GCLP_HBRBACKGROUND, brush_handle) };
        }
    }
}

fn map_mods(state: ModifiersState) -> Modifiers {
    let mut modifiers = Modifiers::empty();
    modifiers.set(Modifiers::SHIFT, state.shift_key());
    modifiers.set(Modifiers::CONTROL, state.control_key());
    modifiers.set(Modifiers::ALT, state.alt_key());
    modifiers.set(Modifiers::META, state.meta_key());
    modifiers
}

fn key_input(event: winit::event::KeyEvent, modifiers: Modifiers) -> HostInputEvent {
    let key = match event.logical_key {
        WindowKey::Named(key) => Key::Named(key),
        WindowKey::Character(text) => Key::Character(text.to_string()),
        WindowKey::Unidentified(_) => Key::Named(NamedKey::Unidentified),
        WindowKey::Dead(_) => Key::Named(NamedKey::Dead),
    };
    let code = match event.physical_key {
        PhysicalKey::Code(code) => code,
        PhysicalKey::Unidentified(_) => Code::Unidentified,
    };
    let key = KeyEvent {
        key,
        code,
        modifiers,
        location: event.location,
        is_auto_repeating: event.repeat,
        is_composing: false,
        state: if event.state == ElementState::Pressed {
            KeyState::Pressed
        } else {
            KeyState::Released
        },
        text: event.text,
    };
    if event.state == ElementState::Pressed {
        HostInputEvent::KeyDown(key)
    } else {
        HostInputEvent::KeyUp(key)
    }
}
