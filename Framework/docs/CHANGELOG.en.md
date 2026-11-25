Updated: Oct 17, 9:44 PM
Author: 中津静流
Beta v0.0
Basic functionality now works end to end

Updated: Oct 18, 7:23 AM
Author: 中津静流
Beta v0.1
Adapted Pick Up And Haul

Updated: Oct 18, 12:50 PM
Author: 中津静流
Beta v0.2
Compatible with complex entities such as corpses and buildings

Updated: Oct 18, 4:34 PM
Author: 中津静流
Beta v0.3
Adapted Combat Extended

Updated: Oct 18, 10:20 PM
Author: 中津静流
Beta v0.4
Fixed a workbench bug when crafting with multiple ingredients

Updated: Oct 19, 6:03 AM
Author: 中津静流
Beta v0.5
Fixed incorrect quantities when feeding babies and slaughtering.

Updated: Oct 19, 8:03 PM
Author: 中津静流
Release v1.0
Refactored the underlying code so that most materials (except corpses, buildings, sculptures, books, etc.) satisfy vanilla's see-it / touch-it / take-it-out logic, naturally adapting to the vast majority of mods

Updated: Oct 20, 6:05 AM
Author: 中津静流
Release v1.1
Fixed a bug where the player faction's starting techs were already researched; improved the mod's performance under extremely high load

Updated: Oct 21, 8:43 PM
Author: 中津静流
Release v2.0
1. This mod completed its second refactor; the main work was unifying the logic chains of the five vanilla paths — "See", "Read", "Find", "Take", "Store".
2. In this update, patches are written against vanilla function APIs, fully decoupled from specific work types and specific workbench bills, greatly improving the mod's compatibility and reliability with vanilla content and with mods that call vanilla function APIs.

Updated: Oct 22, 6:46 AM
Author: 中津静流
Release v2.0.1
Improved log output for easier diagnosis

Updated: Oct 22, 6:20 PM
Author: 中津静流
Release v2.1
Consolidated the underlying logic and unified the flow, improving the mod's compatibility.

Updated: Oct 23, 7:01 AM
Author: 中津静流
Release v2.1.1
Improved the UI and interaction: the UI became a side panel that no longer blocks the player's view; added the Hyperdimensional Space hotkey (C), the Output hotkey (Q), and the Output confirm hotkey (Enter/middle mouse button); streamlined the player's workflow.

Updated: Oct 23, 9:08 PM
Author: 中津静流
Release v2.1.2
1. Improved the Pick Up And Haul patch
2. Consolidated one previously missed vanilla hauling logic path

Updated: Oct 24, 6:36 AM
Author: 中津静流
Release v2.1.3
Added a Use button with hotkey E. Colonists with a chip can directly use/wear items inside Hyperdimensional Space, skipping the output-then-right-click-use flow

Updated: Oct 24, 9:20 AM
Author: 中津静流
Release v2.2
1. Added adapters so third-party mods can use the Hyperdimensional Space stock normally.
2. Currently adapted: "UF Light Industries Furniture" + "Milira Expanded - Future Technology"

Updated: Oct 24, 6:03 PM
Author: 中津静流
Release v2.2.1
The Hyperdimensional Space UI now includes three icons and features from the vanilla container: weight, whether colonists can interact, and info.

Updated: Oct 24, 9:24 PM
Author: 中津静流
Release v2.3
Expanded the scope of preserved items (non-rebuildable), ensuring items from third-party mods that are not manufactured goods, food, raw materials, or chunk piles can be stored properly

Updated: Oct 24, 9:57 PM
Author: 中津静流
Release v2.3.1
Fixed the quest blueprint bug reported by players

Updated: Oct 25, 7:30 AM
Author: 中津静流
Release v2.3.2
Fixed the consistency between UI counts and availability, preventing cases where a number is shown but the items cannot be taken out.

Updated: Oct 25, 1:47 PM
Author: 中津静流
Release v2.3.3
Milira Expanded - Future Technology updated to 1.7; this mod was re-adapted so it properly calls the Hyperdimensional Storage stock

Updated: Oct 25, 4:20 PM
Author: 中津静流
Release v2.3.4
1. Fixed the issue where stock could not be taken out after category/material merging
2. Fixed issues when preserved items (non-rebuildable objects) participate in transport pod loading / caravan formation / trading with colonies

Updated: Oct 25, 7:36 PM
Author: 中津静流
Release v2.3.5
Fixed an issue where switching maps with the gravship (destroying the map) caused part of the Hyperdimensional Space stock materials exceeding stack limits to be lost.

Updated: Oct 26, 6:42 AM
Author: 中津静流
Release v2.3.6
1. Added destruction support for the vanilla channel, ensuring third-party mods that call destruction properly deduct from the Hyperdimensional Space stock
2. Added "ProjectE" to the third-party adapters; narrowed the UF Light Industries adapter's scope to preserved items (non-rebuildable)

Updated: Oct 26, 9:54 PM
Author: 中津静流
Release v2.4
1. Added complete external API interfaces for See, Read, Find, Take, Store
2. Adapted the Manipulator Beam Emitter mod

Updated: Oct 27, 7:20 PM
Author: 中津静流
Release v2.4.1
1. Improved the public API
2. Improved some log output
3. Fixed one omission in the rollback flow
4. Added the feature that hotkeys always yield to vanilla hotkeys

Updated: Oct 28, 6:56 AM
Author: 中津静流
Release v2.4.2
1. Put the API documentation into the mod files
2. Fixed an issue introduced by the optimized code logic where caravans counted colonists as stock

Updated: Oct 28, 6:31 PM
Author: 中津静流
Release v2.4.3
1. Improved the uniqueness of the code logic and the third-party mod adapter logic
2. Added a setting so the hidden vault does not participate in NPC stealing decisions
3. Added a harmless setting for implants to prevent them from being purged, though they are still destroyed on death
4. Narrowed the API scope, internalizing redundant APIs

Updated: Oct 29, 5:03 AM
Author: 中津静流
Release v2.4.4
Replaced building and implant textures.
Thanks to "PSIONIC海豹" for the art support.
Updated: Oct 29, 5:22 AM
Author: 中津静流
Release v2.4.4
Replaced preview images
Updated: Oct 29, 12:12 PM
Author: 中津静流
Release v2.4.4
Updated art assets.
Thanks to "PSIONIC海豹" for the art support.
Updated: Oct 29, 6:21 PM
Author: 中津静流
Release v2.4.4
1. Improved art effects
2. Disabled texture rotation
3. Moved the anchor's interaction cell onto its own tile
4. Added satellite buildings to the Hyperdimensional Core — the Stabilizer and the Hyperdimensional Anchor

Updated: Oct 30, 8:44 PM
Author: 中津静流
Release v2.4.5
When the vanilla target is a storage zone cell rather than a storage container, this mod automatically yields to vanilla. Examples: toxic waste / egg boxes / reliquaries

Updated: Nov 2, 6:34 AM
Author: 中津静流
Release v2.4.6
Fixed the missed turret maintenance logic, unified into the JobDriver_Refuel family to avoid omissions

Updated: Nov 3, 6:16 AM
Author: 中津静流
Release v2.4.7
Improved control when outputting buildings: you can preview how the building looks and adjust its direction, consistent with vanilla

Updated: Nov 3, 12:31 PM
Author: 中津静流
Release v2.4.8
Taking B (items) from A (buildings) and hauling to C (cells, non-containers) is unified as WorkDriver interception instead of enumeration, so that when third-party mods call vanilla WorkDrivers, Hyperdimensional Space endpoints are properly excluded, preventing colonists from getting stuck in job error loops.

Updated: Nov 4, 6:03 AM
Author: 中津静流
Release v2.4.9
Added a mod setting to force-virtualize preserved-type weapons/apparel that carry color/style/art/shield charge

Updated: Nov 4, 5:55 PM
Author: 中津静流
Release v2.4.10
1. Fixed game freezes possibly caused by mod settings
2. Improved text descriptions
3. Added a simple manual

Updated: Nov 7, 4:47 PM
Author: 中津静流
Release v2.4.11
1. Corrected the chip category to bionic
2. Added the missed medical care tier limit during surgery
3. All researchable items unconditionally go into preserved, preventing the research progress of anomaly items from being wiped in vanilla and third-party mods

Updated: Nov 7, 7:50 PM
Author: 中津静流
Release v2.4.12
The hidden vault now automatically changes position, preventing it from occupying the old location after the Core moves.

Updated: Nov 8, 4:00 PM
Author: 中津静流
Release v2.4.13
Updated the adapter alongside the Manipulator Beam Emitter update

Updated: Nov 9, 6:47 AM
Author: 中津静流
Release v2.4.14
1. Extended the JobDriver_Refuel family from a "driver family" to "outfitting job capability", so third-party take quantities correctly match vanilla logic
2. Adapted Glitterworld Destroyer 5's precision armor plate logic via the "outfitting job capability"
3. Fixed the issue where SuperDimensional Storage in the build menu displayed the power grid overlay

Updated: Nov 10, 9:08 PM
Author: 中津静流
Release v2.5
1. Corrected the single orbital trade ship to the vanilla ITrader / PassingShip contract, so it properly adapts to third-party orbital traders such as the Milira imperial orbital trader.
2. Optimized the preserved channel to materialize first at the CompTransporter/MapPortal entrance, fixing the issue of getting stuck during loading.
3. Added the missed tainted apparel state, distinguishing clean apparel from dead-pawn apparel, preventing errors when both are withdrawn at the same time

Updated: Nov 11, 6:47 AM
Author: 中津静流
Release v2.5.1
Improved edge cases of CompTransporter/MapPortal loading — loading multiple pods simultaneously, saving/loading during loading, and items properly returning to the Hyperdimensional Space stock when loading fails

Updated: Nov 11, 12:22 PM
Author: 中津静流
Release v2.5.2
Adapted the CeleTech Shuttle Extension loading manifest

Updated: Nov 12, 9:51 PM
Author: 中津静流
Release v3.0
Third refactor: completed physical + logical isolation of all modules, laying the foundation for future agile development

Updated: Nov 13, 6:59 AM
Author: 中津静流
Release v3.0.1
1. Fixed the issue where the CeleTech shuttle required someone inside to see the Hyperdimensional stock
2. Fixed the issue where the Mech gestator could not recognize stored mechanoid corpses when reviving light mechs

Updated: Nov 13, 12:14 PM
Author: 中津静流
Release v3.0.2
1. Improved old-save compatibility
2. Restored the old-version performance optimization strategies missed in the refactor
3. Added new performance optimization strategies

Updated: Nov 13, 8:17 PM
Author: 中津静流
Release v3.0.3
1. Allowed the Hyperdimensional Space stock to be withheld from wild animals, raiders, prisoners, visitors, and quest visitors; enabled in the MOD settings
2. Allowed the Hyperdimensional Space stock to participate in vanilla container priority comparison and auto-outflow items; enabled in the MOD settings
3. Building on priority outflow, corrected the Hyperdimensional Space top-priority setting; enabled in the MOD settings

Updated: Nov 14, 7:29 AM
Author: 中津静流
Release v3.0.4
1. Fixed the CQE hotkeys not working on orbital maps
2. Fixed the count-criteria consistency drift caused by splitting

Updated: Nov 14, 4:12 PM
Author: 中津静流
Release v3.0.5
Added third-party MOD support — VC Vehicle Framework "VersatranZeroOne"

Updated: Nov 14, 7:33 PM
Author: 中津静流
Release v3.0.6
Improved the underlying logic, eliminated the second implementation, achieving a single source of rules

Updated: Nov 16, 5:51 AM
Author: 中津静流
Release v3.1
The Core module stripped out redundant logic and converged into pure ledger rules + injected facts; it can now be tested independently, improving the mod's data reliability + count-criteria consistency

Updated: Nov 16, 6:41 PM
Author: 中津静流
Release v3.2
1. The core module was further split into the pure logic block "rules", returning to pure ledger data
2. Removed compatibility code from before version 2.4 (Oct 26)

Updated: Nov 17, 6:31 AM
Author: 中津静流
Release v3.2.1
1. The JobDriver_Refuel family + third-party MOD adaptation converged into "default Hand + 1 quantity rule + 1 set of consumption seams"; as long as a third-party MOD reserves a quantity or writes job.count, take quantities adapt automatically — no registration needed, no knowledge of its functions needed.
2. Removed the useless refuel / fermentation / registration outfitting branches and ClampToTrip; removed the useless Glitterworld Destroyer 5 adaptation patch.

Updated: Nov 17, 7:05 PM
Author: 中津静流
Release v3.2.2
1. Improved the SDS.Vanilla flow, continuing to converge toward flow uniqueness and semantic uniqueness.
2. Added manifest rebuilds for preserved items after save-load during transport pod / vehicle loading, and optimized rebuild latency.

Updated: Nov 19, 6:44 PM
Author: 中津静流
Release v3.2.3
Built a generic interface for third-party MODs that require the true body to be near the pickup point when taking, and adapted the third-party MOD — Milira Raising: The One Entwined With You

Updated: Nov 21, 6:10 PM
Author: 中津静流
Release v3.2.4
The Hyperdimensional Space UI now displays the channel each stock item belongs to, along with an explanation of the difference between the virtual channel and the preserved channel.

Updated: Nov 24, 7:05 PM
Author: 中津静流
Release v3.2.5
1. When taking B (items) from A (buildings) and hauling to C (cells, non-containers), items go straight into Hyperdimensional Space; Hyperdimensional Space can be treated as a vanilla storage zone cell, with no need to designate a storage zone as an intermediary.
2. Improved the logic flow: removed the colonist/mechanoid endpoint special case; all endpoints now participate uniformly in vanilla priority and distance comparison, depositing directly only when the final destination is Hyperdimensional Space and its own endpoint is available.

Author: 中津静流
Release v3.2.6
Improved the mirror lifecycle: fill-type workbenches from mods such as the Vanilla Expanded (VE) series no longer throw errors when holding mirrors for extended periods.
