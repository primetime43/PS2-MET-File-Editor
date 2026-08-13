# Team and league setup

**Game Tools → Team and League Setup** edits the six division lists used when Backyard Baseball
creates a new season. The configuration is stored in `data/options/menuoptions.ini` under `[Season]`.

The editor supports:

- moving a club among AL West, AL Central, AL East, NL West, NL Central, and NL East;
- marking a club active or inactive;
- changing the order of clubs inside each active or inactive division list;
- filtering the table by division or status;
- restoring either the currently loaded setup or the retail 30-team alignment; and
- saving the modified INI back to `DATA.MET` with one timestamped backup.

The active and inactive lists are both real game structures. The executable's
`SeasonOptions::MakeTeamActive` and `SeasonOptions::MakeTeamInactive` functions move an `ETeamID`
between those lists. Unknown/custom IDs already present in a modded INI are retained and shown as
unknown rather than removed. **Restore Retail Alignment** intentionally replaces those lists with the
30 retail MLB clubs.

## Stored format

Each division has an active count, an inactive count, and zero-padded indexed entries. For example:

```ini
ALWestActiveCount = 4
ALWestInactiveCount = 0
ALWestActive00 = 10 ;kMariners
ALWestActive01 = 0 ;kAngels
ALWestActive02 = 12 ;kRangers
ALWestActive03 = 9 ;kAthletics
```

The editor regenerates only this managed division block. Other `menuoptions.ini` sections and the
commented retail `Team`, `Field`, and controller defaults are preserved. Counts and indexed entries
are regenerated together, so a moved or deactivated club cannot leave a stale count behind.

## Team IDs

The retail `ETeamID` range is 0 through 29:

| ID | Club | ID | Club |
|---:|---|---:|---|
| 0 | Anaheim Angels | 15 | Atlanta Braves |
| 1 | Baltimore Orioles | 16 | Chicago Cubs |
| 2 | Boston Red Sox | 17 | Cincinnati Reds |
| 3 | Chicago White Sox | 18 | Colorado Rockies |
| 4 | Cleveland Indians | 19 | Florida Marlins |
| 5 | Detroit Tigers | 20 | Houston Astros |
| 6 | Kansas City Royals | 21 | Los Angeles Dodgers |
| 7 | Minnesota Twins | 22 | Milwaukee Brewers |
| 8 | New York Yankees | 23 | Montreal Expos |
| 9 | Oakland Athletics | 24 | New York Mets |
| 10 | Seattle Mariners | 25 | Philadelphia Phillies |
| 11 | Tampa Bay Devil Rays | 26 | Pittsburgh Pirates |
| 12 | Texas Rangers | 27 | San Diego Padres |
| 13 | Toronto Blue Jays | 28 | San Francisco Giants |
| 14 | Arizona Diamondbacks | 29 | St. Louis Cardinals |

## Schedule slots are different

The `templateschedule18_*.dat` and `templateschedule32_*.dat` files store 24 generated **season
slots**, numbered 0 through 23. Those numbers are not stable MLB `ETeamID` values. The game builds
the season league from `SeasonOptions` and then converts the selected teams into schedule slots.
For that reason, the Schedule Editor continues to use honest `Team slot` labels instead of showing
an incorrect MLB name.

Changes in this editor affect newly created seasons. A season already saved on a memory card contains
its own runtime league data and is not rewritten by changing `DATA.MET`.

## Recovered executable references

Addresses below refer to the USA `SLUS_208.65` executable loaded at its normal virtual addresses.

| Symbol | Address | Purpose |
|---|---:|---|
| `SeasonOptions::LoadFromIni` | `0x002635F0` | Loads the `[Season]` active/inactive division arrays |
| `SeasonOptions::MakeTeamInactive` | `0x00263B10` | Moves a stable team ID from active to inactive |
| `SeasonOptions::MakeTeamActive` | `0x00263D00` | Moves a stable team ID from inactive to active |
| `SeasonOptions::GetActiveTeams` | `0x00263E10` | Collects the configured active clubs |
| `SeasonOptions::SetHomeFieldID` | `0x00264160` | Sets the runtime home-field selection |
| `SeasonOptions::SetUserTeamID` | `0x00264180` | Sets the runtime user-team selection |
| global `g_seasonOptions` | `0x006658F0` | Runtime season-options object |

Roster membership and existing-season contents are separate runtime/memory-card structures; this
editor does not pretend those are stored in the division INI.
