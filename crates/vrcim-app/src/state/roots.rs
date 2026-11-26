use std::path::Path;

use serde::Serialize;
use vrcim_core::paths::KnownFolders;

use super::same_path;

#[derive(Clone, Copy, Debug, serde::Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub(crate) enum RootKind {
    Photos,
    #[serde(alias = "emojis")]
    Emoji,
    Stickers,
    Prints,
    Custom,
}

impl RootKind {
    pub(crate) fn path(self, folders: &KnownFolders) -> Option<&Path> {
        Some(match self {
            Self::Photos => &folders.photos,
            Self::Emoji => &folders.emoji,
            Self::Stickers => &folders.stickers,
            Self::Prints => &folders.prints,
            Self::Custom => return None,
        })
    }

    pub(super) fn from_path(path: &Path, folders: &KnownFolders) -> Self {
        [Self::Photos, Self::Emoji, Self::Stickers, Self::Prints]
            .into_iter()
            .find(|kind| {
                kind.path(folders)
                    .is_some_and(|known| same_path(path, known))
            })
            .unwrap_or(Self::Custom)
    }
}
