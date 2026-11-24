# Printing Settings performance results

21 November 2026 · milliseconds elapsed from the Settings link click

## Historical and reproduced baseline

The [original audit](printer-settings-performance-analysis.md) reported median Edge control readiness of 80 ms with a fast fixture and 1,393 ms with a controlled 1,250 ms discovery response. The separately sequenced read-only Windows probe reported 1,299.61 ms median and 2,372.13 ms maximum for full discovery, and 1.18 ms median for installed-name lookup. The probe used one virtual Windows printer. Those Windows times are historical and were collected outside the browser.

The pre-edit audit fixture ran again in Edge 158.0.4370.33 at 1366×768 Light, with a fresh document load for each trial. The controls first appeared at 90, 84, 88, 86 and 73 ms with fast discovery, median **86 ms**. Under 1,250 ms discovery they appeared at 1,380, 1,387, 1,406, 1,402 and 1,375 ms, median **1,387 ms**. Each trial made one discovery request. The old refresh marker was created at the same time as the saved controls. Raw [pre-edit capture and timing index](../artifacts/settings-printing-implementation/before/before.json).

## Post-edit Edge timings

| Fixture | Trials | Navigation click completed | First content observed | Profiles observed | Assignments observed | Device control ready | Full readiness observed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Fast | 5 | 71 | 144 | 148 | 153 | 191 | 196 |
| 1,250 ms discovery | 5 | 64 | 118 | 127 | 131 | 1,330 | 1,335 |
| 3,000 ms discovery | 2 | 67 | 90 | 108 | 113 | 3,064 | 3,068 |

Values are medians. With 1,250 ms of simulated discovery latency, median assignment visibility was **131 ms**, compared with **1,387 ms** before. Profile and assignment availability stayed within the fast fixture range when discovery was delayed. The richer post-edit fixture includes two populated profiles, all nine explicit assignments, barcode initialization and additional locator checks. Its fast median is above 80 ms and includes serial automation observation overhead. First content is the observed printer manager shell. Full readiness requires both the Windows refresh control and the label settings controls to be ready. Request and response-dispatch timestamps are in the [post-edit JSON](../artifacts/settings-printing-implementation/after.json).

A retained-tab activation showed assignments in 83 ms in this run and made zero printer discovery requests. Every ordinary mount in the timed matrix made one request. Repeated rapid refresh events, tab switching during a scan, and closing and reopening while the scan was pending each made one request for that scan. Failure returned a localized status with saved assignments still visible, and a subsequent retry succeeded. Successful zero results produced separate status.

These fixtures intercept browser HTTP traffic. Navigation click completion and locator visibility are automation observations. SQLite latency, a fresh Windows probe, native WebView2 startup and physical print timing were outside this measurement.

## Matched fixture replay

A second post-edit run reused the pre-edit audit fixture, the same Edge version, the same viewport and its first-visible refresh marker. Under fast discovery it observed 271, 82, 93, 81 and 71 ms, median **82 ms**. Under 1,250 ms delay it observed 80, 69, 82, 80 and 86 ms, median **80 ms**. Each trial made one discovery request. This marker now appears with saved controls while the device refresh button remains busy. Device readiness is measured separately in the populated fixture above. [Matched-run raw data](../artifacts/settings-printing-implementation/matched-after.json)

A fresh Windows backend probe was attempted and blocked by the host's Windows PowerShell script execution policy. There is no new Windows backend timing result.
