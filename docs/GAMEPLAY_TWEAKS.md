# Gameplay tweaks and physics presets

**Game Tools → Gameplay Tweaks** exposes 285 supported values from the game's retail INI files. The
raw category tabs remain editable, while **Quick Presets** stage useful combinations without hiding
the individual values.

A preset does not immediately modify `DATA.MET`. Selecting one shows its description, the number of
values it will change, and a preview of the first affected settings. **Apply Preset** stages those
values in the grids. They are written only after selecting **Save to DATA.MET** and confirming the
timestamped backup.

## Preset groups

| Group | Included presets |
| --- | --- |
| Ball Size | Tiny Ball, Large Ball, Huge Arcade Ball, Restore Ball Size |
| Bounce & Rolling | Super Bouncy Fields, Pinball Physics, Long Rolling Ball, Heavy Dead Ball, Low Bounce, Restore Physics |
| Bunts & Normal Hits | Powerful Bunts, Bunt Home-Run Experiment, Stronger Contact, Weak Contact, Wild Contact, Restore Hits |
| Special Hits | Overpowered, Tamed, and Restore Special Hits |
| Catching | Normal Logic, Guaranteed Catches, Drop Every Catch, Hard-to-Catch Physics |
| Complete Game Styles | Arcade Chaos, Big-Ball Slugfest, Defense Challenge, Restore All Loaded Values |

The restore presets use the values present when the archive was opened. This makes experimenting
reversible without assuming that the input archive is an untouched retail copy.

## Physics controls

The presets use settings already loaded by the retail game:

| Source | Setting | Effect |
| --- | --- | --- |
| `ball.ini` | `Radius` | Ball collision size; retail value is `7` |
| `ball.ini` | `CollisionEfficiency` | Global retained velocity after a collision |
| `ball.ini` | `Friction` | Global collision/rolling resistance |
| `fields.ini` | `CollisionEfficiency` | Per-stadium surface bounce response |
| `fields.ini` | `Friction` | Per-stadium rolling resistance |
| `fields.ini` | `MinBounceSpeed` | Speed below which the ball stops making another bounce |
| `fields.ini` | `MinRollSpeed` | Speed below which rolling stops |

Lower friction and lower minimum speeds allow the ball to keep moving longer. Higher collision
efficiency retains more impact velocity. Values above `1.0`, used only by the experimental Pinball
preset, can add energy and produce intentionally exaggerated behavior.

## Hit controls

`bat.ini` has independent sections for Bunt, Crazy Bunt, Grounder, Line Drive, Power, and each
special hit. The presets combine the following controls:

- `BasePower`: base launch velocity;
- `BatterPower`: contribution from the player's batting rating;
- `RandomPower`: random power variation;
- `BaseAngle`, `MinVAngle`, and `MaxVAngle`: launch direction;
- `TopPower`, `BottomPower`, and `SidePower`: contact-location multipliers;
- `HorizontalBuntDispersion`: left/right bunt variation.

The **Bunt Home-Run Experiment** is deliberately extreme and is intended for testing custom home-run
boundaries and event behavior. **Powerful Bunts** is the more practical longer-bunt preset.

## Catch controls

Normal catch probability is still determined by gameplay and player fielding/reaction values. The
catching presets can either preserve that logic while changing the ball physics, or use the game's
retail debug switches:

- `AlwaysCatch=True` forces catch attempts to succeed;
- `AlwaysMiss=True` forces catch attempts to fail;
- setting both to `False` restores normal catch logic.

For a less artificial challenge, use **Hard-to-Catch Physics** and adjust individual fielding or
reaction ratings in the Player Editor instead of enabling `AlwaysMiss`.
