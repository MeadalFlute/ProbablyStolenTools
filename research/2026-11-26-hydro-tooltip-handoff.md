# 2026-11-26 Held Tooltips and Hydroponic Forecast Handoff

> 最终审查入口已改为 `2026-11-26-hydro-tooltip-final-review-handoff.md`。下文17:58部署、r2/476测试为历史；最终部署18:28、r3/478测试见新文件。玩家实机评分实际为ASCII `-->` 回退，尚无r3公告可见性的明确验收。

## Scope

Version remains 0.9.8, HEAD 817af11, dirty workspace including previous feature batch and DS's 70% changes. Nothing committed, released or pushed. Review the entire dirty workspace, including untracked files; this increment is not covered by the earlier DS review.

Review disposition: `2026-11-26-chemistry-compat-review-receipt.md`.

## Implementation

- `ForcedItemTooltip`: RewindOverlay.LateUpdate draws native target tooltips after drag/tool hints while the configured details key is held. Uses the public GetCurrentItem API and the same fake-background raycast exclusion as native dragging. Group members cannot become their own target. GetTooltip's existing finalization selects detail content when available; otherwise native content remains. Does not consume input, stop actions, invoke tool/pour callbacks or create inventory items. Same-target content is rebuilt at most every 100 ms; target changes immediately. Release, loading and disabling release references and hide the owned display.
- `DetailedCompositionTooltip.AddDetailsHint`: normal tooltips append a brown localized hint only for enabled details, naming the actual binding (either Shift becomes Shift; unbound omits it). Disabled native tooltips remain disabled except the existing Shift faucet override.
- `FacilityProjection`: centralized 1.5% mineral and 1% aggregate contaminant limits; mineral share denominator is water plus mineral, not total liquid. Water must be the unique largest component. Native plant contaminants are heavy_metal, chemical_contaminant, microbe, organic_waste, acid, base. Naming reuses existing chemistry/rename gates and capacity, measuring-vessel/dropper/VBE exclusions.
- `FacilityPreview`: seed score including fractional health; projected score after work; remaining post-consumption/post-water-trait solution and mineral ratio. Native crop-specific size splits are mirrored without RNG; count reductions show bounds, zero-count grades omitted. Nutrifruit supports huge grade and module quality, kotton/meal-cap/dream-cap sizes and module transformations remain distinct; coffee has no invented grades. Fillerweed includes its native small output. Berry quality shows expected counts with an explicit random-quality label; native probabilities never produce the unripe grade at harvest, so zero-probability grades are omitted. Extra seed return counts outside the pre-existing hydroreed forecast are not included.
- `SignboardCompatibility`: supported-build readiness defers all automatic boundaries without consuming them; manual retry message. Unknown builds remain warning-only/adapter skipped. Restore stays strict; see receipt for rejected unsafe degradation suggestions. Method shape checks and null lighting-state guard added.
- Replacement announcement now has eight compact Chinese/English sections, follows the player's five rewritten sections and adds forced tooltips, hydroponic forecasts, nutrient names. Revision `:20261126-chemistry-compat-r2`; 5-second fill bar/static gray track unchanged; no settings file manually reset.

## Validation

- Release build: 0 warnings/errors.
- CheckpointTests: 476 passed, including exact threshold boundaries, aggregate contaminants, overflow-safe mineral denominator, deterministic size conservation/thresholds and announcement structure.
- OptionalModCompatibilityTests: 22 passed.
- Actual game Mono vanilla: passed.
- Actual game Mono with real Workshop Proper Signboards 1.0.48 and VBE: passed. New 408 deterministic crop split cases match native PlantHarvest.SplitByHealth with reduction disabled (no RNG); signboard initialization/busy/fault readiness plus recovery and unknown-build admission checked. Live ClearViews/Commit is not invoked by these fixtures.
- Final ReleaseCompatibility: 105 targets / 76 fields; capabilities files only; HardFlags/Error empty. No new Harmony hook was needed for forced drawing; existing mod overlay supplies LateUpdate.
- git diff --check: passed (existing CRLF normalization warnings only).
- Source inspection used current installed beta assembly, MVID d927a6e4-ae7a-4e65-a5e2-9f9624c131f3. Original stable game execution was not repeated. Universal helper still preserves both known profiles.
- Logs: `out/20261126-hydro-tooltip-{build,tests,optional,fixture-build,compatibility,mono-vanilla,mono-optional,deploy}.log`.

## Deployment

`out/full-release-deploy-20261126-175827/deployment.json`

- Main DLL: `D9B5259FA4F0CA3F48ACFBA416CFE103F5C19AB30768789F02648C6F19C3AB51`.
- Universal helper: `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`, 808448 bytes. Restored merged helper after build; exact installed beta profile verified before deployment.
- 95 save-directory files, settings, other Mods and game managed assemblies unchanged. Game was closed; not launched.

## Live Acceptance

1. Move a bottle over another bottle, pour, use a tool and group-drag: hold details key to inspect the destination, release to restore native hints. Check no source tooltip, stuck tooltip, altered transfer or ignored input. Rebind the key and confirm hints and held behavior follow it; physical Shift-middle pinning stays unchanged.
2. Verify small-container full detail, faucet connection text and ordinary brown hints where native tooltips exist. Check screen edges and tooltip motion.
3. Hydroponic: compare current/projected growth score and next-night remaining solution. A 9700 water/300 mineral/1000 other mixture must display 3% minerals, not 2.73%. Check purifying and polluting seeds, insufficient water/power, continuing growth and actual harvest output.
4. Nutrifruit at health98, adjusted quality<=50, quantity6: large3/huge3, zero small/normal omitted. Check kotton loom, edible/nightmare modules, coffee, fillerweed and berry expected qualities. Confirm localized size labels resolve in Chinese and English. Random outcomes may differ from expectation or range endpoints and must not be described as deterministic.
5. Eligible nutrient name boundaries 1.5%/1%; unchanged measured/dropper/VBE names, item actual name and contents.
6. Supported signboard initialization/drag delays a pending automatic checkpoint and allows it after stabilization; manual busy message is readable. Rewind hanging/removal, then save/reload: real third-party Commit remains a required acceptance case.
7. Announcement replays once, eight real divided sections, no blank paragraphs, five-second gray/blue progress track.
# 18:28 验收后微调

- 工作后剩余量固定两位小数，例如 `6.33L`；仅水培预测使用，通用成分表不变。
- 生长评分合并为 `{当前} → {工作后}`；按实际 tooltip TMP 字体含回退字体检查箭头，无字形时使用 `-->`。水/电不足、未工作时只列当前评分。
- 公告诊断：玩家 XML 已保存 `0.9.8:20261126-chemistry-compat-r2`，所以不是主版本号未改变导致的抑制；旧实现没有成功展示日志，无法还原当时可见性。原生 EmporiumEntry.Start 在载入中同步打开晨报，未发现足以认定触发条件错误的证据。本轮保留原门控、修订 r3，并增加一次已读状态和成功展示日志，未手改设置。
- 478 参考、22 可选兼容、原版及真实 Proper Signboards + VBE Mono 回归通过；最终扫描105挂点/76字段、仅 files、HardFlags/Error空。
- 部署 `out/full-release-deploy-20261126-182854/deployment.json`；主DLL `41208059DA12F27492D83A3D5111920B7C58C79D4027C35F4450D29B2F5FC754`，通用 helper仍 `6129A2D3…`/808448 bytes。95个存档目录文件与设置不变，无启动游戏/提交/升版/推送。实机检查新评分箭头及 r3 公告可见性。

