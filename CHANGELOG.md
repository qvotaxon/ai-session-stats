# Changelog

## [1.4.0](https://github.com/qvotaxon/ai-session-stats/compare/v1.3.0...v1.4.0) (2026-11-28)


### Features

* **evolve:** log analysis duration, input size and output size ([#116](https://github.com/qvotaxon/ai-session-stats/issues/116)) ([7e72b12](https://github.com/qvotaxon/ai-session-stats/commit/7e72b12486950f82a41ba2d7526c40d98440aadf))
* show Claude subscription allowance ([#34](https://github.com/qvotaxon/ai-session-stats/issues/34)) ([9fa278d](https://github.com/qvotaxon/ai-session-stats/commit/9fa278dfaec81a16cc8313c90cef1093cdb308f0))
* **versioning:** configure harness plugin and marketplace names ([#114](https://github.com/qvotaxon/ai-session-stats/issues/114)) ([05d9ffe](https://github.com/qvotaxon/ai-session-stats/commit/05d9ffe49d53406ca117fb48ab56c3ad3a282dc2))


### Bug Fixes

* **evolve:** lock down Evolve's Copilot analysis and remove its leftover session ([#105](https://github.com/qvotaxon/ai-session-stats/issues/105)) ([e2b9bd3](https://github.com/qvotaxon/ai-session-stats/commit/e2b9bd31ee49f770eaae18b288d56a2d2ee68203))
* **evolve:** remove false "uses no AI models" claim from page heading ([#96](https://github.com/qvotaxon/ai-session-stats/issues/96)) ([d255f0d](https://github.com/qvotaxon/ai-session-stats/commit/d255f0d25b8ddaa48f02b36aa4213ade78bebb96)), closes [#49](https://github.com/qvotaxon/ai-session-stats/issues/49)
* **evolve:** say where Evolve's AI requests go ([#123](https://github.com/qvotaxon/ai-session-stats/issues/123)) ([4200305](https://github.com/qvotaxon/ai-session-stats/commit/4200305e953de4a8ee577d49343a74b78c140689))
* keep each host's Serilog logger instead of the static one ([#89](https://github.com/qvotaxon/ai-session-stats/issues/89)) ([2c9aae8](https://github.com/qvotaxon/ai-session-stats/commit/2c9aae86a8d85ccaca600f2cd2058449d7f3e9ca)), closes [#87](https://github.com/qvotaxon/ai-session-stats/issues/87)
* make the Evolve handler test independent of random session ids ([#92](https://github.com/qvotaxon/ai-session-stats/issues/92)) ([56560cc](https://github.com/qvotaxon/ai-session-stats/commit/56560cc6c586a1fb71c7b5f071f37dfc47db3a7c)), closes [#88](https://github.com/qvotaxon/ai-session-stats/issues/88)
* refresh Claude allowance when statusline file already exists ([#37](https://github.com/qvotaxon/ai-session-stats/issues/37)) ([67fcc0a](https://github.com/qvotaxon/ai-session-stats/commit/67fcc0a1b72479010e305a63870375b6448a9c63))
* **reports:** say where report requests go ([#129](https://github.com/qvotaxon/ai-session-stats/issues/129)) ([0cc271b](https://github.com/qvotaxon/ai-session-stats/commit/0cc271bd7a2c008307c46181bf1b54bbf0670723))

## [1.3.0](https://github.com/qvotaxon/ai-session-stats/compare/v1.2.0...v1.3.0) (2026-11-18)


### Features

* add local Evolve skill opportunity analysis ([#28](https://github.com/qvotaxon/ai-session-stats/issues/28)) ([4825c31](https://github.com/qvotaxon/ai-session-stats/commit/4825c31b7d536af8109cf642b860942d8951f2b1))
* add MSDC harness plugin update workflow ([#30](https://github.com/qvotaxon/ai-session-stats/issues/30)) ([3dd858b](https://github.com/qvotaxon/ai-session-stats/commit/3dd858bb4a74549fdcd042a5204413da69b6b596))
* **sessions:** add numbered pagination ([aeae05f](https://github.com/qvotaxon/ai-session-stats/commit/aeae05fbb1513986644d95d85ea8e6556c8c4c3a))


### Bug Fixes

* resolve Copilot harness version detection ([f24e2b7](https://github.com/qvotaxon/ai-session-stats/commit/f24e2b7b2e609e45ea50d56033785e3213d1b4e0))
* serve a loading dashboard before initialization finishes ([#32](https://github.com/qvotaxon/ai-session-stats/issues/32)) ([1ba15a9](https://github.com/qvotaxon/ai-session-stats/commit/1ba15a9cc20e9fc6c5e0ac25239bf0e1226c496b))
* show startup page while server launches ([#31](https://github.com/qvotaxon/ai-session-stats/issues/31)) ([4c3fe7b](https://github.com/qvotaxon/ai-session-stats/commit/4c3fe7b1983f69a73798b8960bfc40d30087f779))

## [1.2.0](https://github.com/qvotaxon/ai-session-stats/compare/v1.1.0...v1.2.0) (2026-11-13)


### Features

* add application log viewer and improve CLI discovery ([#20](https://github.com/qvotaxon/ai-session-stats/issues/20)) ([d697dee](https://github.com/qvotaxon/ai-session-stats/commit/d697deea5bc1efda267513d57ebca5a2a16fd345))
* add dashboard and MSDC harness version insights ([#25](https://github.com/qvotaxon/ai-session-stats/issues/25)) ([5a5c1d8](https://github.com/qvotaxon/ai-session-stats/commit/5a5c1d84cc56244d4ca4da721efdfdf69c9f0a56))
* add offline changelog page and tray navigation ([#22](https://github.com/qvotaxon/ai-session-stats/issues/22)) ([6e929a4](https://github.com/qvotaxon/ai-session-stats/commit/6e929a425227ed646d30c7822bf78b063a65e34b))
* publish manually installable preview releases ([#23](https://github.com/qvotaxon/ai-session-stats/issues/23)) ([d458a0e](https://github.com/qvotaxon/ai-session-stats/commit/d458a0e9c059223d5d4cce8bc485b89d2614be78))


### Bug Fixes

* detect Claude harness versions in JSON-escaped hook output ([#26](https://github.com/qvotaxon/ai-session-stats/issues/26)) ([16bb2b2](https://github.com/qvotaxon/ai-session-stats/commit/16bb2b2750ec5dc61e2be96c239cbb87772fc139))
* gate release publication behind owner approval ([#24](https://github.com/qvotaxon/ai-session-stats/issues/24)) ([76cc541](https://github.com/qvotaxon/ai-session-stats/commit/76cc541a85c5e368fd4b8443293fcbbe3ccd125f))

## [1.1.0](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.6...v1.1.0) (2026-11-12)


### Features

* add a per-user Windows installer that keeps self-update working ([#19](https://github.com/qvotaxon/ai-session-stats/issues/19)) ([cd8770b](https://github.com/qvotaxon/ai-session-stats/commit/cd8770be3e35ff3244a3d02291902f1436db9f94))


### Bug Fixes

* prefer the .cmd shim when locating npm-installed CLIs ([#17](https://github.com/qvotaxon/ai-session-stats/issues/17)) ([4ea038f](https://github.com/qvotaxon/ai-session-stats/commit/4ea038fcea098fcddb5aa15f172dfe96517fa451))

## [1.0.6](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.5...v1.0.6) (2026-11-11)


### Bug Fixes

* trigger patch release to verify the longer update health wait ([#15](https://github.com/qvotaxon/ai-session-stats/issues/15)) ([df9546e](https://github.com/qvotaxon/ai-session-stats/commit/df9546e68d43ed314ddf488159d4da727f2d7e4a))

## [1.0.5](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.4...v1.0.5) (2026-11-11)


### Bug Fixes

* wait longer for the updated server and retry locked directory moves ([#13](https://github.com/qvotaxon/ai-session-stats/issues/13)) ([b021923](https://github.com/qvotaxon/ai-session-stats/commit/b021923c813970152bc16aa18484f04fcdc0f4d1))

## [1.0.4](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.3...v1.0.4) (2026-11-10)


### Bug Fixes

* handle release readiness, update errors, and lock cleanup ([#11](https://github.com/qvotaxon/ai-session-stats/issues/11)) ([71c026d](https://github.com/qvotaxon/ai-session-stats/commit/71c026dc04b1612ee00ecb880a926d4a2dba5269))

## [1.0.3](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.2...v1.0.3) (2026-11-10)


### Bug Fixes

* run tray updates silently in the background ([#9](https://github.com/qvotaxon/ai-session-stats/issues/9)) ([fafa2ad](https://github.com/qvotaxon/ai-session-stats/commit/fafa2adb224a907faf180d15f2e02f742195a927))

## [1.0.2](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.1...v1.0.2) (2026-11-10)


### Bug Fixes

* handle updater task launch errors and application file locks ([#7](https://github.com/qvotaxon/ai-session-stats/issues/7)) ([585fe55](https://github.com/qvotaxon/ai-session-stats/commit/585fe55f5bb6f931c3d7f64751572887265825de))

## [1.0.1](https://github.com/qvotaxon/ai-session-stats/compare/v1.0.0...v1.0.1) (2026-11-10)


### Bug Fixes

* trigger patch release to verify tray updates ([2aeef93](https://github.com/qvotaxon/ai-session-stats/commit/2aeef93b80e4c1bcd53970508588d437f6c40588))

## 1.0.0 (2026-11-10)


### Features

* add automatic tray updates ([43ec103](https://github.com/qvotaxon/ai-session-stats/commit/43ec1033af9025cddad3411b553792eef7c7f688))
