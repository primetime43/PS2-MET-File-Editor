# EVT facial-event and lip-sync format

Backyard Baseball stores 2,169 UTF-8 EVT event streams in `DATA.MET`. The editor recognizes three
retail uses:

| Files | Classes | Purpose |
| ---: | --- | --- |
| 1,608 | `CLASS_TALKIES` | Timed lip-sync visemes paired with same-name VAG dialogue |
| 560 | `CLASS_EYES`, `CLASS_MOUTH` | Numbered facial poses attached to batting animations |
| 1 | `CLASS_EYES` | Commentator blink timeline |

Talkie types are `STATIC`, `AI`, `EE`, `OH`, `OO`, `CDG`, `MM`, `FV`, and the
occasional retail `ROOT` sentinel. Each event stores a timestamp in seconds, class, type, numeric
value, and element ID.

## Editor

Open **Facial Event Editor...** on the Game Tools tab. If an EVT is selected in the DATA.MET
browser, that file opens automatically; double-clicking an EVT opens the editor directly.

The editor provides:

- Search and separate talkie/animation filters.
- An editable timestamp, class, type, value, and element-ID grid.
- Add, delete, per-file reset, and reset-all actions.
- A textured 3D head/upper-body preview and color-coded event timeline. Talkies resolve Abner and
  Sunny's commentator models or match roster speaker names to their player models (`ichiro` to
  `suzu`, `jeter` to `jete`, and so on). Batting EVT files resolve the matching player's batting
  model and animation.
- The character's actual eye and mouth PNGs, loaded directly from `DATA.MET` and applied to the model.
- Synchronized playback of the matching VAG for talkie files.
- A drawn face or texture-strip fallback when no compatible DFF/HAnim model is available.
- Validation of event types, non-negative values, and ascending timestamps within each facial class.
- A warning when a talkie event extends past its VAG duration.
- One timestamped `DATA.MET` backup when changes are saved.

Numbered batting events map directly to the PNG number in the same character directory:

```text
CLASS_EYES  pose N  ->  *_eyes_tx.NNN.png
CLASS_MOUTH pose N  ->  *_mouth_tx.NNN.png
```

For example, eye pose `3` uses `*_eyes_tx.003.png`. Pose counts are character-specific: most players
have 10 eye poses and 19 mouth poses, while some have more and Mr. Clanky has only eye textures.
The editor shows a missing-asset message when an EVT references a PNG that is not present.

Talkie names use the retail game's bit-to-slot mapping:

```text
STATIC / INVALID / ROOT -> mouth .001
AI                      -> mouth .002
EE                      -> mouth .003
OH                      -> mouth .004
OO                      -> mouth .005
CDG                     -> mouth .006
MM                      -> mouth .007
FV                      -> mouth .008
```

This mapping was recovered from `BaseballYaga::LipSync::Raise` in `SLUS_208.65` at virtual address
`0x001D64B0`. The function maps event values `1, 2, 4, 8, 16, 32, 64, 128` to zero-based texture
indices `0..7`; the corresponding archive filenames are numbered `.001` through `.008`.

The retail archive contains 68 talkie speaker folders. Forty-eight have a fixed character and resolve
to a loadable textured model. The other 20 are the `customboy_*` and `customgirl_*` voice packs. A
voice pack can be assigned to many created players with different appearance settings, so an EVT file
alone cannot identify one correct custom-player head; those files deliberately retain the drawn
phoneme fallback.

## Retail pseudo-XML quirk

Talkie EVT files use ordinary child elements:

```xml
<value value="1.0"/>
<elementID value="0"/>
```

The 560 batting files omit the opening `<` on those two records:

```text
value value="1.0"/>
elementID value="0"/>
```

They are therefore XML text nodes rather than normal elements. The editor deliberately parses and
rewrites this shipped syntax without normalizing it. Batting timestamps also retain up to seven
decimal places, and eye/mouth sequences are validated independently because each class can restart
at time zero inside the same file.
