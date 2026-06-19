//! iii-bridge — godot-rust (gdext) extension exposing an `IiiClient` Godot node
//! that talks to the iii engine.
//!
//! Threading rule (the spike that's already proven): async work runs on a tokio
//! runtime owned by this extension; it NEVER touches Godot. Results are pushed
//! through an mpsc channel and drained on the MAIN thread in `process()`, where
//! the `response` signal is emitted.

use godot::prelude::*;
use iii_sdk::{register_worker, InitOptions, TriggerRequest, III};
use serde_json::json;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::OnceLock;
use tokio::runtime::Runtime;

struct IiiBridgeExtension;

#[gdextension]
unsafe impl ExtensionLibrary for IiiBridgeExtension {}

/// Process-wide multi-threaded tokio runtime for driving `trigger().await`.
/// (The iii client maintains its own connection thread/runtime separately.)
fn runtime() -> &'static Runtime {
    static RT: OnceLock<Runtime> = OnceLock::new();
    RT.get_or_init(|| Runtime::new().expect("failed to start tokio runtime"))
}

#[derive(GodotClass)]
#[class(base=Node)]
struct IiiClient {
    base: Base<Node>,
    url: GString,
    iii: Option<III>,
    // Plain Rust strings only — GString (a Godot type) must never be built/touched
    // off the main thread, so the channel carries String and process() converts.
    tx: Sender<(String, String)>,
    rx: Receiver<(String, String)>,
}

#[godot_api]
impl IiiClient {
    /// Emitted on the main thread when a request resolves.
    #[signal]
    fn response(id: GString, payload: GString);

    #[func]
    fn set_url(&mut self, url: GString) {
        self.url = url;
    }

    /// Eagerly connect to the engine (otherwise the first `request` connects).
    #[func]
    fn connect_engine(&mut self) {
        let _ = self.ensure_client();
    }

    /// Invoke `function_id` with `payload_json` (a JSON string built on the Godot
    /// side, e.g. `{"prompt":"a red car"}`). The FULL JSON result is returned via
    /// the `response` signal as a string for the caller to `JSON.parse`. `id` is an
    /// opaque correlation token echoed back. Errors come back as `{"error":"..."}`.
    #[func]
    fn request(&mut self, id: GString, function_id: GString, payload_json: GString) {
        let iii = self.ensure_client().clone();
        let tx = self.tx.clone();
        let id = id.to_string();
        let fid = function_id.to_string();
        let payload_str = payload_json.to_string();
        runtime().spawn(async move {
            let payload: serde_json::Value =
                serde_json::from_str(&payload_str).unwrap_or_else(|_| json!({}));
            let out = match iii
                .trigger(TriggerRequest {
                    function_id: fid,
                    payload,
                    action: None,
                    timeout_ms: Some(300_000),
                })
                .await
            {
                Ok(v) => v.to_string(),
                Err(e) => json!({ "error": e.to_string() }).to_string(),
            };
            // Off-thread: only plain Rust strings cross back to the main thread.
            let _ = tx.send((id, out));
        });
    }
}

impl IiiClient {
    /// Lazily create + connect the iii client (connection runs on its own thread).
    fn ensure_client(&mut self) -> &III {
        if self.iii.is_none() {
            let url = self.url.to_string();
            self.iii = Some(register_worker(&url, InitOptions::default()));
        }
        self.iii.as_ref().expect("client just initialized")
    }
}

#[godot_api]
impl INode for IiiClient {
    fn init(base: Base<Node>) -> Self {
        let (tx, rx) = channel();
        Self {
            base,
            url: GString::from("ws://127.0.0.1:49134"),
            iii: None,
            tx,
            rx,
        }
    }

    fn process(&mut self, _delta: f64) {
        // Main thread: now it's safe to build Godot strings and emit.
        while let Ok((id, payload)) = self.rx.try_recv() {
            self.signals()
                .response()
                .emit(&GString::from(id.as_str()), &GString::from(payload.as_str()));
        }
    }
}
