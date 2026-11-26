use super::{Duration, Mode, app};

#[test]
fn pageframe_reads_and_mutates_fixture_api_without_a_window() {
    use pageframe::host::{ColorScheme, HostConfig, HostSession, Url};
    use std::{cell::RefCell, rc::Rc, time::Instant};

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .unwrap();
    let listener = runtime
        .block_on(tokio::net::TcpListener::bind("127.0.0.1:0"))
        .unwrap();
    let url = Url::parse(&format!("http://{}/", listener.local_addr().unwrap())).unwrap();
    let router = app(Mode::Empty);
    let _server = runtime.spawn(async move { axum::serve(listener, router).await });
    let mut session =
        HostSession::new(HostConfig::url(url).color_scheme(ColorScheme::Dark)).unwrap();
    let posts = Rc::new(RefCell::new(Vec::new()));
    let collected = Rc::clone(&posts);
    session.on_post_message(move |message| collected.borrow_mut().push(message.to_owned()));
    session.eval(
        r#"
        fetch('/api/state').then(response => response.json()).then(async state => {
            const settings = document.querySelector('#studio-actions [data-key="settings"]');
            settings.dispatchEvent(new MouseEvent('click'));
            const settingsOpen = document.getElementById('settings-dialog').open;
            const response = await fetch('/api/local/select-root', {
                method: 'POST', headers: {'Content-Type': 'application/json'},
                body: JSON.stringify({kind:'photos'})
            });
            const next = await response.json();
            ipc.postMessage(
                ['fixture', state.selectedRootKind, next.selectedRootKind, settingsOpen].join(':')
            );
        }).catch(error => ipc.postMessage('fixture-error:' + error.message));
    "#,
    );
    let deadline = Instant::now() + Duration::from_secs(10);
    while posts.borrow().is_empty() && Instant::now() < deadline {
        session.poll();
        std::thread::sleep(Duration::from_millis(10));
    }
    assert!(
        posts
            .borrow()
            .iter()
            .any(|message| message == "fixture:emoji:photos:true"),
        "{:?}. Script errors: {:?}",
        posts.borrow(),
        session.document_mut().take_js_errors()
    );
}
