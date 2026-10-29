# Lek: where its assets come from

Every file under `Client/wwwroot/games/` that is not code written for Lek, with its source and licence. The code itself
is Nova Globen's, like the rest of this repository.

| Files | Source | Author | Licence |
|---|---|---|---|
| `elementals/music/*.mp3`, `elementals/sfx/*.mp3` | OpenGameArt and Kenney, one row per pack in [`elementals/music/CREDITS.md`](Client/wwwroot/games/elementals/music/CREDITS.md) | CodeManu, nene, Synth-thetic, Emma_MA, Spring Spring, artisticdude, bart, Kenney | CC0 1.0 |
| `elementals/music/desert.mp3` | "Curse of the Scarab", incompetech.com | Kevin MacLeod | CC BY 4.0: the credit is shown on the Elementals menu (`elementals/index.html`, `elementals/classic.html`), the only games that play it |
| `elementals/portraits/cactus.png`, `elementals/portraits/ice.png` | Made for Nova Globen | Nova Globen AB | Nova Globen's own |
| `noir/models/*.glb` | Toon Shooter Game Kit, quaternius.com, via Poly Pizza; see [`noir/models/CREDITS.md`](Client/wwwroot/games/noir/models/CREDITS.md) | Quaternius | CC0 1.0 |
| `noir/vendor/three.module.min.js`, `noir/vendor/addons/` | three.js | three.js authors | MIT, [`noir/vendor/LICENSE`](Client/wwwroot/games/noir/vendor/LICENSE) |

Loaded from another site at run time, not kept here: the SignalR browser client (Microsoft, MIT) from cdnjs. No font is
fetched from another site: a game's typeface is used when the device has it, system fonts otherwise.
